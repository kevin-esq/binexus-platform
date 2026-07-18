use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub enum ConfigStatus {
    Uninitialized,
    ServerConfigured,
    PairingInProgress,
    Paired,
    RecoveryRequired,
    PairedCredentialsUnavailable,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct DesktopConfig {
    pub schema_version: u32,
    pub device_id: Option<Uuid>,
    pub branch_base_url: Option<String>,
    pub branch_instance_id: Option<Uuid>,
    pub terminal_id: Option<Uuid>,
    pub pairing_request_id: Option<Uuid>,
    pub status: ConfigStatus,
}

impl Default for DesktopConfig {
    fn default() -> Self {
        Self {
            schema_version: 1,
            device_id: None,
            branch_base_url: None,
            branch_instance_id: None,
            terminal_id: None,
            pairing_request_id: None,
            status: ConfigStatus::Uninitialized,
        }
    }
}
