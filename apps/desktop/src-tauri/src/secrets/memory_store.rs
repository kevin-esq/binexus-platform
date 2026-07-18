use parking_lot::Mutex;

use super::{SecretEnvelopeV1, SecretStore};
use crate::error::AppResult;

#[derive(Default)]
pub struct InMemorySecretStore(Mutex<Option<SecretEnvelopeV1>>);

impl SecretStore for InMemorySecretStore {
    fn get(&self) -> AppResult<Option<SecretEnvelopeV1>> {
        Ok(self.0.lock().clone())
    }
    fn set(&self, envelope: &SecretEnvelopeV1) -> AppResult<()> {
        *self.0.lock() = Some(envelope.clone());
        Ok(())
    }
    fn delete(&self) -> AppResult<()> {
        *self.0.lock() = None;
        Ok(())
    }
}
