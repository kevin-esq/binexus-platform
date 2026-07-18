use super::SecretEnvelopeV1;
use crate::error::AppResult;

pub trait SecretStore: Send + Sync {
    fn get(&self) -> AppResult<Option<SecretEnvelopeV1>>;
    fn set(&self, envelope: &SecretEnvelopeV1) -> AppResult<()>;
    fn delete(&self) -> AppResult<()>;
}
