use std::fs;
use std::path::PathBuf;

use base64::engine::general_purpose::{STANDARD as B64, URL_SAFE_NO_PAD};
use base64::Engine as _;
use p256::ecdsa::{signature::Signer, Signature, SigningKey, VerifyingKey};
use p256::pkcs8::{DecodePrivateKey, EncodePublicKey};
use serde::Deserialize;
use sha2::{Digest, Sha256};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GoldenVectorDocument {
    exchange_version: String,
    public_key_base64_url: String,
    public_key_fingerprint_sha256_hex: String,
    #[allow(dead_code)]
    fingerprint_short_display: String,
    private_key_pkcs8_base64: String,
    exchange: GoldenVectorCase,
    confirm: GoldenVectorCase,
    receipt_reissue: GoldenVectorCase,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GoldenVectorCase {
    name: String,
    canonical_payload_hex: String,
    signature_base64_url: String,
}

fn main() {
    let fixture = locate_fixture();
    let raw = fs::read_to_string(&fixture).expect("read fixture");
    let doc: GoldenVectorDocument = serde_json::from_str(&raw).expect("parse fixture");

    let pkcs8 = B64.decode(doc.private_key_pkcs8_base64).expect("pkcs8 b64");
    let signing_key = SigningKey::from_pkcs8_der(&pkcs8).expect("pkcs8 der");
    let verifying_key = VerifyingKey::from(&signing_key);
    let spki = verifying_key
        .to_public_key_der()
        .expect("spki der");
    let public_key_b64url = URL_SAFE_NO_PAD.encode(spki.as_bytes());
    let fingerprint = hex::encode(Sha256::digest(spki.as_bytes()));

    assert_eq!(public_key_b64url, doc.public_key_base64_url, "public key mismatch");
    assert_eq!(fingerprint, doc.public_key_fingerprint_sha256_hex, "fingerprint mismatch");

    verify_case("exchange", &doc.exchange, &signing_key, &verifying_key);
    verify_case("confirm", &doc.confirm, &signing_key, &verifying_key);
    verify_case("receipt-reissue", &doc.receipt_reissue, &signing_key, &verifying_key);

    // Rust re-sign: ECDSA k is non-deterministic — verify roundtrip, not byte equality with C#.
    let mut rust_signatures = Vec::new();
    for case in [&doc.exchange, &doc.confirm, &doc.receipt_reissue] {
        let payload = hex::decode(&case.canonical_payload_hex).expect("payload hex");
        let sig: Signature = signing_key.sign(&payload);
        use p256::ecdsa::signature::Verifier;
        verifying_key
            .verify(&payload, &sig)
            .unwrap_or_else(|_| panic!("{} rust roundtrip verify failed", case.name));
        rust_signatures.push((
            case.name.clone(),
            URL_SAFE_NO_PAD.encode(sig.to_bytes()),
        ));
    }

    println!(
        "{{\"ok\":true,\"fixture\":\"{}\",\"exchange_version\":\"{}\",\"rust_signatures\":{}}}",
        fixture.display(),
        doc.exchange_version,
        serde_json::to_string(&rust_signatures).expect("json")
    );
}

fn verify_case(name: &str, case: &GoldenVectorCase, _signing_key: &SigningKey, verifying_key: &VerifyingKey) {
    let payload = hex::decode(&case.canonical_payload_hex).expect("payload hex");
    let signature_bytes = URL_SAFE_NO_PAD.decode(&case.signature_base64_url).expect("sig b64");
    let signature = Signature::from_slice(&signature_bytes).expect("sig parse");
    use p256::ecdsa::signature::Verifier;
    verifying_key
        .verify(&payload, &signature)
        .unwrap_or_else(|_| panic!("{name} C# signature verify failed in Rust"));
}

fn locate_fixture() -> PathBuf {
    let mut dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    dir.push("../fixtures/pairing-crypto-golden-v1.json");
    dir
}
