use super::{validate_branch_url, ProblemDetails};
use crate::error::{AppError, AppResult};
use chrono::{DateTime, Utc};
use reqwest::{redirect::Policy, Client, Response};
use serde::{de::DeserializeOwned, Deserialize};
use url::Url;
use uuid::Uuid;

#[derive(Clone)]
pub struct BranchClient {
    base: Url,
    http: Client,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingChallenge {
    pub challenge_id: Uuid,
    pub branch_instance_id: Uuid,
    pub nonce: String,
    pub expires_at_utc: DateTime<Utc>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingExchange {
    pub pairing_request_id: Uuid,
    pub device_fingerprint_short: String,
    pub status: String,
    pub pairing_status_token: String,
    pub expires_at_utc: DateTime<Utc>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingStatus {
    pub pairing_request_id: Uuid,
    pub status: String,
    pub branch_instance_id: Uuid,
    pub terminal_id: Option<Uuid>,
    pub confirmation_challenge_id: Option<Uuid>,
    pub confirmation_nonce: Option<String>,
    pub confirmation_expires_at_utc: Option<DateTime<Utc>>,
    pub pairing_receipt: Option<String>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReceiptChallenge {
    pub challenge_id: Uuid,
    pub branch_instance_id: Uuid,
    pub nonce: String,
    pub expires_at_utc: DateTime<Utc>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReceiptReissue {
    pub pairing_request_id: Uuid,
    pub branch_instance_id: Uuid,
    pub terminal_id: Uuid,
    pub pairing_receipt: String,
    pub confirmation_challenge_id: Uuid,
    pub confirmation_nonce: String,
    pub confirmation_expires_at_utc: DateTime<Utc>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingConfirm {
    pub pairing_request_id: Uuid,
    pub device_id: Uuid,
    pub terminal_id: Uuid,
    pub status: String,
    pub already_active: bool,
}

impl BranchClient {
    pub fn new(base: Url) -> AppResult<Self> {
        let http = Client::builder()
            .redirect(Policy::none())
            .build()
            .map_err(|_| AppError::Network)?;
        Ok(Self { base, http })
    }

    /// Configured Branch base URL (normalized without a trailing slash).
    pub fn base_url(&self) -> &str {
        self.base.as_str().trim_end_matches('/')
    }
    fn endpoint(&self, path: &str) -> AppResult<Url> {
        validate_branch_url(self.base.as_str())?;
        self.base.join(path).map_err(|_| AppError::Network)
    }
    async fn json<T: DeserializeOwned>(
        &self,
        response: Result<Response, reqwest::Error>,
    ) -> AppResult<T> {
        let response = response.map_err(|_| AppError::Network)?;
        if response.status().is_success() {
            return response.json().await.map_err(|_| AppError::Network);
        }
        let _problem: Result<ProblemDetails, _> = response.json().await;
        Err(AppError::Pairing)
    }
    pub async fn health(&self) -> AppResult<serde_json::Value> {
        self.json(self.http.get(self.endpoint("health/branch")?).send().await)
            .await
    }
    pub async fn challenge(
        &self,
        session_id: Uuid,
        code: &str,
        device_id: Uuid,
        public_key: &str,
        credential_hash: &str,
    ) -> AppResult<PairingChallenge> {
        self.json(self.http.post(self.endpoint("branch/pairing/challenges")?).json(&serde_json::json!({"pairingSessionId":session_id,"pairingCode":code,"deviceId":device_id,"publicKey":public_key,"credentialHash":credential_hash})).send().await).await
    }
    pub async fn exchange(&self, request: serde_json::Value) -> AppResult<PairingExchange> {
        self.json(
            self.http
                .post(self.endpoint("branch/pairing/exchange")?)
                .json(&request)
                .send()
                .await,
        )
        .await
    }
    pub async fn status(&self, request_id: Uuid, token: &str) -> AppResult<PairingStatus> {
        self.json(
            self.http
                .post(self.endpoint(&format!("branch/pairing/requests/{request_id}/status"))?)
                .json(&serde_json::json!({"pairingStatusToken":token}))
                .send()
                .await,
        )
        .await
    }
    pub async fn receipt_challenge(
        &self,
        request_id: Uuid,
        token: &str,
    ) -> AppResult<ReceiptChallenge> {
        self.json(
            self.http
                .post(self.endpoint(&format!(
                    "branch/pairing/requests/{request_id}/receipt/challenges"
                ))?)
                .json(&serde_json::json!({"pairingStatusToken":token}))
                .send()
                .await,
        )
        .await
    }
    pub async fn reissue(
        &self,
        request_id: Uuid,
        token: &str,
        challenge_id: Uuid,
        signature: &str,
    ) -> AppResult<ReceiptReissue> {
        self.json(self.http.post(self.endpoint(&format!("branch/pairing/requests/{request_id}/receipt/reissue"))?).json(&serde_json::json!({"pairingStatusToken":token,"reissueChallengeId":challenge_id,"signature":signature})).send().await).await
    }
    pub async fn confirm(&self, request: serde_json::Value) -> AppResult<PairingConfirm> {
        self.json(
            self.http
                .post(self.endpoint("branch/pairing/confirm")?)
                .json(&request)
                .send()
                .await,
        )
        .await
    }

    pub async fn device_auth_challenge(&self, device_id: Uuid) -> AppResult<DeviceAuthChallenge> {
        self.json_device_auth(
            self.http
                .post(self.endpoint("branch/device-auth/challenges")?)
                .json(&serde_json::json!({ "deviceId": device_id }))
                .send()
                .await,
        )
        .await
    }

    pub async fn device_auth_tokens(
        &self,
        challenge_id: Uuid,
        device_id: Uuid,
        signature: &str,
        protocol_version: &str,
    ) -> AppResult<DeviceAuthToken> {
        self.json_device_auth(
            self.http
                .post(self.endpoint("branch/device-auth/tokens")?)
                .json(&serde_json::json!({
                    "challengeId": challenge_id,
                    "deviceId": device_id,
                    "signature": signature,
                    "protocolVersion": protocol_version,
                }))
                .send()
                .await,
        )
        .await
    }

    pub async fn device_auth_me(&self, device_authorization: &str) -> AppResult<DeviceAuthMe> {
        self.json_device_auth(
            self.http
                .get(self.endpoint("branch/device-auth/me")?)
                .header(crate::device_auth::DEVICE_AUTH_HEADER, device_authorization)
                .send()
                .await,
        )
        .await
    }

    /// Calls an operational endpoint with a DAT and, when supplied, the user JWT.
    /// The response body is deliberately discarded because callers only need authorization status.
    pub async fn device_auth_operational_status(
        &self,
        device_authorization: &str,
        user_jwt: Option<&str>,
    ) -> AppResult<u16> {
        let mut request = self
            .http
            .get(self.endpoint("sales/sessions/current")?)
            .header(crate::device_auth::DEVICE_AUTH_HEADER, device_authorization);
        if let Some(user_jwt) = user_jwt {
            request = request.bearer_auth(user_jwt);
        }

        let response = request.send().await.map_err(|_| AppError::Network)?;
        if response.status().is_success() {
            return Ok(response.status().as_u16());
        }

        let status = response.status().as_u16();
        let error = self.device_auth_error(response).await;
        match error {
            AppError::DeviceAuth { .. } => Ok(status),
            error => Err(error),
        }
    }

    async fn json_device_auth<T: DeserializeOwned>(
        &self,
        response: Result<Response, reqwest::Error>,
    ) -> AppResult<T> {
        let response = response.map_err(|_| AppError::Network)?;
        if response.status().is_success() {
            return response.json().await.map_err(|_| AppError::Network);
        }
        Err(self.device_auth_error(response).await)
    }

    async fn device_auth_error(&self, response: Response) -> AppError {
        let status = response.status();
        let problem: Result<ProblemDetails, _> = response.json().await;
        let code = problem.ok().and_then(|value| value.code.or(value.title));
        match (status.as_u16(), code.as_deref()) {
            (401, Some("DEVICE_TOKEN_EXPIRED")) | (401, Some("DEVICE_AUTH_REQUIRED")) => {
                AppError::DeviceSessionExpired
            }
            (403, Some("DEVICE_REVOKED")) | (403, Some("DEVICE_NOT_ACTIVE")) => {
                AppError::DeviceRevoked
            }
            (403, Some("DEVICE_BRANCH_MISMATCH")) | (403, Some("USER_BRANCH_MISMATCH")) => {
                AppError::BranchIdentityMismatch
            }
            _ => AppError::DeviceAuth { code },
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceAuthChallenge {
    pub challenge_id: Uuid,
    pub nonce: String,
    pub branch_instance_id: Uuid,
    pub expires_at_utc: DateTime<Utc>,
    pub protocol_version: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceAuthToken {
    pub access_token: String,
    pub token_type: String,
    pub expires_at_utc: DateTime<Utc>,
    pub device_id: Uuid,
    pub terminal_id: Uuid,
    pub branch_instance_id: Uuid,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceAuthMe {
    pub device_id: Uuid,
    pub status: String,
    pub terminal_id: Uuid,
    pub branch_instance_id: Uuid,
    pub tenant_id: Uuid,
    pub branch_id: Uuid,
}
