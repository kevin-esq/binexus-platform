use super::{
    CONFIRM_VERSION, DEVICE_AUTH_AUDIENCE, DEVICE_AUTH_CHALLENGE_VERSION, EXCHANGE_VERSION,
    RECEIPT_REISSUE_VERSION,
};
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

pub fn encode_device_auth_challenge(
    challenge_id: Uuid,
    nonce: &str,
    device_id: Uuid,
    branch_instance_id: Uuid,
    credential_hash: &str,
    fingerprint: &str,
    expires_at: DateTime<Utc>,
) -> Vec<u8> {
    encode(&[
        DEVICE_AUTH_CHALLENGE_VERSION.into(),
        challenge_id.to_string(),
        nonce.into(),
        device_id.to_string(),
        branch_instance_id.to_string(),
        DEVICE_AUTH_AUDIENCE.into(),
        credential_hash.into(),
        fingerprint.into(),
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

    #[test]
    fn device_auth_golden_vector_matches_csharp_canonical_bytes_and_signature() {
        let payload = encode_device_auth_challenge(
            Uuid::parse_str("0194f0a0-0000-7000-8000-000000000001").unwrap(),
            "nonce-value-1",
            Uuid::parse_str("0194f0a0-0000-7000-8000-000000000002").unwrap(),
            Uuid::parse_str("0194f0a0-0000-7000-8000-000000000003").unwrap(),
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "2026-07-18T12:00:00.0000000Z".parse().unwrap(),
        );
        assert_eq!(
            hex::encode(&payload),
            include_str!("../../../spikes/fixtures/device-auth-crypto-golden-v1.json")
                .split("\"canonicalPayloadHex\": \"")
                .nth(1)
                .and_then(|value| value.split('"').next())
                .unwrap()
        );
        let signature = crate::crypto::sign(
            "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgAQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyChRANCAARRXD1uueOWuQTT/sp/VP3NDMHpl783XcpRWtCmw7QDX0U2vjpQ8xj7+aVHWQKiIVAr7w1X4IxTsswKVvF9n5NU",
            &payload,
        ).unwrap();
        assert!(crate::crypto::verify(
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUVw9brnjlrkE0_7Kf1T9zQzB6Ze_N13KUVrQpsO0A19FNr46UPMY-_mlR1kCoiFQK-8NV-CMU7LMClbxfZ-TVA",
            &payload,
            &signature
        ).unwrap());
    }
}
