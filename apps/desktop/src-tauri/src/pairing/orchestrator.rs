use std::sync::Arc;

use chrono::{DateTime, Utc};
use parking_lot::Mutex;
use serde::Serialize;
use tauri::{AppHandle, Emitter};
use tokio::time::{sleep, Duration};

use crate::branch::BranchClient;
use crate::config::{ConfigStatus, ConfigStore, DesktopConfig};
use crate::error::{AppError, AppResult};
use crate::secrets::{PairingEnvelope, SecretStore};
use crate::state::PairingProgressEvent;

use super::ceremony::{finalize, PairingCeremony};
use super::PairingPoller;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingProgress {
    pub phase: String,
    pub fingerprint_short: Option<String>,
    pub expires_at: Option<DateTime<Utc>>,
    pub error_code: Option<String>,
}

pub struct PairingOrchestrator {
    app: AppHandle,
    config: Arc<Mutex<DesktopConfig>>,
    config_store: Arc<ConfigStore>,
    secrets: Arc<dyn SecretStore>,
    poller: PairingPoller,
    ceremony: PairingCeremony,
}

impl PairingOrchestrator {
    pub fn new(
        app: AppHandle,
        config: Arc<Mutex<DesktopConfig>>,
        config_store: Arc<ConfigStore>,
        secrets: Arc<dyn SecretStore>,
    ) -> Self {
        let ceremony = PairingCeremony::new(
            Arc::clone(&config),
            Arc::clone(&config_store),
            Arc::clone(&secrets),
        );
        Self {
            app,
            config,
            config_store,
            secrets,
            poller: PairingPoller::default(),
            ceremony,
        }
    }

    /// `pairing_code` format: `{pairingSessionId}:{8-digit-code}` (QR / operator paste).
    pub async fn begin(&self, pairing_code: String, terminal_name: String) -> AppResult<()> {
        let progress = self
            .ceremony
            .exchange(&pairing_code, &terminal_name)
            .await?;
        self.emit(
            &progress.phase,
            progress.fingerprint_short,
            None,
            progress.error_code.as_deref(),
        );
        Ok(())
    }

    pub fn cancel(&self) {
        self.poller.cancel();
    }

    pub async fn resume(&self) -> AppResult<()> {
        self.poller.cancel();
        let envelope = self
            .secrets
            .get()?
            .ok_or(AppError::CredentialsUnavailable)?;
        let request_id = envelope.pairing.request_id.ok_or(AppError::Pairing)?;
        let token = envelope
            .pairing
            .status_token
            .clone()
            .ok_or(AppError::Pairing)?;
        let base = self
            .config
            .lock()
            .branch_base_url
            .clone()
            .ok_or(AppError::Configuration)?;
        let app = self.app.clone();
        let config = Arc::clone(&self.config);
        let store = Arc::clone(&self.config_store);
        let secrets = Arc::clone(&self.secrets);
        self.poller.replace(tokio::spawn(async move {
            let client = match crate::branch::validate_branch_url(&base).and_then(BranchClient::new)
            {
                Ok(value) => value,
                Err(error) => {
                    emit_progress(&app, "error", None, None, Some(error.code()));
                    return;
                }
            };
            loop {
                match client.status(request_id, &token).await {
                    Ok(status) if status.status == "Approved" => {
                        let fp = secrets.get().ok().flatten().and_then(|env| {
                            crate::crypto::fingerprint_short_from_pkcs8(
                                &env.private_key_pkcs8_base64,
                            )
                            .ok()
                        });
                        emit_progress(&app, "finalizing", fp, None, None);
                        if finalize(&client, status, &token, &secrets, &config, &store)
                            .await
                            .is_err()
                        {
                            emit_progress(&app, "error", None, None, Some("PAIRING_FAILED"));
                        } else {
                            emit_progress(&app, "paired", None, None, None);
                        }
                        break;
                    }
                    Ok(status) if status.status == "Rejected" || status.status == "Expired" => {
                        let code = if status.status == "Rejected" {
                            "PAIRING_REJECTED"
                        } else {
                            "PAIRING_EXPIRED"
                        };
                        clear_transient_pairing(&secrets, &config, &store);
                        emit_progress(&app, "error", None, None, Some(code));
                        break;
                    }
                    Ok(_) => sleep(Duration::from_secs(2)).await,
                    Err(error) => {
                        emit_progress(&app, "error", None, None, Some(error.code()));
                        break;
                    }
                }
            }
        }));
        Ok(())
    }

    fn emit(
        &self,
        phase: &str,
        fingerprint_short: Option<String>,
        expires_at: Option<DateTime<Utc>>,
        error_code: Option<&str>,
    ) {
        emit_progress(&self.app, phase, fingerprint_short, expires_at, error_code);
    }
}

fn emit_progress(
    app: &AppHandle,
    phase: &str,
    fingerprint_short: Option<String>,
    expires_at: Option<DateTime<Utc>>,
    error_code: Option<&str>,
) {
    let _ = app.emit(
        "pairing-progress",
        PairingProgressEvent {
            phase: phase.into(),
            fingerprint_short,
            expires_at,
            error_code: error_code.map(str::to_owned),
        },
    );
}

fn clear_transient_pairing(
    secrets: &Arc<dyn SecretStore>,
    config: &Arc<Mutex<DesktopConfig>>,
    store: &Arc<ConfigStore>,
) {
    if let Ok(Some(mut envelope)) = secrets.get() {
        envelope.pairing = PairingEnvelope::default();
        let _ = secrets.set(&envelope);
    }
    let mut cfg = config.lock();
    if cfg.status == ConfigStatus::PairingInProgress {
        cfg.status = ConfigStatus::ServerConfigured;
        cfg.pairing_request_id = None;
        let _ = store.save(&cfg);
    }
}
