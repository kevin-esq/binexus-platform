use std::sync::Arc;

use chrono::{DateTime, Duration, Utc};
use parking_lot::Mutex;
use serde::Serialize;
use tokio::sync::{watch, Mutex as AsyncMutex};
use uuid::Uuid;
use zeroize::Zeroizing;

use crate::branch::BranchClient;
use crate::crypto::{self, DEVICE_AUTH_CHALLENGE_VERSION};
use crate::error::{AppError, AppResult};
use crate::secrets::SecretStore;

/// HTTP header name for Branch Device Access Tokens (DAT).
pub const DEVICE_AUTH_HEADER: &str = "X-Binexus-Device-Authorization";

/// Skew before expiry when a silent PoP renewal is triggered.
pub const RENEW_SKEW: Duration = Duration::seconds(60);

/// Paired identity the DAT ceremony must bind to (from DesktopConfig, not RAM DAT).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeviceAuthIdentity {
    pub branch_instance_id: Uuid,
    pub device_id: Uuid,
    pub terminal_id: Option<Uuid>,
    pub branch_base_url: String,
}

/// Public device-session state. Never includes the DAT, private keys, or credential material.
#[derive(Debug, Clone, Serialize, PartialEq, Eq)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum DeviceSessionPublicState {
    DeviceSessionExpired,
    Authenticating,
    DeviceAuthenticated {
        device_id: Uuid,
        terminal_id: Uuid,
        branch_instance_id: Uuid,
        expires_at_utc: DateTime<Utc>,
    },
    DeviceRevoked {
        message: String,
    },
    CredentialsUnavailable {
        message: String,
    },
    BranchIdentityMismatch {
        message: String,
    },
    DeviceSessionFailed {
        code: String,
        message: String,
    },
}

/// RAM-only DAT material. Must never be serialized to IPC or disk.
/// Intentionally omits `Debug` so the access token cannot leak via formatting.
struct DatMaterial {
    access_token: Zeroizing<String>,
    expires_at_utc: DateTime<Utc>,
    device_id: Uuid,
    terminal_id: Uuid,
    branch_instance_id: Uuid,
}

struct SessionInner {
    dat: Option<DatMaterial>,
    authenticating: bool,
    last_state: Option<DeviceSessionPublicState>,
    cancellation_epoch: u64,
}

type FlightResult = AppResult<Zeroizing<String>>;

/// In-flight PoP ceremony shared by concurrent `ensure_access_token` callers.
struct SharedFlight {
    result_rx: watch::Receiver<Option<FlightResult>>,
}

struct FlightSlot {
    current: Option<SharedFlight>,
}

/// Obtains, holds, and renews DATs with single-flight PoP refresh.
pub struct DeviceAuthSession {
    secrets: Arc<dyn SecretStore>,
    inner: Mutex<SessionInner>,
    flight: AsyncMutex<FlightSlot>,
}

impl DeviceAuthSession {
    pub fn new(secrets: Arc<dyn SecretStore>) -> Self {
        Self {
            secrets,
            inner: Mutex::new(SessionInner {
                dat: None,
                authenticating: false,
                last_state: None,
                cancellation_epoch: 0,
            }),
            flight: AsyncMutex::new(FlightSlot { current: None }),
        }
    }

    pub fn public_state(&self) -> DeviceSessionPublicState {
        let guard = self.inner.lock();
        if guard.authenticating {
            return DeviceSessionPublicState::Authenticating;
        }
        if let Some(dat) = guard.dat.as_ref() {
            if dat.expires_at_utc > Utc::now() {
                return DeviceSessionPublicState::DeviceAuthenticated {
                    device_id: dat.device_id,
                    terminal_id: dat.terminal_id,
                    branch_instance_id: dat.branch_instance_id,
                    expires_at_utc: dat.expires_at_utc,
                };
            }
        }
        if let Some(state) = &guard.last_state {
            return state.clone();
        }
        DeviceSessionPublicState::DeviceSessionExpired
    }

