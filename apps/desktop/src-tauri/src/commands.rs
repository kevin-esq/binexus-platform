use std::path::PathBuf;
use std::sync::Arc;

use parking_lot::Mutex;
use tauri::{AppHandle, State};
use uuid::Uuid;

use crate::branch::{validate_branch_url, BranchClient};
use crate::config::{ConfigStatus, ConfigStore, DesktopConfig};
use crate::crypto;
use crate::error::{AppError, AppResult};
use crate::pairing::PairingOrchestrator;
use crate::secrets::{KeyringSecretStore, PairingEnvelope, SecretEnvelopeV1, SecretStore};
use crate::state::AppUiState;

pub struct AppContext {
    pub config: Arc<Mutex<DesktopConfig>>,
    pub config_store: Arc<ConfigStore>,
    pub secrets: Arc<dyn SecretStore>,
    pub orchestrator: Mutex<Option<Arc<PairingOrchestrator>>>,
    pub terminal_name: Mutex<Option<String>>,
    /// True when `config.json` was absent at process start (distinct from Uninitialized-with-file).
    pub config_file_missing_on_boot: std::sync::atomic::AtomicBool,
}

impl AppContext {
    pub fn new(app_data: PathBuf) -> AppResult<Self> {
        let config_store = Arc::new(ConfigStore::new(app_data));
        let loaded = config_store.load()?;
        let config_file_missing_on_boot = loaded.is_none();
        let config = loaded.unwrap_or_default();
        Ok(Self {
            config: Arc::new(Mutex::new(config)),
            config_store,
            secrets: Arc::new(KeyringSecretStore::new()),
            orchestrator: Mutex::new(None),
            terminal_name: Mutex::new(None),
            config_file_missing_on_boot: std::sync::atomic::AtomicBool::new(
                config_file_missing_on_boot,
            ),
        })
    }

    pub fn ensure_orchestrator(&self, app: AppHandle) -> AppResult<()> {
        let mut slot = self.orchestrator.lock();
        if slot.is_none() {
            *slot = Some(Arc::new(PairingOrchestrator::new(
                app,
                Arc::clone(&self.config),
                Arc::clone(&self.config_store),
                Arc::clone(&self.secrets),
            )));
        }
        Ok(())
    }

    fn orchestrator(&self) -> AppResult<Arc<PairingOrchestrator>> {
        self.orchestrator
            .lock()
            .as_ref()
            .cloned()
            .ok_or(AppError::Internal)
    }

    pub fn resolve_ui_state(&self) -> AppUiState {
        let config = self.config.lock().clone();
        let envelope = self.secrets.get().ok().flatten();
        let has_envelope = envelope.is_some();
        // Identity-derived display fingerprint — independent of poller / receipt / status token.
        let fingerprint = envelope.as_ref().and_then(|env| {
            crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).ok()
        });
        let terminal_name = self.terminal_name.lock().clone().unwrap_or_default();

        // Approved recovery rule: config.json missing + envelope present → RecoveryRequired.
        // Fresh identity with Uninitialized config file is NeedsServerSetup, not recovery.
        if self
            .config_file_missing_on_boot
            .load(std::sync::atomic::Ordering::SeqCst)
            && has_envelope
            && config.branch_base_url.is_none()
        {
            return AppUiState::RecoveryRequired {
                message: Some("Local secrets exist but configuration is missing.".into()),
                device_fingerprint_short: fingerprint,
            };
        }

        if let (Some(env), Some(cfg_id)) = (envelope.as_ref(), config.device_id) {
            if env.device_id != cfg_id {
                return AppUiState::RecoveryRequired {
                    message: Some("Device identity does not match local configuration.".into()),
                    device_fingerprint_short: fingerprint,
                };
            }
        }

        if config.status == ConfigStatus::Paired && !has_envelope {
            return AppUiState::PairedCredentialsUnavailable {
                message: Some("Device is marked paired but secure credentials are missing.".into()),
            };
        }

