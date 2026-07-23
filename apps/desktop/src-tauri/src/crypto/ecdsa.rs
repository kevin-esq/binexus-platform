use crate::error::{AppError, AppResult};
use base64::{
    engine::general_purpose::{STANDARD, URL_SAFE_NO_PAD},
    Engine,
};
use p256::{
    ecdsa::{
        signature::{Signer, Verifier},
        Signature, SigningKey, VerifyingKey,
    },
    pkcs8::{DecodePrivateKey, DecodePublicKey, EncodePrivateKey, EncodePublicKey},
};
use sha2::{Digest, Sha256};

pub const FINGERPRINT_SHORT_HEX_LEN: usize = 12;

pub fn generate_key_material() -> AppResult<(String, String)> {
    let key = SigningKey::random(&mut rand::thread_rng());
    let pkcs8 = key.to_pkcs8_der().map_err(|_| AppError::Internal)?;
    Ok((
        STANDARD.encode(pkcs8.as_bytes()),
        public_key_base64url(&key)?,
    ))
}
pub fn public_key_base64url(key: &SigningKey) -> AppResult<String> {
    let der = VerifyingKey::from(key)
        .to_public_key_der()
        .map_err(|_| AppError::Internal)?;
    Ok(URL_SAFE_NO_PAD.encode(der.as_bytes()))
}
pub fn fingerprint(public_key_base64url: &str) -> AppResult<String> {
    let der = URL_SAFE_NO_PAD
        .decode(public_key_base64url)
        .map_err(|_| AppError::Internal)?;
    Ok(hex::encode(Sha256::digest(der)))
}

/// Display-only short fingerprint: first 12 hex chars as `A1B2-C3D4-E5F6` (matches Branch admin).
pub fn to_short_display(full_hex_fingerprint: &str) -> AppResult<String> {
    if full_hex_fingerprint.len() < FINGERPRINT_SHORT_HEX_LEN {
        return Err(AppError::Internal);
    }
    let head = full_hex_fingerprint[..FINGERPRINT_SHORT_HEX_LEN].to_ascii_uppercase();
    Ok(format!("{}-{}-{}", &head[0..4], &head[4..8], &head[8..12]))
}

pub fn fingerprint_short_display(public_key_base64url: &str) -> AppResult<String> {
    to_short_display(&fingerprint(public_key_base64url)?)
}

pub fn fingerprint_short_from_pkcs8(pkcs8_base64: &str) -> AppResult<String> {
    to_short_display(&fingerprint_from_pkcs8(pkcs8_base64)?)
}

pub fn fingerprint_from_pkcs8(pkcs8_base64: &str) -> AppResult<String> {
    let der = STANDARD
        .decode(pkcs8_base64)
        .map_err(|_| AppError::Internal)?;
    let key = SigningKey::from_pkcs8_der(&der).map_err(|_| AppError::Internal)?;
    fingerprint(&public_key_base64url(&key)?)
}

pub fn credential_hash(credential_base64url: &str) -> String {
    hex::encode(Sha256::digest(credential_base64url.as_bytes()))
}
pub fn sign(pkcs8_base64: &str, payload: &[u8]) -> AppResult<String> {
    let der = STANDARD
        .decode(pkcs8_base64)
        .map_err(|_| AppError::Internal)?;
    let key = SigningKey::from_pkcs8_der(&der).map_err(|_| AppError::Internal)?;
    let signature: Signature = key.sign(payload);
    Ok(URL_SAFE_NO_PAD.encode(signature.to_bytes()))
}
pub fn verify(
    public_key_base64url: &str,
    payload: &[u8],
    signature_base64url: &str,
) -> AppResult<bool> {
    let key = VerifyingKey::from_public_key_der(
        &URL_SAFE_NO_PAD
            .decode(public_key_base64url)
            .map_err(|_| AppError::Internal)?,
    )
    .map_err(|_| AppError::Internal)?;
    let signature = Signature::from_slice(
        &URL_SAFE_NO_PAD
            .decode(signature_base64url)
            .map_err(|_| AppError::Internal)?,
    )
    .map_err(|_| AppError::Internal)?;
    Ok(key.verify(payload, &signature).is_ok())
}

#[cfg(test)]
mod fingerprint_display_tests {
    use super::*;

    #[test]
    fn short_display_matches_branch_admin_format() {
        let full = "a1b2c3d4e5f6789012345678abcdef0123456789abcdef0123456789abcdef01";
        assert_eq!(to_short_display(full).unwrap(), "A1B2-C3D4-E5F6");
    }

    #[test]
    fn short_from_pkcs8_is_stable_for_same_key() {
        let (pkcs8, public) = generate_key_material().unwrap();
        let a = fingerprint_short_from_pkcs8(&pkcs8).unwrap();
        let b = fingerprint_short_display(&public).unwrap();
        assert_eq!(a, b);
        assert_eq!(a, fingerprint_short_from_pkcs8(&pkcs8).unwrap());
        assert!(a.contains('-'));
        assert_eq!(a.len(), 14);
    }
}
