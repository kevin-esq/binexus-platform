//! Product pairing ceremony against a live Branch Runtime (no Tauri AppHandle).
//! Used by PairingOrchestrator and the `pairing_interop` CI harness.

use std::sync::Arc;
use std::time::Duration;

use base64::{engine::general_purpose::STANDARD, Engine};
use p256::ecdsa::SigningKey;
use p256::pkcs8::DecodePrivateKey;
use parking_lot::Mutex;
use tokio::time::sleep;
use uuid::Uuid;

use crate::branch::{BranchClient, PairingStatus};
use crate::config::{ConfigStatus, ConfigStore, DesktopConfig};
use crate::crypto;
use crate::error::{AppError, AppResult};
use crate::secrets::SecretStore;

#[derive(Debug, Clone)]
pub struct CeremonyProgress {
    pub phase: String,
    pub fingerprint_short: Option<String>,
    pub pairing_request_id: Option<Uuid>,
    pub error_code: Option<String>,
}

pub struct PairingCeremony {
    config: Arc<Mutex<DesktopConfig>>,
    config_store: Arc<ConfigStore>,
    secrets: Arc<dyn SecretStore>,
}

impl PairingCeremony {
    pub fn new(
        config: Arc<Mutex<DesktopConfig>>,
        config_store: Arc<ConfigStore>,
        secrets: Arc<dyn SecretStore>,
    ) -> Self {
        Self {
            config,
            config_store,
            secrets,
        }
    }

    /// Exchange half of the ceremony. Persists status token in the secure envelope.
    pub async fn exchange(
        &self,
        pairing_code: &str,
        terminal_name: &str,
    ) -> AppResult<CeremonyProgress> {
        let (session_raw, code) = pairing_code.split_once(':').ok_or(AppError::Pairing)?;
        let session_id = Uuid::parse_str(session_raw.trim()).map_err(|_| AppError::Pairing)?;
        let code = code.trim();
        let envelope = self
            .secrets
            .get()?
            .ok_or(AppError::CredentialsUnavailable)?;
        let base = self
            .config
            .lock()
            .branch_base_url
            .clone()
            .ok_or(AppError::Configuration)?;
        let client = BranchClient::new(crate::branch::validate_branch_url(&base)?)?;
        let signing_key = SigningKey::from_pkcs8_der(
            &STANDARD
                .decode(&envelope.private_key_pkcs8_base64)
                .map_err(|_| AppError::Internal)?,
        )
        .map_err(|_| AppError::Internal)?;
        let public_key = crypto::public_key_base64url(&signing_key)?;
        let fingerprint = crypto::fingerprint(&public_key)?;
        let credential_hash = crypto::credential_hash(&envelope.device_credential_base64url);
        let challenge = client
            .challenge(
                session_id,
                code,
                envelope.device_id,
                &public_key,
                &credential_hash,
            )
            .await?;
        let payload = crypto::encode_exchange(
            challenge.challenge_id,
            challenge.branch_instance_id,
            session_id,
            envelope.device_id,
            &fingerprint,
            &credential_hash,
            &challenge.nonce,
            challenge.expires_at_utc,
        );
        let signature = crypto::sign(&envelope.private_key_pkcs8_base64, &payload)?;
        let exchange = client
            .exchange(serde_json::json!({
                "pairingSessionId": session_id,
                "pairingCode": code,
                "deviceId": envelope.device_id,
                "publicKey": public_key,
                "challengeId": challenge.challenge_id,
                "signature": signature,
                "credentialHash": credential_hash,
                "terminalName": terminal_name,
            }))
            .await?;
        let mut updated = envelope;
        updated.pairing.request_id = Some(exchange.pairing_request_id);
        updated.pairing.status_token = Some(exchange.pairing_status_token);
        // Commit order: secure envelope first, then non-secret config (see reconcile docs).
        self.secrets.set(&updated)?;
        {
            let mut config = self.config.lock();
            config.pairing_request_id = updated.pairing.request_id;
            config.status = ConfigStatus::PairingInProgress;
            self.config_store.save(&config)?;
        }
        Ok(CeremonyProgress {
            phase: "pendingApproval".into(),
            fingerprint_short: Some(exchange.device_fingerprint_short),
            pairing_request_id: Some(exchange.pairing_request_id),
            error_code: None,
        })
    }