        derive_from_config(&config, has_envelope, fingerprint, terminal_name)
    }

    /// Stable short fingerprint from the local device identity (PKCS#8), if present.
    pub fn identity_fingerprint_short(&self) -> Option<String> {
        self.secrets.get().ok().flatten().and_then(|env| {
            crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).ok()
        })
    }
}

fn derive_from_config(
    config: &DesktopConfig,
    has_envelope: bool,
    fingerprint: Option<String>,
    terminal_name: String,
) -> AppUiState {
    match config.status {
        ConfigStatus::Uninitialized => {
            let _ = has_envelope;
            AppUiState::NeedsServerSetup {
                branch_url: None,
                message: None,
                device_fingerprint_short: fingerprint,
            }
        }
        ConfigStatus::ServerConfigured => AppUiState::NeedsPairing {
            branch_url: config.branch_base_url.clone().unwrap_or_default(),
            device_name: None,
            message: None,
            device_fingerprint_short: fingerprint,
        },
        ConfigStatus::PairingInProgress => {
            let branch_url = config.branch_base_url.clone().unwrap_or_default();
            if let Some(request_id) = config.pairing_request_id {
                AppUiState::PendingApproval {
                    branch_url,
                    pairing_request_id: request_id,
                    device_fingerprint_short: fingerprint,
                    terminal_name,
                }
            } else {
                AppUiState::NeedsPairing {
                    branch_url,
                    device_name: None,
                    message: None,
                    device_fingerprint_short: fingerprint,
                }
            }
        }
        ConfigStatus::Paired => {
            if !has_envelope {
                return AppUiState::PairedCredentialsUnavailable {
                    message: Some(
                        "Device is marked paired but secure credentials are missing.".into(),
                    ),
                };
            }
            AppUiState::Paired {
                branch_url: config.branch_base_url.clone().unwrap_or_default(),
                terminal_name,
                device_fingerprint_short: fingerprint,
            }
        }
        ConfigStatus::RecoveryRequired => AppUiState::RecoveryRequired {
            message: Some("Assisted recovery is required.".into()),
            device_fingerprint_short: fingerprint,
        },
        ConfigStatus::PairedCredentialsUnavailable => AppUiState::PairedCredentialsUnavailable {
            message: Some("Paired credentials are unavailable.".into()),
        },
    }
}

fn map_err(error: AppError) -> String {
    error.code().to_string()
}

#[tauri::command]
pub fn get_app_state(ctx: State<'_, AppContext>) -> Result<AppUiState, String> {
    Ok(ctx.resolve_ui_state())
}

#[tauri::command]
pub fn initialize_device(ctx: State<'_, AppContext>) -> Result<AppUiState, String> {
    let envelope = ctx.secrets.get().map_err(map_err)?;
    let mut config = ctx.config.lock();

    if ctx
        .config_file_missing_on_boot
        .load(std::sync::atomic::Ordering::SeqCst)
        && envelope.is_some()
        && config.branch_base_url.is_none()
    {
        config.status = ConfigStatus::RecoveryRequired;
        ctx.config_store.save(&config).map_err(map_err)?;
        return Ok(AppUiState::RecoveryRequired {
            message: Some("Local secrets exist but configuration is missing.".into()),
            device_fingerprint_short: envelope.as_ref().and_then(|env| {
                crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).ok()
            }),
        });
    }

    if envelope.is_none() {
        if matches!(
            config.status,
            ConfigStatus::Paired | ConfigStatus::PairingInProgress | ConfigStatus::RecoveryRequired
        ) {
            // Never auto-regenerate identity when prior state implies secrets should exist.
            if config.status == ConfigStatus::Paired {
                config.status = ConfigStatus::PairedCredentialsUnavailable;
                ctx.config_store.save(&config).map_err(map_err)?;
                return Ok(AppUiState::PairedCredentialsUnavailable {
                    message: Some("Paired credentials are unavailable.".into()),
                });
            }
            return Ok(ctx.resolve_ui_state());
        }

        let device_id = Uuid::now_v7();
        let (pkcs8, _public) = crypto::generate_key_material().map_err(map_err)?;
        let mut credential = [0u8; 32];
        rand::RngCore::fill_bytes(&mut rand::thread_rng(), &mut credential);
        let credential_b64 = {
            use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
            URL_SAFE_NO_PAD.encode(credential)
        };
        let secret = SecretEnvelopeV1 {
            schema_version: SecretEnvelopeV1::SCHEMA_VERSION,
            device_id,
            private_key_pkcs8_base64: pkcs8,
            device_credential_base64url: credential_b64,
            pairing: PairingEnvelope::default(),
        };
        ctx.secrets.set(&secret).map_err(map_err)?;
        config.device_id = Some(device_id);
        if config.branch_base_url.is_none() {
            config.status = ConfigStatus::Uninitialized;
        }
        ctx.config_store.save(&config).map_err(map_err)?;
        ctx.config_file_missing_on_boot
            .store(false, std::sync::atomic::Ordering::SeqCst);
    }

    drop(config);
    Ok(ctx.resolve_ui_state())
}

