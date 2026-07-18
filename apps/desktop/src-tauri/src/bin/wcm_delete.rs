//! Clears the product WCM envelope used by KeyringSecretStore (PR5 GUI smoke / scenario E prep).
use binexus_desktop_lib::secrets::{KeyringSecretStore, SecretStore};

fn main() {
    let store = KeyringSecretStore::new();
    match store.delete() {
        Ok(()) => println!("WCM_DELETE_OK"),
        Err(error) => {
            eprintln!("WCM_DELETE_ERR {}", error.code());
            std::process::exit(1);
        }
    }
}