    /// Poll until Approved / Rejected / Expired, then confirm (reissue if receipt missing).
    pub async fn poll_until_terminal(&self, max_wait: Duration) -> AppResult<CeremonyProgress> {
        let deadline = tokio::time::Instant::now() + max_wait;
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
        let client = BranchClient::new(crate::branch::validate_branch_url(&base)?)?;

        loop {
            if tokio::time::Instant::now() > deadline {
                return Err(AppError::Pairing);
            }
            let status = client.status(request_id, &token).await?;
            match status.status.as_str() {
                "Approved" => {
                    finalize(
                        &client,
                        status,
                        &token,
                        &self.secrets,
                        &self.config,
                        &self.config_store,
                    )
                    .await?;
                    return Ok(CeremonyProgress {
                        phase: "paired".into(),
                        fingerprint_short: None,
                        pairing_request_id: Some(request_id),
                        error_code: None,
                    });
                }
                "Rejected" | "Expired" => {
                    return Ok(CeremonyProgress {
                        phase: "error".into(),
                        fingerprint_short: None,
                        pairing_request_id: Some(request_id),
                        error_code: Some("PAIRING_FAILED".into()),
                    });
                }
                _ => sleep(Duration::from_millis(400)).await,
            }
        }
    }

    /// Poll until Approved / Rejected / Expired without confirming.
    pub async fn poll_until_approved(&self, max_wait: Duration) -> AppResult<PairingStatus> {
        let deadline = tokio::time::Instant::now() + max_wait;
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
        let client = BranchClient::new(crate::branch::validate_branch_url(&base)?)?;

        loop {
            if tokio::time::Instant::now() > deadline {
                return Err(AppError::Pairing);
            }
            let status = client.status(request_id, &token).await?;
            match status.status.as_str() {
                "Approved" => return Ok(status),
                "Rejected" | "Expired" => return Err(AppError::Pairing),
                _ => sleep(Duration::from_millis(400)).await,
            }
        }
    }

    /// Confirm path that forces receipt reissue (ignores receipt fields on status).
    pub async fn confirm_forcing_reissue(&self) -> AppResult<CeremonyProgress> {
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
        let client = BranchClient::new(crate::branch::validate_branch_url(&base)?)?;
        let approved = client.status(request_id, &token).await?;
        if approved.status != "Approved" {
            return Err(AppError::Pairing);
        }
        let status = PairingStatus {
            pairing_request_id: request_id,
            status: "Approved".into(),
            branch_instance_id: approved.branch_instance_id,
            terminal_id: None,
            confirmation_challenge_id: None,
            confirmation_nonce: None,
            confirmation_expires_at_utc: None,
            pairing_receipt: None,
        };
        finalize(
            &client,
            status,
            &token,
            &self.secrets,
            &self.config,
            &self.config_store,
        )
        .await?;
        Ok(CeremonyProgress {
            phase: "paired".into(),
            fingerprint_short: None,
            pairing_request_id: Some(request_id),
            error_code: None,
        })
    }
}

pub async fn finalize(
    client: &BranchClient,
    status: PairingStatus,
    token: &str,
    secrets: &Arc<dyn SecretStore>,
    config: &Arc<Mutex<DesktopConfig>>,
    store: &Arc<ConfigStore>,
) -> AppResult<()> {
    let mut envelope = secrets.get()?.ok_or(AppError::CredentialsUnavailable)?;
    let request_id = status.pairing_request_id;
    let (branch_id, terminal_id, challenge_id, nonce, expires, receipt) = match (
        status.terminal_id,
        status.confirmation_challenge_id,
        status.confirmation_nonce.clone(),
        status.confirmation_expires_at_utc,
        status.pairing_receipt.clone(),
    ) {
        (Some(terminal), Some(challenge), Some(nonce), Some(expires), Some(receipt)) => (
            status.branch_instance_id,
            terminal,
            challenge,
            nonce,
            expires,
            receipt,
        ),
        _ => {
            let challenge = client.receipt_challenge(request_id, token).await?;
            let signing_key = SigningKey::from_pkcs8_der(
                &STANDARD
                    .decode(&envelope.private_key_pkcs8_base64)
                    .map_err(|_| AppError::Internal)?,
            )
            .map_err(|_| AppError::Internal)?;
            let public = crypto::public_key_base64url(&signing_key)?;
            let payload = crypto::encode_receipt_reissue(
                challenge.challenge_id,
                request_id,
                challenge.branch_instance_id,
                envelope.device_id,
                &crypto::fingerprint(&public)?,
                &crypto::credential_hash(&envelope.device_credential_base64url),
                &challenge.nonce,
                challenge.expires_at_utc,
            );
            let reissue = client
                .reissue(
                    request_id,
                    token,
                    challenge.challenge_id,
                    &crypto::sign(&envelope.private_key_pkcs8_base64, &payload)?,
                )
                .await?;
            (
                reissue.branch_instance_id,
                reissue.terminal_id,
                reissue.confirmation_challenge_id,
                reissue.confirmation_nonce,
                reissue.confirmation_expires_at_utc,
                reissue.pairing_receipt,
            )
        }
    };
    let signing_key = SigningKey::from_pkcs8_der(
        &STANDARD
            .decode(&envelope.private_key_pkcs8_base64)
            .map_err(|_| AppError::Internal)?,
    )
    .map_err(|_| AppError::Internal)?;
    let public = crypto::public_key_base64url(&signing_key)?;
    let payload = crypto::encode_confirm(
        challenge_id,
        request_id,
        branch_id,
        envelope.device_id,
        terminal_id,
        &crypto::fingerprint(&public)?,
        &crypto::credential_hash(&envelope.device_credential_base64url),
        &crypto::credential_hash(&receipt),
        &nonce,
        expires,
    );
    client
        .confirm(serde_json::json!({
            "pairingRequestId": request_id,
            "confirmationChallengeId": challenge_id,
            "signature": crypto::sign(&envelope.private_key_pkcs8_base64, &payload)?,
            "pairingReceipt": receipt,
            "pairingStatusToken": token,
        }))
        .await?;
    envelope.pairing.receipt = None;
    envelope.pairing.status_token = None;
    envelope.pairing.request_id = None;
    // Envelope first (clears transient pairing secrets), then config Paired.
    secrets.set(&envelope)?;
    let mut updated = config.lock();
    updated.status = ConfigStatus::Paired;
    updated.branch_instance_id = Some(branch_id);
    updated.terminal_id = Some(terminal_id);
    updated.pairing_request_id = None;
    store.save(&updated)
}

