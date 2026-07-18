//! PR5.0 secure storage spike — keyring (WCM) vs DPAPI.
//! Run: cargo run -p secure-storage-spike

use std::process::{Command, Stdio};
use std::time::Instant;

use base64::{engine::general_purpose::STANDARD as B64, Engine as _};
use keyring::Entry;
use rand::RngCore;
use serde::Serialize;

const SERVICE: &str = "io.binexus.desktop.spike";

#[derive(Debug, Serialize)]
struct SpikeReport {
    provider: &'static str,
    scenario: &'static str,
    ok: bool,
    detail: String,
    elapsed_ms: u128,
}

#[derive(Debug, Serialize)]
struct EnvelopeV1 {
    schema_version: u32,
    device_id: String,
    private_key_pkcs8_base64: String,
    device_credential_base64url: String,
    pairing: PairingAttempt,
}

#[derive(Debug, Serialize)]
struct PairingAttempt {
    request_id: Option<String>,
    status_token: Option<String>,
    receipt: Option<String>,
}

fn main() {
    if std::env::args().nth(1).as_deref() == Some("child-store") {
        child_store_main();
        return;
    }

    let run_suffix = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let account = format!("pr5-spike-{run_suffix}");

    let envelope = sample_envelope();
    let json = serde_json::to_string(&envelope).expect("serialize envelope");
    let size = json.len();

    let mut reports = Vec::new();
    reports.push(SpikeReport {
        provider: "envelope",
        scenario: "serialize_v1",
        ok: true,
        detail: format!("bytes={size}"),
        elapsed_ms: 0,
    });

    reports.extend(run_keyring(&account, &json));
    #[cfg(windows)]
    reports.extend(run_dpapi(&account, &json));
    reports.extend(run_two_process_keyring(&account, &json));

    // cleanup
    let _ = Entry::new(SERVICE, &account).expect("entry").delete_credential();

    println!("{}", serde_json::to_string_pretty(&reports).expect("report json"));

    let failures = reports.iter().filter(|r| !r.ok).count();
    if failures > 0 {
        std::process::exit(1);
    }
}

fn sample_envelope() -> EnvelopeV1 {
    let mut pkcs8 = vec![0u8; 121];
    rand::thread_rng().fill_bytes(&mut pkcs8);
    let mut credential = [0u8; 32];
    rand::thread_rng().fill_bytes(&mut credential);
    EnvelopeV1 {
        schema_version: 1,
        device_id: "0197a1b0-c3d4-7890-abcd-ef1234567893".into(),
        private_key_pkcs8_base64: B64.encode(pkcs8),
        device_credential_base64url: base64_url(&credential),
        pairing: PairingAttempt {
            request_id: Some("0197a1b0-c3d4-7890-abcd-ef1234567894".into()),
            status_token: Some("status-token-spike-value-not-production".into()),
            receipt: Some("receipt-spike-value-not-production".into()),
        },
    }
}

fn base64_url(bytes: &[u8]) -> String {
    B64.encode(bytes)
        .replace('+', "-")
        .replace('/', "_")
        .trim_end_matches('=')
        .to_string()
}

fn run_keyring(account: &str, payload: &str) -> Vec<SpikeReport> {
    let mut out = Vec::new();
    let entry = Entry::new(SERVICE, account).expect("entry");

    out.push(timed("keyring", "create", || {
        entry
            .set_password(payload)
            .map_err(|e| e.to_string())
            .map(|_| "stored".into())
    }));

    out.push(timed("keyring", "read", || {
        entry
            .get_password()
            .map(|v| format!("len={}", v.len()))
            .map_err(|e| e.to_string())
    }));

    out.push(timed("keyring", "overwrite", || {
        entry
            .set_password(&format!("{payload}-v2"))
            .and_then(|_| entry.get_password())
            .map(|v| format!("len={}", v.len()))
            .map_err(|e| e.to_string())
    }));

    out.push(timed("keyring", "delete", || {
        entry
            .delete_credential()
            .map_err(|e| e.to_string())
            .map(|_| "deleted".into())
    }));

    out.push(timed("keyring", "missing_entry", || {
        match entry.get_password() {
            Err(keyring::Error::NoEntry) => Ok("NoEntry".into()),
            Err(e) => Err(e.to_string()),
            Ok(v) => Err(format!("unexpected value len={}", v.len())),
        }
    }));

    out
}

#[cfg(windows)]
fn run_dpapi(_account: &str, payload: &str) -> Vec<SpikeReport> {
    use windows::Win32::Security::Cryptography::{
        CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB, CRYPTPROTECT_UI_FORBIDDEN,
    };

    let mut out = Vec::new();
    let description: Vec<u16> = "binexus-pr5-spike"
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();

    out.push(timed("dpapi", "protect_unprotect", || {
        let input = payload.as_bytes();
        let in_blob = CRYPT_INTEGER_BLOB {
            cbData: input.len() as u32,
            pbData: input.as_ptr() as *mut u8,
        };
        let mut out_blob = CRYPT_INTEGER_BLOB::default();
        unsafe {
            CryptProtectData(
                &in_blob,
                windows::core::PCWSTR(description.as_ptr()),
                None,
                None,
                None,
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out_blob,
            )
            .map_err(|e| e.to_string())?;

            let mut plain = CRYPT_INTEGER_BLOB::default();
            CryptUnprotectData(
                &out_blob,
                None,
                None,
                None,
                None,
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut plain,
            )
            .map_err(|e| e.to_string())?;

            let slice = std::slice::from_raw_parts(plain.pbData, plain.cbData as usize);
            let text = String::from_utf8(slice.to_vec()).map_err(|e| e.to_string())?;
            if text != payload {
                return Err("roundtrip mismatch".into());
            }
            Ok(format!("cipher_bytes={}", out_blob.cbData))
        }
    }));

    out
}

fn run_two_process_keyring(account: &str, payload: &str) -> Vec<SpikeReport> {
    let exe = std::env::current_exe().expect("current exe");
    let mut out = Vec::new();

    out.push(timed("keyring", "two_process_child_store", || {
        let status = Command::new(&exe)
            .arg("child-store")
            .arg(account)
            .arg(payload)
            .stdout(Stdio::null())
            .status()
            .map_err(|e| e.to_string())?;
        if !status.success() {
            return Err(format!("child exit={status}"));
        }
        std::thread::sleep(std::time::Duration::from_millis(200));
        Ok("child stored".into())
    }));

    let entry = Entry::new(SERVICE, account).expect("entry");
    out.push(timed("keyring", "two_process_parent_read", || {
        entry
            .get_password()
            .map(|v| format!("len={}", v.len()))
            .map_err(|e| e.to_string())
    }));

    out
}

fn child_store_main() {
    let account = std::env::args().nth(2).expect("account");
    let payload = std::env::args().nth(3).expect("payload");
    let entry = Entry::new(SERVICE, &account).expect("entry");
    entry.set_password(&payload).expect("child store");
    let roundtrip = entry.get_password().expect("child read");
    assert_eq!(roundtrip, payload, "child roundtrip mismatch");
}

fn timed(
    provider: &'static str,
    scenario: &'static str,
    f: impl FnOnce() -> Result<String, String>,
) -> SpikeReport {
    let start = Instant::now();
    match f() {
        Ok(detail) => SpikeReport {
            provider,
            scenario,
            ok: true,
            detail,
            elapsed_ms: start.elapsed().as_millis(),
        },
        Err(detail) => SpikeReport {
            provider,
            scenario,
            ok: false,
            detail,
            elapsed_ms: start.elapsed().as_millis(),
        },
    }
}
