use super::{CONFIRM_VERSION, EXCHANGE_VERSION, RECEIPT_REISSUE_VERSION};
use chrono::{DateTime, Utc};
use uuid::Uuid;

fn encode(fields: &[String]) -> Vec<u8> {
    let mut output = Vec::new();
    for field in fields {
        let bytes = field.as_bytes();
        assert!(bytes.len() <= u16::MAX as usize, "pairing field too long");
        output.extend_from_slice(&(bytes.len() as u16).to_be_bytes());
        output.extend_from_slice(bytes);
    }
    output
}

fn timestamp(value: DateTime<Utc>) -> String {
    // Match .NET DateTimeOffset.UtcDateTime.ToString("O") used by CanonicalDevicePairingChallengeCodec.
    format!(
        "{}.{:07}Z",
        value.format("%Y-%m-%dT%H:%M:%S"),
        value.timestamp_subsec_nanos() / 100
    )
}

pub fn encode_exchange(
    challenge_id: Uuid,
    branch_instance_id: Uuid,
    session_id: Uuid,
    device_id: Uuid,
    fingerprint: &str,
    credential_hash: &str,
    nonce: &str,
    expires_at: DateTime<Utc>,
) -> Vec<u8> {
    encode(&[
        EXCHANGE_VERSION.into(),
        challenge_id.to_string(),
        branch_instance_id.to_string(),
        session_id.to_string(),
        device_id.to_string(),
        fingerprint.into(),
        credential_hash.into(),
        nonce.into(),
        timestamp(expires_at),
    ])
}
pub fn encode_receipt_reissue(
    challenge_id: Uuid,
    request_id: Uuid,
    branch_instance_id: Uuid,
    device_id: Uuid,
    fingerprint: &str,
    credential_hash: &str,
    nonce: &str,
    expires_at: DateTime<Utc>,
) -> Vec<u8> {
    encode(&[
        RECEIPT_REISSUE_VERSION.into(),
        challenge_id.to_string(),
        request_id.to_string(),
        branch_instance_id.to_string(),
        device_id.to_string(),
        fingerprint.into(),
        credential_hash.into(),
        nonce.into(),
        timestamp(expires_at),
    ])
}
pub fn encode_confirm(
    challenge_id: Uuid,
    request_id: Uuid,
    branch_instance_id: Uuid,
    device_id: Uuid,
    terminal_id: Uuid,
    fingerprint: &str,
    credential_hash: &str,
    receipt_hash: &str,
    nonce: &str,
    expires_at: DateTime<Utc>,
) -> Vec<u8> {
    encode(&[
        CONFIRM_VERSION.into(),
        challenge_id.to_string(),
        request_id.to_string(),
        branch_instance_id.to_string(),
        device_id.to_string(),
        terminal_id.to_string(),
        fingerprint.into(),
        credential_hash.into(),
        receipt_hash.into(),
        nonce.into(),
        timestamp(expires_at),
    ])
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn roundtrips_length_prefixed_utf8() {
        let encoded = encode(&["é".into(), "x".into()]);
        assert_eq!(encoded, vec![0, 2, 0xc3, 0xa9, 0, 1, b'x']);
    }
    #[test]
    fn exchange_starts_with_version() {
        let id = Uuid::nil();
        let encoded = encode_exchange(id, id, id, id, "f", "h", "n", DateTime::UNIX_EPOCH);
        assert!(encoded.starts_with(&[0x00, EXCHANGE_VERSION.len() as u8]));
        assert!(String::from_utf8_lossy(&encoded).contains(EXCHANGE_VERSION));
    }
}