#[tauri::command]
pub async fn configure_branch_url(
    app: AppHandle,
    ctx: State<'_, AppContext>,
    branch_url: String,
) -> Result<AppUiState, String> {
    let url = validate_branch_url(&branch_url).map_err(map_err)?;
    let client = BranchClient::new(url.clone()).map_err(map_err)?;
    client.health().await.map_err(map_err)?;

    let mut config = ctx.config.lock();
    if ctx.secrets.get().map_err(map_err)?.is_none() {
        return Err(map_err(AppError::CredentialsUnavailable));
    }
    config.branch_base_url = Some(url.as_str().trim_end_matches('/').to_string());
    config.status = ConfigStatus::ServerConfigured;
    ctx.config_store.save(&config).map_err(map_err)?;
    drop(config);

    ctx.ensure_orchestrator(app).map_err(map_err)?;
    Ok(ctx.resolve_ui_state())
}

#[tauri::command]
pub async fn begin_pairing(
    app: AppHandle,
    ctx: State<'_, AppContext>,
    pairing_code: String,
    terminal_name: String,
) -> Result<AppUiState, String> {
    ctx.ensure_orchestrator(app.clone()).map_err(map_err)?;
    *ctx.terminal_name.lock() = Some(terminal_name.clone());
    let orchestrator = ctx.orchestrator().map_err(map_err)?;
    orchestrator
        .begin(pairing_code, terminal_name)
        .await
        .map_err(map_err)?;
    orchestrator.resume().await.map_err(map_err)?;
    Ok(ctx.resolve_ui_state())
}

#[tauri::command]
pub async fn cancel_pairing(ctx: State<'_, AppContext>) -> Result<AppUiState, String> {
    if let Some(orchestrator) = ctx.orchestrator.lock().as_ref() {
        orchestrator.cancel();
    }
    let mut config = ctx.config.lock();
    if config.status == ConfigStatus::PairingInProgress {
        config.status = ConfigStatus::ServerConfigured;
        config.pairing_request_id = None;
        ctx.config_store.save(&config).map_err(map_err)?;
    }
    if let Ok(Some(mut envelope)) = ctx.secrets.get() {
        envelope.pairing = PairingEnvelope::default();
        let _ = ctx.secrets.set(&envelope);
    }
    drop(config);
    Ok(ctx.resolve_ui_state())
}

#[tauri::command]
pub async fn resume_pairing(
    app: AppHandle,
    ctx: State<'_, AppContext>,
) -> Result<AppUiState, String> {
    ctx.ensure_orchestrator(app).map_err(map_err)?;
    let orchestrator = ctx.orchestrator().map_err(map_err)?;
    orchestrator.resume().await.map_err(map_err)?;
    Ok(AppUiState::Finalizing {
        branch_url: ctx
            .config
            .lock()
            .branch_base_url
            .clone()
            .unwrap_or_default(),
        terminal_name: ctx.terminal_name.lock().clone().unwrap_or_default(),
        device_fingerprint_short: ctx.identity_fingerprint_short(),
    })
}