/// Reconcile after a partial write between WCM envelope and config.json.
/// Not a distributed transaction — recovery is explicit and never auto-regenerates identity.
pub fn reconcile_partial_write(
    config: &mut DesktopConfig,
    has_envelope: bool,
    envelope_device_id: Option<Uuid>,
) -> ConfigStatus {
    if has_envelope {
        if let Some(env_id) = envelope_device_id {
            if let Some(cfg_id) = config.device_id {
                if cfg_id != env_id {
                    config.status = ConfigStatus::RecoveryRequired;
                    return config.status;
                }
            }
        }
        if config.branch_base_url.is_none() && config.status != ConfigStatus::RecoveryRequired {
            config.status = ConfigStatus::RecoveryRequired;
        }
    } else if config.status == ConfigStatus::Paired {
        config.status = ConfigStatus::PairedCredentialsUnavailable;
    }
    config.status
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::secrets::{InMemorySecretStore, PairingEnvelope, SecretEnvelopeV1};
    use tempfile::tempdir;

    fn envelope(device_id: Uuid) -> SecretEnvelopeV1 {
        SecretEnvelopeV1 {
            schema_version: 1,
            device_id,
            private_key_pkcs8_base64: "pk".into(),
            device_credential_base64url: "cred".into(),
            pairing: PairingEnvelope::default(),
        }
    }

    #[test]
    fn reconcile_mismatch_device_id_requires_recovery() {
        let mut config = DesktopConfig {
            device_id: Some(Uuid::nil()),
            branch_base_url: Some("http://127.0.0.1:1".into()),
            status: ConfigStatus::Paired,
            ..DesktopConfig::default()
        };
        let other = Uuid::from_u128(1);
        assert_eq!(
            reconcile_partial_write(&mut config, true, Some(other)),
            ConfigStatus::RecoveryRequired
        );
    }

    #[test]
    fn reconcile_paired_without_envelope() {
        let mut config = DesktopConfig {
            status: ConfigStatus::Paired,
            branch_base_url: Some("http://127.0.0.1:1".into()),
            ..DesktopConfig::default()
        };
        assert_eq!(
            reconcile_partial_write(&mut config, false, None),
            ConfigStatus::PairedCredentialsUnavailable
        );
    }

    #[test]
    fn reconcile_envelope_without_config_url() {
        let mut config = DesktopConfig::default();
        let id = Uuid::from_u128(7);
        assert_eq!(
            reconcile_partial_write(&mut config, true, Some(id)),
            ConfigStatus::RecoveryRequired
        );
    }

    #[test]
    fn commit_order_envelope_then_config_survives_config_failure_simulation() {
        let dir = tempdir().unwrap();
        let store = Arc::new(ConfigStore::at(dir.path().join("config.json")));
        let secrets: Arc<dyn SecretStore> = Arc::new(InMemorySecretStore::default());
        let device_id = Uuid::from_u128(9);
        secrets.set(&envelope(device_id)).unwrap();
        // Simulate crash after envelope write, before config Paired:
        let mut config = DesktopConfig {
            device_id: Some(device_id),
            branch_base_url: Some("http://127.0.0.1:1".into()),
            status: ConfigStatus::PairingInProgress,
            pairing_request_id: Some(Uuid::nil()),
            ..DesktopConfig::default()
        };
        store.save(&config).unwrap();
        // Restart reconcile: envelope present + PairingInProgress is resumable, not recovery.
        assert_eq!(
            reconcile_partial_write(&mut config, true, Some(device_id)),
            ConfigStatus::PairingInProgress
        );
        assert!(secrets.get().unwrap().is_some());
    }
}
