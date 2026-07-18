use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct PairingEnvelope {
    pub request_id: Option<Uuid>,
    pub status_token: Option<String>,
    pub receipt: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct SecretEnvelopeV1 {
    pub schema_version: u32,
    pub device_id: Uuid,
    pub private_key_pkcs8_base64: String,
    pub device_credential_base64url: String,
    pub pairing: PairingEnvelope,
}

impl SecretEnvelopeV1 {
    pub const SCHEMA_VERSION: u32 = 1;
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn serializes_envelope_v1() {
        let value = SecretEnvelopeV1 {
            schema_version: 1,
            device_id: Uuid::nil(),
            private_key_pkcs8_base64: "key".into(),
            device_credential_base64url: "credential".into(),
            pairing: PairingEnvelope::default(),
        };
        assert_eq!(
            serde_json::from_str::<SecretEnvelopeV1>(&serde_json::to_string(&value).unwrap())
                .unwrap(),
            value
        );
    }
}