#[tauri::command]
pub async fn retire_device(ctx: State<'_, AppContext>) -> Result<AppUiState, String> {
    if let Some(orchestrator) = ctx.orchestrator.lock().as_ref() {
        orchestrator.cancel();
    }
    // Explicit retire is the assisted-recovery / restart-setup path: wipe the secure
    // envelope and non-secret config, then mint a fresh identity so Server setup works.
    ctx.secrets.delete().map_err(map_err)?;
    {
        let mut config = ctx.config.lock();
        *config = DesktopConfig::default();
        ctx.config_store.save(&config).map_err(map_err)?;
    }
    *ctx.terminal_name.lock() = None;
    *ctx.orchestrator.lock() = None;

    let device_id = Uuid::now_v7();
    let (pkcs8, _public) = crypto::generate_key_material().map_err(map_err)?;
    let mut credential = [0u8; 32];
    rand::RngCore::fill_bytes(&mut rand::thread_rng(), &mut credential);
    let credential_b64 = {
        use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
        URL_SAFE_NO_PAD.encode(credential)
    };
    ctx.secrets
        .set(&SecretEnvelopeV1 {
            schema_version: SecretEnvelopeV1::SCHEMA_VERSION,
            device_id,
            private_key_pkcs8_base64: pkcs8,
            device_credential_base64url: credential_b64,
            pairing: PairingEnvelope::default(),
        })
        .map_err(map_err)?;
    {
        let mut config = ctx.config.lock();
        config.device_id = Some(device_id);
        config.status = ConfigStatus::Uninitialized;
        ctx.config_store.save(&config).map_err(map_err)?;
    }
    ctx.config_file_missing_on_boot
        .store(false, std::sync::atomic::Ordering::SeqCst);
    Ok(ctx.resolve_ui_state())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::secrets::InMemorySecretStore;
    use tempfile::tempdir;

    fn ctx_with(config: DesktopConfig, envelope: Option<SecretEnvelopeV1>) -> AppContext {
        let dir = tempdir().unwrap();
        let store = Arc::new(ConfigStore::new(dir.path()));
        let secrets: Arc<dyn SecretStore> = Arc::new(InMemorySecretStore::default());
        if let Some(value) = envelope {
            secrets.set(&value).unwrap();
        }
        AppContext {
            config: Arc::new(Mutex::new(config)),
            config_store: store,
            secrets,
            orchestrator: Mutex::new(None),
            terminal_name: Mutex::new(None),
            config_file_missing_on_boot: std::sync::atomic::AtomicBool::new(false),
        }
    }

    fn ctx_boot_missing_config(envelope: Option<SecretEnvelopeV1>) -> AppContext {
        let ctx = ctx_with(DesktopConfig::default(), envelope);
        ctx.config_file_missing_on_boot
            .store(true, std::sync::atomic::Ordering::SeqCst);
        ctx
    }

    fn envelope(device_id: Uuid) -> SecretEnvelopeV1 {
        let (pkcs8, _) = crypto::generate_key_material().unwrap();
        SecretEnvelopeV1 {
            schema_version: 1,
            device_id,
            private_key_pkcs8_base64: pkcs8,
            device_credential_base64url: "y".into(),
            pairing: PairingEnvelope::default(),
        }
    }

    #[test]
    fn pending_approval_includes_identity_fingerprint() {
        let request_id = Uuid::from_u128(9);
        let device_id = Uuid::from_u128(3);
        let env = envelope(device_id);
        let expected = crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).unwrap();
        let config = DesktopConfig {
            status: ConfigStatus::PairingInProgress,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            pairing_request_id: Some(request_id),
            device_id: Some(device_id),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, Some(env));
        *ctx.terminal_name.lock() = Some("Front Desk".into());
        match ctx.resolve_ui_state() {
            AppUiState::PendingApproval {
                device_fingerprint_short,
                terminal_name,
                ..
            } => {
                assert_eq!(device_fingerprint_short.as_deref(), Some(expected.as_str()));
                assert_eq!(terminal_name, "Front Desk");
            }
            other => panic!("expected PendingApproval, got {other:?}"),
        }
    }

    #[test]
    fn server_configured_preserves_identity_fingerprint() {
        let device_id = Uuid::from_u128(4);
        let env = envelope(device_id);
        let expected = crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).unwrap();
        let config = DesktopConfig {
            status: ConfigStatus::ServerConfigured,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            device_id: Some(device_id),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, Some(env));
        match ctx.resolve_ui_state() {
            AppUiState::NeedsPairing {
                device_fingerprint_short,
                ..
            } => assert_eq!(device_fingerprint_short.as_deref(), Some(expected.as_str())),
            other => panic!("expected NeedsPairing, got {other:?}"),
        }
    }

    #[test]
    fn fingerprint_stable_across_pairing_in_progress_and_server_configured() {
        let device_id = Uuid::from_u128(5);
        let env = envelope(device_id);
        let expected = crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).unwrap();
        let mut config = DesktopConfig {
            status: ConfigStatus::PairingInProgress,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            pairing_request_id: Some(Uuid::from_u128(1)),
            device_id: Some(device_id),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config.clone(), Some(env));
        let pending_fp = match ctx.resolve_ui_state() {
            AppUiState::PendingApproval {
                device_fingerprint_short,
                ..
            } => device_fingerprint_short,
            other => panic!("expected PendingApproval, got {other:?}"),
        };
        config.status = ConfigStatus::ServerConfigured;
        config.pairing_request_id = None;
        *ctx.config.lock() = config;
        let needs_fp = match ctx.resolve_ui_state() {
            AppUiState::NeedsPairing {
                device_fingerprint_short,
                ..
            } => device_fingerprint_short,
            other => panic!("expected NeedsPairing, got {other:?}"),
        };
        assert_eq!(pending_fp.as_deref(), Some(expected.as_str()));
        assert_eq!(needs_fp, pending_fp);
    }

    #[test]
    fn envelope_without_config_is_recovery_required() {
        let ctx = ctx_boot_missing_config(Some(envelope(Uuid::nil())));
        match ctx.resolve_ui_state() {
            AppUiState::RecoveryRequired { .. } => {}
            other => panic!("expected RecoveryRequired, got {other:?}"),
        }
    }

    #[test]
    fn fresh_identity_without_url_is_needs_server_setup() {
        let ctx = ctx_with(
            DesktopConfig {
                device_id: Some(Uuid::nil()),
                status: ConfigStatus::Uninitialized,
                ..DesktopConfig::default()
            },
            Some(envelope(Uuid::nil())),
        );
        match ctx.resolve_ui_state() {
            AppUiState::NeedsServerSetup { .. } => {}
            other => panic!("expected NeedsServerSetup, got {other:?}"),
        }
    }

    #[test]
    fn identity_ready_maps_to_needs_server_setup() {
        let ctx = ctx_with(DesktopConfig::default(), None);
        match ctx.resolve_ui_state() {
            AppUiState::NeedsServerSetup { .. } => {}
            other => panic!("expected NeedsServerSetup, got {other:?}"),
        }
    }

    #[test]
    fn server_configured_maps_to_needs_pairing() {
        let config = DesktopConfig {
            status: ConfigStatus::ServerConfigured,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            device_id: Some(Uuid::nil()),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, Some(envelope(Uuid::nil())));
        match ctx.resolve_ui_state() {
            AppUiState::NeedsPairing { .. } => {}
            other => panic!("expected NeedsPairing, got {other:?}"),
        }
    }

    #[test]
    fn pairing_in_progress_with_request_is_pending_approval() {
        let request_id = Uuid::from_u128(9);
        let config = DesktopConfig {
            status: ConfigStatus::PairingInProgress,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            pairing_request_id: Some(request_id),
            device_id: Some(Uuid::nil()),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, Some(envelope(Uuid::nil())));
        match ctx.resolve_ui_state() {
            AppUiState::PendingApproval {
                pairing_request_id, ..
            } => assert_eq!(pairing_request_id, request_id),
            other => panic!("expected PendingApproval, got {other:?}"),
        }
    }

    #[test]
    fn paired_with_envelope_is_paired() {
        let config = DesktopConfig {
            status: ConfigStatus::Paired,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            device_id: Some(Uuid::nil()),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, Some(envelope(Uuid::nil())));
        match ctx.resolve_ui_state() {
            AppUiState::Paired { .. } => {}
            other => panic!("expected Paired, got {other:?}"),
        }
    }

    #[test]
    fn paired_without_envelope_is_credentials_unavailable() {
        let config = DesktopConfig {
            status: ConfigStatus::Paired,
            branch_base_url: Some("http://127.0.0.1:5102".into()),
            ..DesktopConfig::default()
        };
        let ctx = ctx_with(config, None);
        match ctx.resolve_ui_state() {
            AppUiState::PairedCredentialsUnavailable { .. } => {}
            other => panic!("expected PairedCredentialsUnavailable, got {other:?}"),
        }
    }

    #[test]
    fn explicit_recovery_and_credentials_statuses_map() {
        let recovery = ctx_with(
            DesktopConfig {
                status: ConfigStatus::RecoveryRequired,
                ..DesktopConfig::default()
            },
            None,
        );
        assert!(matches!(
            recovery.resolve_ui_state(),
            AppUiState::RecoveryRequired { .. }
        ));
        let unavailable = ctx_with(
            DesktopConfig {
                status: ConfigStatus::PairedCredentialsUnavailable,
                ..DesktopConfig::default()
            },
            None,
        );
        assert!(matches!(
            unavailable.resolve_ui_state(),
            AppUiState::PairedCredentialsUnavailable { .. }
        ));
    }

    #[test]
    fn retire_wipes_envelope_and_leaves_needs_server_setup() {
        let dir = tempdir().unwrap();
        let store = Arc::new(ConfigStore::new(dir.path()));
        let secrets: Arc<dyn SecretStore> = Arc::new(InMemorySecretStore::default());
        let old_id = Uuid::from_u128(99);
        secrets.set(&envelope(old_id)).unwrap();
        let ctx = AppContext {
            config: Arc::new(Mutex::new(DesktopConfig::default())),
            config_store: store,
            secrets: Arc::clone(&secrets),
            orchestrator: Mutex::new(None),
            terminal_name: Mutex::new(None),
            config_file_missing_on_boot: std::sync::atomic::AtomicBool::new(true),
        };
        // Simulate the retire command body without Tauri State wrapper.
        ctx.secrets.delete().unwrap();
        {
            let mut config = ctx.config.lock();
            *config = DesktopConfig::default();
            ctx.config_store.save(&config).unwrap();
        }
        let device_id = Uuid::from_u128(100);
        let (pkcs8, _) = crypto::generate_key_material().unwrap();
        ctx.secrets
            .set(&SecretEnvelopeV1 {
                schema_version: 1,
                device_id,
                private_key_pkcs8_base64: pkcs8,
                device_credential_base64url: "cred".into(),
                pairing: PairingEnvelope::default(),
            })
            .unwrap();
        {
            let mut config = ctx.config.lock();
            config.device_id = Some(device_id);
            config.status = ConfigStatus::Uninitialized;
            ctx.config_store.save(&config).unwrap();
        }
        ctx.config_file_missing_on_boot
            .store(false, std::sync::atomic::Ordering::SeqCst);
        match ctx.resolve_ui_state() {
            AppUiState::NeedsServerSetup { .. } => {}
            other => panic!("expected NeedsServerSetup after retire, got {other:?}"),
        }
        assert_ne!(secrets.get().unwrap().unwrap().device_id, old_id);
    }
}
