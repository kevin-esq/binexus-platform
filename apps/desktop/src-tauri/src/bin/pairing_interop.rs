//! CI / integration harness: product Rust Branch client against a live Branch Runtime.
//!
//! Protocol (stdout JSON lines, no secrets):
//!   {"event":"ready"}
//!   {"event":"exchanged","pairingRequestId":"...","fingerprintShort":"..."}
//!   {"event":"paired"}
//!   {"event":"error","code":"..."}
//!
//! Env:
//!   BINEXUS_BRANCH_BASE_URL   http://127.0.0.1:<port>
//!   BINEXUS_PAIRING_CODE      {sessionId}:{8-digit-code}
//!   BINEXUS_TERMINAL_NAME     default "Interop Terminal"
//!   BINEXUS_DATA_DIR          profile directory (config + harness envelope file)
//!   BINEXUS_MODE              full | reissue | resume-paired
//!   BINEXUS_POLL_SECS         default 90

use std::env;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
use binexus_desktop_lib::config::{ConfigStatus, ConfigStore};
use binexus_desktop_lib::crypto;
use binexus_desktop_lib::pairing::PairingCeremony;
use binexus_desktop_lib::secrets::{
    FileSecretStore, PairingEnvelope, SecretEnvelopeV1, SecretStore,
};
use parking_lot::Mutex;
use uuid::Uuid;

#[tokio::main]
async fn main() {
    if let Err(code) = run().await {
        println!(r#"{{"event":"error","code":"{code}"}}"#);
        std::process::exit(1);
    }
}

async fn run() -> Result<(), &'static str> {
    let base = env::var("BINEXUS_BRANCH_BASE_URL").map_err(|_| "MISSING_BASE_URL")?;
    let data_dir = env::var("BINEXUS_DATA_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            std::env::temp_dir().join(format!("binexus-interop-{}", Uuid::now_v7()))
        });
    std::fs::create_dir_all(&data_dir).map_err(|_| "DATA_DIR")?;
    let mode = env::var("BINEXUS_MODE").unwrap_or_else(|_| "full".into());
    let poll_secs: u64 = env::var("BINEXUS_POLL_SECS")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(90);

    let config_store = Arc::new(ConfigStore::new(data_dir.clone()));
    let secrets: Arc<dyn SecretStore> =
        Arc::new(FileSecretStore::new(data_dir.join("harness-envelope.json")));

    if secrets.get().map_err(|_| "SECRETS")?.is_none() {
        let device_id = Uuid::now_v7();
        let (pkcs8, _) = crypto::generate_key_material().map_err(|_| "KEYGEN")?;
        let mut credential = [0u8; 32];
        rand::RngCore::fill_bytes(&mut rand::thread_rng(), &mut credential);
        secrets
            .set(&SecretEnvelopeV1 {
                schema_version: SecretEnvelopeV1::SCHEMA_VERSION,
                device_id,
                private_key_pkcs8_base64: pkcs8,
                device_credential_base64url: URL_SAFE_NO_PAD.encode(credential),
                pairing: PairingEnvelope::default(),
            })
            .map_err(|_| "SECRETS")?;
    }

    let envelope = secrets.get().map_err(|_| "SECRETS")?.ok_or("SECRETS")?;
    let mut config = config_store.load().ok().flatten().unwrap_or_default();
    config.schema_version = 1;
    config.device_id = Some(envelope.device_id);
    config.branch_base_url = Some(base);
    if config.status == ConfigStatus::Uninitialized {
        config.status = ConfigStatus::ServerConfigured;
    }
    config_store.save(&config).map_err(|_| "CONFIG")?;

    let config = Arc::new(Mutex::new(config));
    let ceremony = PairingCeremony::new(
        Arc::clone(&config),
        Arc::clone(&config_store),
        Arc::clone(&secrets),
    );

    println!(r#"{{"event":"ready","deviceId":"{}"}}"#, envelope.device_id);

    match mode.as_str() {
        "resume-paired" => {
            let loaded = config_store
                .load()
                .map_err(|_| "CONFIG")?
                .ok_or("NO_CONFIG")?;
            if loaded.status != ConfigStatus::Paired {
                return Err("NOT_PAIRED");
            }
            if secrets.get().map_err(|_| "SECRETS")?.is_none() {
                return Err("CREDENTIALS_MISSING");
            }
            println!(r#"{{"event":"paired","phase":"resume"}}"#);
            Ok(())
        }
        "reissue" => {
            let code = env::var("BINEXUS_PAIRING_CODE").map_err(|_| "MISSING_CODE")?;
            let terminal =
                env::var("BINEXUS_TERMINAL_NAME").unwrap_or_else(|_| "Interop Terminal".into());
            let progress = ceremony
                .exchange(&code, &terminal)
                .await
                .map_err(|_| "EXCHANGE")?;
            let request_id = progress.pairing_request_id.ok_or("NO_REQUEST")?.to_string();
            let fp = progress.fingerprint_short.unwrap_or_default();
            println!(
                r#"{{"event":"exchanged","pairingRequestId":"{request_id}","fingerprintShort":"{fp}"}}"#
            );
            ceremony
                .poll_until_approved(Duration::from_secs(poll_secs))
                .await
                .map_err(|_| "POLL")?;
            let wait = ceremony
                .confirm_forcing_reissue()
                .await
                .map_err(|_| "REISSUE")?;
            if wait.phase != "paired" {
                return Err("NOT_PAIRED");
            }
            println!(r#"{{"event":"paired","path":"reissue"}}"#);
            Ok(())
        }
        "full" => {
            let code = env::var("BINEXUS_PAIRING_CODE").map_err(|_| "MISSING_CODE")?;
            let terminal =
                env::var("BINEXUS_TERMINAL_NAME").unwrap_or_else(|_| "Interop Terminal".into());
            let progress = ceremony
                .exchange(&code, &terminal)
                .await
                .map_err(|_| "EXCHANGE")?;
            let request_id = progress.pairing_request_id.ok_or("NO_REQUEST")?.to_string();
            let fp = progress.fingerprint_short.unwrap_or_default();
            println!(
                r#"{{"event":"exchanged","pairingRequestId":"{request_id}","fingerprintShort":"{fp}"}}"#
            );

            let wait = ceremony
                .poll_until_terminal(Duration::from_secs(poll_secs))
                .await
                .map_err(|_| "POLL")?;
            if wait.phase != "paired" {
                return Err("NOT_PAIRED");
            }
            println!(r#"{{"event":"paired","path":"full"}}"#);
            Ok(())
        }
        _ => Err("BAD_MODE"),
    }
}
