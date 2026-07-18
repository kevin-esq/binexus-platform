use serde::Serialize;
use thiserror::Error;

#[derive(Debug, Clone, Serialize)]
pub struct PublicError {
    pub code: &'static str,
    pub message: String,
}

#[derive(Debug, Error)]
pub enum AppError {
    #[error("Configuration is invalid.")]
    Configuration,
    #[error("The Branch Server URL is not allowed.")]
    UrlPolicy,
    #[error("The Branch Server could not be reached.")]
    Network,
    #[error("Pairing requires recovery.")]
    RecoveryRequired,
    #[error("Paired credentials are unavailable.")]
    CredentialsUnavailable,
    #[error("Pairing cannot continue.")]
    Pairing,
    #[error("Another Binexus instance is already running.")]
    AlreadyRunning,
    #[error("Secure storage is unavailable.")]
    SecretStore,
    #[error("An internal operation failed.")]
    Internal,
}

impl AppError {
    pub fn code(&self) -> &'static str {
        match self {
            Self::Configuration => "CONFIGURATION_INVALID",
            Self::UrlPolicy => "BRANCH_URL_NOT_ALLOWED",
            Self::Network => "BRANCH_UNREACHABLE",
            Self::RecoveryRequired => "RECOVERY_REQUIRED",
            Self::CredentialsUnavailable => "PAIRED_CREDENTIALS_UNAVAILABLE",
            Self::Pairing => "PAIRING_FAILED",
            Self::AlreadyRunning => "ALREADY_RUNNING",
            Self::SecretStore => "SECURE_STORAGE_UNAVAILABLE",
            Self::Internal => "INTERNAL_ERROR",
        }
    }

    pub fn public(&self) -> PublicError {
        PublicError {
            code: self.code(),
            message: self.to_string(),
        }
    }
}

pub type AppResult<T> = Result<T, AppError>;
