use chrono::{DateTime, Utc};
use serde::Serialize;
use uuid::Uuid;

/// Public UI state. Never includes secrets, tokens, receipts, or private keys.
#[derive(Debug, Clone, Serialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum AppUiState {
    Booting,
    NeedsServerSetup {
        #[serde(skip_serializing_if = "Option::is_none")]
        branch_url: Option<String>,
        #[serde(skip_serializing_if = "Option::is_none")]
        message: Option<String>,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
    },
    NeedsPairing {
        branch_url: String,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_name: Option<String>,
        #[serde(skip_serializing_if = "Option::is_none")]
        message: Option<String>,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
    },
    PendingApproval {
        branch_url: String,
        pairing_request_id: Uuid,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
        terminal_name: String,
    },
    Finalizing {
        branch_url: String,
        terminal_name: String,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
    },
    Paired {
        branch_url: String,
        terminal_name: String,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
    },
    RecoveryRequired {
        #[serde(skip_serializing_if = "Option::is_none")]
        message: Option<String>,
        #[serde(skip_serializing_if = "Option::is_none")]
        device_fingerprint_short: Option<String>,
    },
    PairedCredentialsUnavailable {
        #[serde(skip_serializing_if = "Option::is_none")]
        message: Option<String>,
    },
    Blocked {
        message: String,
    },
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PairingProgressEvent {
    pub phase: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fingerprint_short: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub expires_at: Option<DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error_code: Option<String>,
}
