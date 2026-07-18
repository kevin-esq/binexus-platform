use keyring::{Entry, Error};

use super::{SecretEnvelopeV1, SecretStore};
use crate::error::{AppError, AppResult};

const SERVICE: &str = "io.binexus.desktop";
const ACCOUNT: &str = "device-secret-envelope-v1";

#[derive(Default)]
pub struct KeyringSecretStore;

impl KeyringSecretStore {
    pub fn new() -> Self {
        Self
    }

    fn entry(&self) -> AppResult<Entry> {
        Entry::new(SERVICE, ACCOUNT).map_err(|_| AppError::SecretStore)
    }
}

impl SecretStore for KeyringSecretStore {
    fn get(&self) -> AppResult<Option<SecretEnvelopeV1>> {
        match self.entry()?.get_password() {
            Ok(value) => serde_json::from_str(&value)
                .map(Some)
                .map_err(|_| AppError::SecretStore),
            Err(Error::NoEntry) => Ok(None),
            Err(_) => Err(AppError::SecretStore),
        }
    }

    fn set(&self, envelope: &SecretEnvelopeV1) -> AppResult<()> {
        let value = serde_json::to_string(envelope).map_err(|_| AppError::SecretStore)?;
        self.entry()?
            .set_password(&value)
            .map_err(|_| AppError::SecretStore)
    }

    fn delete(&self) -> AppResult<()> {
        match self.entry()?.delete_credential() {
            Ok(()) | Err(Error::NoEntry) => Ok(()),
            Err(_) => Err(AppError::SecretStore),
        }
    }
}
