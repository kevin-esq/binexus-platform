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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::branch::BranchClient;
    use crate::crypto;
    use crate::secrets::{InMemorySecretStore, PairingEnvelope, SecretEnvelopeV1};
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::time::Duration as StdDuration;
    use tokio::sync::Barrier;
    use wiremock::{
        matchers::{method, path},
        Match, Mock, MockServer, Request, ResponseTemplate,
    };

    const BRANCH_INSTANCE: u128 = 12;
    const DEVICE: u128 = 7;
    const TERMINAL: u128 = 13;

    fn paired_identity(branch_base_url: impl Into<String>) -> DeviceAuthIdentity {
        DeviceAuthIdentity {
            branch_instance_id: Uuid::from_u128(BRANCH_INSTANCE),
            device_id: Uuid::from_u128(DEVICE),
            terminal_id: Some(Uuid::from_u128(TERMINAL)),
            branch_base_url: branch_base_url.into(),
        }
    }

    #[test]
    fn public_state_never_exposes_token_fields() {
        let json = serde_json::to_string(&DeviceSessionPublicState::DeviceAuthenticated {
            device_id: Uuid::nil(),
            terminal_id: Uuid::nil(),
            branch_instance_id: Uuid::nil(),
            expires_at_utc: DateTime::UNIX_EPOCH,
        })
        .unwrap();
        assert!(!json.contains("accessToken"));
        assert!(!json.contains("Bearer"));
        assert!(!json.to_lowercase().contains("token"));
        for forbidden in ["signature", "nonce", "credentialHash", "securityStamp"] {
            assert!(!json.contains(forbidden));
        }
    }

    #[test]
    fn clear_returns_inactive() {
        let store = Arc::new(InMemorySecretStore::default());
        let session = DeviceAuthSession::new(store);
        {
            let mut guard = session.inner.lock();
            guard.dat = Some(DatMaterial {
                access_token: Zeroizing::new("secret-dat-must-not-leak".into()),
                expires_at_utc: Utc::now() + Duration::minutes(5),
                device_id: Uuid::nil(),
                terminal_id: Uuid::nil(),
                branch_instance_id: Uuid::nil(),
            });
        }
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::DeviceAuthenticated { .. }
        ));
        session.clear();
        assert_eq!(
            session.public_state(),
            DeviceSessionPublicState::DeviceSessionExpired
        );
        assert!(session.authorization_value().is_none());
    }

    #[test]
    fn memory_store_roundtrip_keeps_envelope_out_of_public_state() {
        let store = Arc::new(InMemorySecretStore::default());
        let envelope = SecretEnvelopeV1 {
            schema_version: 1,
            device_id: Uuid::nil(),
            private_key_pkcs8_base64: "key".into(),
            device_credential_base64url: "cred".into(),
            pairing: PairingEnvelope::default(),
        };
        store.set(&envelope).unwrap();
        let session = DeviceAuthSession::new(store);
        let state = serde_json::to_string(&session.public_state()).unwrap();
        assert!(!state.contains("key"));
        assert!(!state.contains("cred"));
    }

    #[test]
    fn restart_loses_ram_only_dat() {
        let store = Arc::new(InMemorySecretStore::default());
        let session = DeviceAuthSession::new(Arc::clone(&store) as Arc<dyn SecretStore>);
        session.inner.lock().dat = Some(DatMaterial {
            access_token: Zeroizing::new("dat-is-ram-only".into()),
            expires_at_utc: Utc::now() + Duration::minutes(5),
            device_id: Uuid::nil(),
            terminal_id: Uuid::nil(),
            branch_instance_id: Uuid::nil(),
        });
        assert!(session.authorization_value().is_some());

        let restarted = DeviceAuthSession::new(store);
        assert!(restarted.authorization_value().is_none());
        assert_eq!(
            restarted.public_state(),
            DeviceSessionPublicState::DeviceSessionExpired
        );
    }

    #[test]
    fn renew_skew_is_sixty_seconds() {
        assert_eq!(RENEW_SKEW, Duration::seconds(60));
    }

    #[test]
    fn dat_material_access_token_is_zeroizing_string() {
        trait AssertZeroizingString {}
        impl AssertZeroizingString for Zeroizing<String> {}

        fn assert_access_token_field<T: AssertZeroizingString>(_value: &T) {}

        let material = DatMaterial {
            access_token: Zeroizing::new("secret".into()),
            expires_at_utc: Utc::now(),
            device_id: Uuid::nil(),
            terminal_id: Uuid::nil(),
            branch_instance_id: Uuid::nil(),
        };
        assert_access_token_field(&material.access_token);
        assert_eq!(
            std::any::type_name_of_val(&material.access_token),
            std::any::type_name::<Zeroizing<String>>()
        );
    }

    fn seed_dat(
        session: &DeviceAuthSession,
        expires_at_utc: DateTime<Utc>,
        branch_instance_id: Uuid,
    ) {
        session.inner.lock().dat = Some(DatMaterial {
            access_token: Zeroizing::new("existing-ram-only-dat".into()),
            expires_at_utc,
            device_id: Uuid::from_u128(DEVICE),
            terminal_id: Uuid::from_u128(TERMINAL),
            branch_instance_id,
        });
    }

    fn paired_store() -> Arc<dyn SecretStore> {
        let store = Arc::new(InMemorySecretStore::default());
        let (private_key_pkcs8_base64, _) = crypto::generate_key_material().unwrap();
        store
            .set(&SecretEnvelopeV1 {
                schema_version: 1,
                device_id: Uuid::from_u128(DEVICE),
                private_key_pkcs8_base64,
                device_credential_base64url: "credential".into(),
                pairing: PairingEnvelope::default(),
            })
            .unwrap();
        store
    }

    struct CountRequests(Arc<AtomicUsize>);

    impl Match for CountRequests {
        fn matches(&self, _: &Request) -> bool {
            self.0.fetch_add(1, Ordering::SeqCst);
            true
        }
    }

    async fn mount_happy_path(
        server: &MockServer,
        expires: &str,
    ) -> (Arc<AtomicUsize>, Arc<AtomicUsize>) {
        let challenges = Arc::new(AtomicUsize::new(0));
        let tokens = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .and(CountRequests(Arc::clone(&tokens)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(server)
            .await;
        (challenges, tokens)
    }

    #[tokio::test]
    async fn concurrent_ensure_access_token_shares_one_issue() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        let (_challenges, issued) = mount_happy_path(&server, &expires).await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = paired_identity(client.base_url());
        let (first, second) = tokio::join!(
            session.ensure_access_token(&client, &identity),
            session.ensure_access_token(&client, &identity)
        );
        assert!(first.unwrap().starts_with("Bearer "));
        assert!(second.unwrap().starts_with("Bearer "));
        assert_eq!(issued.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn single_flight_ten_callers_share_one_challenge_and_token() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        let challenges = Arc::new(AtomicUsize::new(0));
        let tokens = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(
                ResponseTemplate::new(200)
                    .set_delay(StdDuration::from_millis(50))
                    .set_body_json(serde_json::json!({
                        "challengeId": Uuid::from_u128(11), "nonce": "nonce",
                        "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                        "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
                    })),
            )
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .and(CountRequests(Arc::clone(&tokens)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "shared-ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = Arc::new(paired_identity(client.base_url()));
        let barrier = Arc::new(Barrier::new(10));
        let mut handles = Vec::new();
        for _ in 0..10 {
            let session = Arc::clone(&session);
            let client = Arc::clone(&client);
            let identity = Arc::clone(&identity);
            let barrier = Arc::clone(&barrier);
            handles.push(tokio::spawn(async move {
                barrier.wait().await;
                session.ensure_access_token(&client, &identity).await
            }));
        }
        let mut results = Vec::new();
        for handle in handles {
            results.push(handle.await.unwrap());
        }
        assert!(results.iter().all(|r| {
            r.as_ref()
                .is_ok_and(|v| v.as_str() == "Bearer shared-ram-only-dat")
        }));
        assert_eq!(challenges.load(Ordering::SeqCst), 1);
        assert_eq!(tokens.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn single_flight_ten_callers_share_leader_failure_without_extra_flight() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(500).set_delay(StdDuration::from_millis(50)))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = Arc::new(paired_identity(client.base_url()));
        let barrier = Arc::new(Barrier::new(10));
        let mut handles = Vec::new();
        for _ in 0..10 {
            let session = Arc::clone(&session);
            let client = Arc::clone(&client);
            let identity = Arc::clone(&identity);
            let barrier = Arc::clone(&barrier);
            handles.push(tokio::spawn(async move {
                barrier.wait().await;
                session.ensure_access_token(&client, &identity).await
            }));
        }
        let mut results = Vec::new();
        for handle in handles {
            results.push(handle.await.unwrap());
        }
        assert!(results.iter().all(|r| r.is_err()));
        let first_err = format!("{:?}", results[0].as_ref().unwrap_err());
        assert!(results
            .iter()
            .all(|r| format!("{:?}", r.as_ref().unwrap_err()) == first_err));
        assert_eq!(challenges.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn renew_when_expiry_within_skew_triggers_reissue() {
        let server = MockServer::start().await;
        let tokens = Arc::new(AtomicUsize::new(0));
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .and(CountRequests(Arc::clone(&tokens)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "reissued-ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        seed_dat(
            &session,
            Utc::now() + Duration::seconds(59),
            Uuid::from_u128(BRANCH_INSTANCE),
        );
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert_eq!(
            session
                .ensure_access_token(&client, &identity)
                .await
                .unwrap()
                .as_str(),
            "Bearer reissued-ram-only-dat"
        );
        assert_eq!(tokens.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn branch_instance_mismatch_clears_dat() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(99),
                "expiresAtUtc": (Utc::now() + Duration::minutes(5)).to_rfc3339(),
                "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        seed_dat(
            &session,
            Utc::now() + Duration::seconds(59),
            Uuid::from_u128(BRANCH_INSTANCE),
        );
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert!(session.authorization_value().is_none());
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::BranchIdentityMismatch { .. }
        ));
    }

    #[tokio::test]
    async fn restart_without_dat_rejects_foreign_branch_challenge_without_signing() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        let tokens = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(99),
                "expiresAtUtc": (Utc::now() + Duration::minutes(5)).to_rfc3339(),
                "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .and(CountRequests(Arc::clone(&tokens)))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert_eq!(challenges.load(Ordering::SeqCst), 1);
        assert_eq!(tokens.load(Ordering::SeqCst), 0);
        assert!(session.authorization_value().is_none());
    }

    #[tokio::test]
    async fn restart_without_dat_rejects_token_from_other_branch_instance() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "foreign-branch-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(99)
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert!(session.authorization_value().is_none());
    }

    #[tokio::test]
    async fn token_device_id_mismatch_clears_dat() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "wrong-device-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(77), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert!(session.authorization_value().is_none());
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::BranchIdentityMismatch { .. }
        ));
    }

    #[tokio::test]
    async fn token_terminal_id_mismatch_when_configured_rejects() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "wrong-terminal-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(99),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert!(session.authorization_value().is_none());
    }

    #[tokio::test]
    async fn ensure_with_wrong_url_context_is_identity_mismatch() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        seed_dat(
            &session,
            Utc::now() + Duration::seconds(30),
            Uuid::from_u128(BRANCH_INSTANCE),
        );
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = DeviceAuthIdentity {
            branch_instance_id: Uuid::from_u128(BRANCH_INSTANCE),
            device_id: Uuid::from_u128(DEVICE),
            terminal_id: Some(Uuid::from_u128(TERMINAL)),
            branch_base_url: "https://other-branch.example".into(),
        };

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert_eq!(challenges.load(Ordering::SeqCst), 0);
        assert!(session.authorization_value().is_none());
    }

    #[tokio::test]
    async fn correct_challenge_with_inconsistent_token_rejects() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "inconsistent-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(55)
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::BranchIdentityMismatch)
        ));
        assert!(session.authorization_value().is_none());
    }

    #[test]
    fn branch_url_change_clears_dat() {
        let session = DeviceAuthSession::new(paired_store());
        seed_dat(
            &session,
            Utc::now() + Duration::minutes(5),
            Uuid::from_u128(BRANCH_INSTANCE),
        );

        session.clear_on_branch_url_change();

        assert!(session.authorization_value().is_none());
        assert_eq!(
            session.public_state(),
            DeviceSessionPublicState::DeviceSessionExpired
        );
    }

    #[tokio::test]
    async fn cancel_releases_single_flight() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(
                ResponseTemplate::new(200)
                    .set_delay(StdDuration::from_millis(100))
                    .set_body_json(serde_json::json!({
                        "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                        "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
                    })),
            )
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "post-cancel-ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = Arc::new(paired_identity(client.base_url()));
        let leader = {
            let session = Arc::clone(&session);
            let client = Arc::clone(&client);
            let identity = Arc::clone(&identity);
            tokio::spawn(async move { session.ensure_access_token(&client, &identity).await })
        };
        while !matches!(
            session.public_state(),
            DeviceSessionPublicState::Authenticating
        ) {
            tokio::task::yield_now().await;
        }

        session.clear();

        assert!(matches!(
            leader.await.unwrap(),
            Err(AppError::DeviceSessionExpired)
        ));
        assert!(session.authorization_value().is_none());
        assert_eq!(
            session
                .ensure_access_token(&client, &identity)
                .await
                .unwrap()
                .as_str(),
            "Bearer post-cancel-ram-only-dat"
        );
    }

    #[tokio::test]
    async fn clear_ends_all_single_flight_waiters() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(
                ResponseTemplate::new(200)
                    .set_delay(StdDuration::from_millis(80))
                    .set_body_json(serde_json::json!({
                        "challengeId": Uuid::from_u128(11), "nonce": "nonce",
                        "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                        "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
                    })),
            )
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "should-not-persist", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = Arc::new(paired_identity(client.base_url()));
        let barrier = Arc::new(Barrier::new(5));
        let mut handles = Vec::new();
        for _ in 0..5 {
            let session = Arc::clone(&session);
            let client = Arc::clone(&client);
            let identity = Arc::clone(&identity);
            let barrier = Arc::clone(&barrier);
            handles.push(tokio::spawn(async move {
                barrier.wait().await;
                session.ensure_access_token(&client, &identity).await
            }));
        }
        while !matches!(
            session.public_state(),
            DeviceSessionPublicState::Authenticating
        ) {
            tokio::task::yield_now().await;
        }
        session.clear();

        for handle in handles {
            assert!(matches!(
                handle.await.unwrap(),
                Err(AppError::DeviceSessionExpired)
            ));
        }
        assert!(session.authorization_value().is_none());
    }

    #[tokio::test]
    async fn missing_credentials_does_not_issue_challenge() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(200))
            .mount(&server)
            .await;
        let session = DeviceAuthSession::new(Arc::new(InMemorySecretStore::default()));
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());
        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::CredentialsUnavailable)
        ));
        assert_eq!(challenges.load(Ordering::SeqCst), 0);
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::CredentialsUnavailable { .. }
        ));
    }

    #[tokio::test]
    async fn leader_failure_propagates_to_waiters_and_next_ensure_can_succeed() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(500))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = paired_identity(client.base_url());
        let (first, second) = tokio::join!(
            session.ensure_access_token(&client, &identity),
            session.ensure_access_token(&client, &identity)
        );
        assert!(first.is_err());
        assert!(second.is_err());

        server.reset().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "recovered-ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        assert_eq!(
            session
                .ensure_access_token(&client, &identity)
                .await
                .unwrap()
                .as_str(),
            "Bearer recovered-ram-only-dat"
        );
    }

    #[tokio::test]
    async fn retry_after_failure_can_succeed() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(500))
            .mount(&server)
            .await;
        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());

        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::DeviceAuth { .. })
        ));

        server.reset().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "retried-ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        assert_eq!(
            session
                .ensure_access_token(&client, &identity)
                .await
                .unwrap()
                .as_str(),
            "Bearer retried-ram-only-dat"
        );
    }

    #[tokio::test]
    async fn new_call_after_failed_ten_caller_flight_can_retry() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(500).set_delay(StdDuration::from_millis(40)))
            .mount(&server)
            .await;

        let session = Arc::new(DeviceAuthSession::new(paired_store()));
        let client = Arc::new(BranchClient::new(server.uri().parse().unwrap()).unwrap());
        let identity = Arc::new(paired_identity(client.base_url()));
        let barrier = Arc::new(Barrier::new(10));
        let mut handles = Vec::new();
        for _ in 0..10 {
            let session = Arc::clone(&session);
            let client = Arc::clone(&client);
            let identity = Arc::clone(&identity);
            let barrier = Arc::clone(&barrier);
            handles.push(tokio::spawn(async move {
                barrier.wait().await;
                session.ensure_access_token(&client, &identity).await
            }));
        }
        for handle in handles {
            assert!(handle.await.unwrap().is_err());
        }
        assert_eq!(challenges.load(Ordering::SeqCst), 1);

        server.reset().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "after-failed-flight-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        assert_eq!(
            session
                .ensure_access_token(&client, &identity)
                .await
                .unwrap()
                .as_str(),
            "Bearer after-failed-flight-dat"
        );
    }

    #[tokio::test]
    async fn expired_renewal_attempts_at_most_once_per_ensure_chain() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        let tokens = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": (Utc::now() + Duration::minutes(5)).to_rfc3339(),
                "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .and(CountRequests(Arc::clone(&tokens)))
            .respond_with(ResponseTemplate::new(401).set_body_json(serde_json::json!({
                "title": "DEVICE_TOKEN_EXPIRED", "code": "DEVICE_TOKEN_EXPIRED"
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());
        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::DeviceSessionExpired)
        ));
        assert_eq!(challenges.load(Ordering::SeqCst), 2);
        assert_eq!(tokens.load(Ordering::SeqCst), 2);
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::DeviceSessionFailed { code, .. } if code == "DEVICE_SESSION_EXPIRED"
        ));
    }

    #[tokio::test]
    async fn revoked_response_clears_dat_without_retry() {
        let server = MockServer::start().await;
        let challenges = Arc::new(AtomicUsize::new(0));
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .and(CountRequests(Arc::clone(&challenges)))
            .respond_with(ResponseTemplate::new(403).set_body_json(serde_json::json!({
                "title": "DEVICE_REVOKED", "code": "DEVICE_REVOKED"
            })))
            .mount(&server)
            .await;

        let session = DeviceAuthSession::new(paired_store());
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());
        assert!(matches!(
            session.ensure_access_token(&client, &identity).await,
            Err(AppError::DeviceRevoked)
        ));
        assert_eq!(challenges.load(Ordering::SeqCst), 1);
        assert!(session.authorization_value().is_none());
        assert!(matches!(
            session.public_state(),
            DeviceSessionPublicState::DeviceRevoked { .. }
        ));
    }

    #[test]
    fn app_error_debug_never_contains_device_auth_material() {
        let debug = format!(
            "{:?}",
            AppError::DeviceAuth {
                code: Some("DEVICE_PROOF_INVALID".into())
            }
        );
        for forbidden in [
            "accessToken",
            "signature",
            "nonce",
            "credentialHash",
            "securityStamp",
        ] {
            assert!(!debug.contains(forbidden));
        }
    }

    #[test]
    fn public_state_and_app_error_display_never_expose_device_auth_material() {
        let state = DeviceSessionPublicState::DeviceSessionFailed {
            code: "DEVICE_AUTH_FAILED".into(),
            message: AppError::DeviceAuth {
                code: Some("DEVICE_PROOF_INVALID".into()),
            }
            .to_string(),
        };
        let state_debug = format!("{state:?}");
        let error_display = AppError::DeviceAuth {
            code: Some("DEVICE_PROOF_INVALID".into()),
        }
        .to_string();

        for forbidden in [
            "ram-only-dat",
            "signature",
            "nonce",
            "credentialHash",
            "securityStamp",
            "private_key",
        ] {
            assert!(!state_debug.contains(forbidden));
            assert!(!error_display.contains(forbidden));
        }
        assert!(!format!("{:?}", DeviceSessionPublicState::Authenticating).contains("ram-only-dat"));
    }

    #[tokio::test]
    async fn dat_issue_does_not_mutate_the_secret_envelope() {
        let server = MockServer::start().await;
        let expires = (Utc::now() + Duration::minutes(5)).to_rfc3339();
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/challenges"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "challengeId": Uuid::from_u128(11), "nonce": "nonce", "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE),
                "expiresAtUtc": expires, "protocolVersion": DEVICE_AUTH_CHALLENGE_VERSION
            })))
            .mount(&server)
            .await;
        Mock::given(method("POST"))
            .and(path("/branch/device-auth/tokens"))
            .respond_with(ResponseTemplate::new(200).set_body_json(serde_json::json!({
                "accessToken": "ram-only-dat", "tokenType": "binexus-device-access", "expiresAtUtc": expires,
                "deviceId": Uuid::from_u128(DEVICE), "terminalId": Uuid::from_u128(TERMINAL),
                "branchInstanceId": Uuid::from_u128(BRANCH_INSTANCE)
            })))
            .mount(&server)
            .await;

        let store = Arc::new(InMemorySecretStore::default());
        let (private_key_pkcs8_base64, _) = crypto::generate_key_material().unwrap();
        store
            .set(&SecretEnvelopeV1 {
                schema_version: 1,
                device_id: Uuid::from_u128(DEVICE),
                private_key_pkcs8_base64,
                device_credential_base64url: "credential".into(),
                pairing: PairingEnvelope::default(),
            })
            .unwrap();
        let before = store.get().unwrap();
        let session = DeviceAuthSession::new(Arc::clone(&store) as Arc<dyn SecretStore>);
        let client = BranchClient::new(server.uri().parse().unwrap()).unwrap();
        let identity = paired_identity(client.base_url());
        session
            .ensure_access_token(&client, &identity)
            .await
            .unwrap();
        assert_eq!(store.get().unwrap(), before);
    }
}
