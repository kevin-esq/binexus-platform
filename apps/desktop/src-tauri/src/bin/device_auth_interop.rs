//! CI harness for the Rust product DAT lifecycle. Output is JSON lines and never contains a DAT.

use std::env;
use std::path::PathBuf;
use std::sync::Arc;

use binexus_desktop_lib::branch::BranchClient;
use binexus_desktop_lib::config::ConfigStore;
use binexus_desktop_lib::device_auth::{DeviceAuthIdentity, DeviceAuthSession};
use binexus_desktop_lib::error::AppError;
use binexus_desktop_lib::secrets::{FileSecretStore, SecretStore};
use tokio::io::{AsyncBufReadExt, BufReader};

#[tokio::main]
async fn main() {
    if let Err(code) = run().await {
        println!(r#"{{"event":"error","code":"{code}"}}"#);
        std::process::exit(1);
    }
}

async fn run() -> Result<(), &'static str> {
    let base_url = env::var("BINEXUS_BRANCH_BASE_URL").map_err(|_| "MISSING_BASE_URL")?;
    let mode = env::var("BINEXUS_MODE").unwrap_or_else(|_| "device-auth".into());
    if !matches!(
        mode.as_str(),
        "device-auth" | "device-auth-full" | "device-auth-revoke-probe"
    ) {
        return Err("BAD_MODE");
    }
    let data_dir = env::var("BINEXUS_DATA_DIR")
        .map(PathBuf::from)
        .map_err(|_| "MISSING_DATA_DIR")?;
    let secrets: Arc<dyn SecretStore> =
        Arc::new(FileSecretStore::new(data_dir.join("harness-envelope.json")));
    let envelope = secrets
        .get()
        .map_err(|_| "SECRETS")?
        .ok_or("CREDENTIALS_MISSING")?;
    let config = ConfigStore::new(data_dir)
        .load()
        .map_err(|_| "CONFIG")?
        .ok_or("CONFIG")?;
    let identity = DeviceAuthIdentity {
        branch_instance_id: config.branch_instance_id.ok_or("CONFIG")?,
        device_id: config.device_id.ok_or("CONFIG")?,
        terminal_id: config.terminal_id,
        branch_base_url: config.branch_base_url.unwrap_or_else(|| base_url.clone()),
    };
    if identity.device_id != envelope.device_id {
        return Err("IDENTITY");
    }
    let client = BranchClient::new(
        identity
            .branch_base_url
            .parse()
            .map_err(|_| "BAD_BASE_URL")?,
    )
    .map_err(|_| "CLIENT")?;
    let session = DeviceAuthSession::new(secrets);

    println!(r#"{{"event":"ready","deviceId":"{}"}}"#, envelope.device_id);
    let authorization = session
        .ensure_access_token(&client, &identity)
        .await
        .map_err(|_| "DEVICE_AUTH")?;
    println!(r#"{{"event":"issued"}}"#);

    let me = client
        .device_auth_me(authorization.as_str())
        .await
        .map_err(|_| "ME")?;
    println!(
        r#"{{"event":"me","deviceId":"{}","terminalId":"{}","branchInstanceId":"{}"}}"#,
        me.device_id, me.terminal_id, me.branch_instance_id
    );

    if mode == "device-auth-full" {
        let status = client
            .device_auth_operational_status(
                authorization.as_str(),
                env::var("BINEXUS_USER_JWT").ok().as_deref(),
            )
            .await
            .map_err(|_| "OPERATIONAL")?;
        println!(r#"{{"event":"operational","status":{status}}}"#);
    }

    if mode == "device-auth-revoke-probe" {
        let mut line = String::new();
        let bytes = BufReader::new(tokio::io::stdin())
            .read_line(&mut line)
            .await
            .map_err(|_| "REVOKE_SIGNAL")?;
        if bytes == 0 || line.trim() != "REVOKE_DONE" {
            return Err("REVOKE_SIGNAL");
        }

        match client.device_auth_me(authorization.as_str()).await {
            Err(AppError::DeviceRevoked) => {
                println!(r#"{{"event":"post_revoke","status":403,"code":"DEVICE_REVOKED"}}"#);
            }
            Ok(_) => return Err("REVOKE_NOT_ENFORCED"),
            Err(_) => return Err("REVOKE_PROBE"),
        }
    }

    Ok(())
}
