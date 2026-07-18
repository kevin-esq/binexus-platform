use std::path::PathBuf;
use std::sync::Arc;

use parking_lot::Mutex;
use tauri::State;
use uuid::Uuid;

use crate::config::{ConfigStatus, ConfigStore, DesktopConfig};
use crate::crypto;
use crate::error::{AppError, AppResult};
use crate::secrets::{KeyringSecretStore, PairingEnvelope, SecretEnvelopeV1, SecretStore};
use crate::state::AppUiState;

pub struct AppContext {
    pub config: Arc<Mutex<DesktopConfig>>,
    pub config_store: Arc<ConfigStore>,
    pub secrets: Arc<dyn SecretStore>,
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
            terminal_name: Mutex::new(None),
            config_file_missing_on_boot: std::sync::atomic::AtomicBool::new(
                config_file_missing_on_boot,
            ),
        })
    }

    pub fn resolve_ui_state(&self) -> AppUiState {
        let config = self.config.lock().clone();
        let envelope = self.secrets.get().ok().flatten();
        let has_envelope = envelope.is_some();
        let fingerprint = envelope.as_ref().and_then(|env| {
            crypto::fingerprint_short_from_pkcs8(&env.private_key_pkcs8_base64).ok()
        });
        let terminal_name = self.terminal_name.lock().clone().unwrap_or_default();

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
pub async fn retire_device(ctx: State<'_, AppContext>) -> Result<AppUiState, String> {
    ctx.secrets.delete().map_err(map_err)?;
    {
        let mut config = ctx.config.lock();
        *config = DesktopConfig::default();
        ctx.config_store.save(&config).map_err(map_err)?;
    }
    *ctx.terminal_name.lock() = None;

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