    /// Returns `Bearer <DAT>` when a non-expired token is in memory. Never logs the value.
    pub fn authorization_value(&self) -> Option<Zeroizing<String>> {
        let guard = self.inner.lock();
        let dat = guard.dat.as_ref()?;
        if dat.expires_at_utc <= Utc::now() {
            return None;
        }
        Some(bearer_from_token(dat.access_token.as_str()))
    }

    pub fn clear(&self) {
        let mut guard = self.inner.lock();
        guard.dat = None;
        guard.authenticating = false;
        guard.last_state = None;
        guard.cancellation_epoch = guard.cancellation_epoch.wrapping_add(1);
    }

    /// Invalidates RAM-only DAT state when the configured Branch URL changes.
    pub fn clear_on_branch_url_change(&self) {
        self.clear();
    }

    /// Ensures a usable DAT exists, using structural single-flight so concurrent callers
    /// share exactly one PoP ceremony.
    pub async fn ensure_access_token(
        &self,
        client: &BranchClient,
        identity: &DeviceAuthIdentity,
    ) -> AppResult<Zeroizing<String>> {
        if let Some(value) = self.authorization_value_if_fresh(identity)? {
            return Ok(value);
        }

        enum Role {
            Leader {
                tx: watch::Sender<Option<FlightResult>>,
                cancellation_epoch: u64,
            },
            Follower(watch::Receiver<Option<FlightResult>>),
        }

        let role = {
            let mut gate = self.flight.lock().await;
            if let Some(value) = self.authorization_value_if_fresh(identity)? {
                return Ok(value);
            }

            if let Some(shared) = gate.current.as_ref() {
                Role::Follower(shared.result_rx.clone())
            } else {
                let (tx, rx) = watch::channel(None);
                gate.current = Some(SharedFlight {
                    result_rx: rx.clone(),
                });
                let cancellation_epoch = {
                    let mut guard = self.inner.lock();
                    guard.authenticating = true;
                    guard.last_state = None;
                    guard.cancellation_epoch
                };
                Role::Leader {
                    tx,
                    cancellation_epoch,
                }
            }
        };

        match role {
            Role::Follower(mut rx) => loop {
                {
                    let borrowed = rx.borrow();
                    if let Some(result) = borrowed.as_ref() {
                        return clone_flight_result(result);
                    }
                }
                if rx.changed().await.is_err() {
                    return Err(AppError::DeviceSessionExpired);
                }
            },
            Role::Leader {
                tx,
                cancellation_epoch,
            } => {
                let result = self.refresh_locked(client, identity).await;
                let result = {
                    let mut guard = self.inner.lock();
                    if guard.cancellation_epoch != cancellation_epoch {
                        guard.dat = None;
                        guard.authenticating = false;
                        Err(AppError::DeviceSessionExpired)
                    } else {
                        guard.authenticating = false;
                        match &result {
                            Ok(_) => {
                                guard.last_state = None;
                                result
                            }
                            Err(err) => {
                                guard.dat = None;
                                guard.last_state = Some(public_failure_state(err));
                                result
                            }
                        }
                    }
                };

                let _ = tx.send(Some(clone_flight_result(&result)));
                {
                    let mut gate = self.flight.lock().await;
                    gate.current = None;
                }
                result
            }
        }
    }

    fn authorization_value_if_fresh(
        &self,
        identity: &DeviceAuthIdentity,
    ) -> AppResult<Option<Zeroizing<String>>> {
        let mut guard = self.inner.lock();
        let Some(dat) = guard.dat.as_ref() else {
            return Ok(None);
        };
        if dat.expires_at_utc - RENEW_SKEW <= Utc::now() {
            return Ok(None);
        }
        if !dat_matches_identity(dat, identity) {
            guard.dat = None;
            guard.last_state = Some(public_failure_state(&AppError::BranchIdentityMismatch));
            return Err(AppError::BranchIdentityMismatch);
        }
        Ok(Some(bearer_from_token(dat.access_token.as_str())))
    }

    async fn refresh_locked(
        &self,
        client: &BranchClient,
        identity: &DeviceAuthIdentity,
    ) -> AppResult<Zeroizing<String>> {
        match self.refresh_once(client, identity).await {
            Err(AppError::DeviceSessionExpired) => self.refresh_once(client, identity).await,
            result => result,
        }
    }

