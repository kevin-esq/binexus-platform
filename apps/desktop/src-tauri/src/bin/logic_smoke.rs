//! Additional smoke binary only — not a substitute for `cargo test`.
//! Prefer `cargo test --workspace --all-targets` for discovery, isolation, and CI.
use std::env::temp_dir;
use std::fs;
use std::time::{SystemTime, UNIX_EPOCH};

use base64::{engine::general_purpose::STANDARD, Engine};
use binexus_desktop_lib::branch::validate_branch_url;
use binexus_desktop_lib::config::{ConfigStore, DesktopConfig};
use binexus_desktop_lib::crypto::{
    encode_exchange, fingerprint, generate_key_material, public_key_base64url, sign, verify,
};
use binexus_desktop_lib::secrets::{
    InMemorySecretStore, PairingEnvelope, SecretEnvelopeV1, SecretStore,
};
use binexus_desktop_lib::single_instance;
use chrono::{TimeZone, Utc};
use p256::ecdsa::SigningKey;
use p256::pkcs8::DecodePrivateKey;
use uuid::Uuid;

fn main() {
    assert!(validate_branch_url("http://127.0.0.1:5102").is_ok());
    assert!(validate_branch_url("http://169.254.169.254").is_err());
    assert!(validate_branch_url("http://8.8.8.8").is_err());

    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_millis();
    let dir = temp_dir().join(format!("binexus-logic-smoke-{stamp}"));
    fs::create_dir_all(&dir).expect("temp dir");
    let store = ConfigStore::new(dir.clone());
    let config = DesktopConfig::default();
    store.save(&config).expect("save");
    assert_eq!(store.load().expect("load"), Some(config));

    let (pkcs8, public) = generate_key_material().expect("keys");
    let fp = fingerprint(&public).expect("fp");
    assert_eq!(fp.len(), 64);
    let payload = encode_exchange(
        Uuid::nil(),
        Uuid::nil(),
        Uuid::nil(),
        Uuid::nil(),
        &fp,
        "aa",
        "nonce",
        Utc.with_ymd_and_hms(2026, 7, 17, 12, 0, 0).unwrap(),
    );
    let signature = sign(&pkcs8, &payload).expect("sign");
    assert!(verify(&public, &payload, &signature).expect("verify"));

    let secrets = InMemorySecretStore::default();
    let key = SigningKey::from_pkcs8_der(&STANDARD.decode(&pkcs8).unwrap()).unwrap();
    let _ = public_key_base64url(&key).unwrap();
    secrets
        .set(&SecretEnvelopeV1 {
            schema_version: 1,
            device_id: Uuid::nil(),
            private_key_pkcs8_base64: pkcs8,
            device_credential_base64url: "cred".into(),
            pairing: PairingEnvelope::default(),
        })
        .unwrap();
    assert!(secrets.get().unwrap().is_some());

    let lock = dir.join("instance.lock");
    let _held = single_instance::try_acquire(&lock).expect("first lock");
    assert!(matches!(
        single_instance::try_acquire(&lock),
        Err(single_instance::SingleInstanceError::AlreadyRunning)
    ));
    drop(_held);
    assert!(single_instance::try_acquire(&lock).is_ok());
    let _ = fs::remove_dir_all(&dir);

    println!("{{\"ok\":true,\"suite\":\"logic-smoke\"}}");
}