    async fn refresh_once(
        &self,
        client: &BranchClient,
        identity: &DeviceAuthIdentity,
    ) -> AppResult<Zeroizing<String>> {
        if normalize_branch_url(client.base_url())
            != normalize_branch_url(&identity.branch_base_url)
        {
            self.clear_dat_ram();
            return Err(AppError::BranchIdentityMismatch);
        }

        let envelope = self
            .secrets
            .get()?
            .ok_or(AppError::CredentialsUnavailable)?;

        if envelope.device_id != identity.device_id {
            self.clear_dat_ram();
            return Err(AppError::BranchIdentityMismatch);
        }

        let challenge = client.device_auth_challenge(envelope.device_id).await?;
        if challenge.branch_instance_id != identity.branch_instance_id {
            self.clear_dat_ram();
            return Err(AppError::BranchIdentityMismatch);
        }

        let fingerprint = crypto::fingerprint_from_pkcs8(&envelope.private_key_pkcs8_base64)?;
        let credential_hash = crypto::credential_hash(&envelope.device_credential_base64url);

        let payload = crypto::encode_device_auth_challenge(
            challenge.challenge_id,
            &challenge.nonce,
            envelope.device_id,
            challenge.branch_instance_id,
            &credential_hash,
            &fingerprint,
            challenge.expires_at_utc,
        );
        let signature = crypto::sign(&envelope.private_key_pkcs8_base64, &payload)?;

        let token = client
            .device_auth_tokens(
                challenge.challenge_id,
                envelope.device_id,
                &signature,
                DEVICE_AUTH_CHALLENGE_VERSION,
            )
            .await?;

        let access_token = Zeroizing::new(token.access_token);
        if token.branch_instance_id != identity.branch_instance_id
            || token.device_id != envelope.device_id
            || token.device_id != identity.device_id
        {
            drop(access_token);
            self.clear_dat_ram();
            return Err(AppError::BranchIdentityMismatch);
        }
        if let Some(expected_terminal_id) = identity.terminal_id {
            if token.terminal_id != expected_terminal_id {
                drop(access_token);
                self.clear_dat_ram();
                return Err(AppError::BranchIdentityMismatch);
            }
        }

        let bearer = bearer_from_token(access_token.as_str());
        {
            let mut guard = self.inner.lock();
            guard.dat = Some(DatMaterial {
                access_token,
                expires_at_utc: token.expires_at_utc,
                device_id: token.device_id,
                terminal_id: token.terminal_id,
                branch_instance_id: token.branch_instance_id,
            });
        }

        Ok(bearer)
    }

    fn clear_dat_ram(&self) {
        self.inner.lock().dat = None;
    }
}

fn dat_matches_identity(dat: &DatMaterial, identity: &DeviceAuthIdentity) -> bool {
    if dat.branch_instance_id != identity.branch_instance_id || dat.device_id != identity.device_id
    {
        return false;
    }
    if let Some(expected_terminal_id) = identity.terminal_id {
        if dat.terminal_id != expected_terminal_id {
            return false;
        }
    }
    true
}

fn normalize_branch_url(url: &str) -> &str {
    url.trim_end_matches('/')
}

fn bearer_from_token(access_token: &str) -> Zeroizing<String> {
    Zeroizing::new(format!("Bearer {access_token}"))
}

fn clone_flight_result(result: &FlightResult) -> FlightResult {
    match result {
        Ok(value) => Ok(Zeroizing::new(value.as_str().to_owned())),
        Err(err) => Err(err.clone()),
    }
}

fn public_failure_state(error: &AppError) -> DeviceSessionPublicState {
    match error {
        AppError::CredentialsUnavailable => DeviceSessionPublicState::CredentialsUnavailable {
            message: error.to_string(),
        },
        AppError::BranchIdentityMismatch => DeviceSessionPublicState::BranchIdentityMismatch {
            message: error.to_string(),
        },
        AppError::DeviceRevoked => DeviceSessionPublicState::DeviceRevoked {
            message: error.to_string(),
        },
        _ => DeviceSessionPublicState::DeviceSessionFailed {
            code: error.code().to_string(),
            message: error.to_string(),
        },
    }
}

