//! Minimal Tauri shell entry (commit scaffold). Product modules land in later commits.
#![allow(dead_code)]

pub mod error;
pub mod single_instance;

use tauri::Manager;

use error::AppError;
use single_instance::{
    try_acquire, SingleInstanceError, EXIT_ALREADY_RUNNING, EXIT_LOCK_FAILED,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let outcome = tauri::Builder::default()
        .setup(|app| {
            let app_data = app
                .path()
                .app_data_dir()
                .map_err(|_| AppError::Internal)?;
            std::fs::create_dir_all(&app_data).map_err(|_| AppError::Internal)?;

            match try_acquire(&app_data.join("binexus-desktop.lock")) {
                Ok(lock) => {
                    app.manage(lock);
                }
                Err(SingleInstanceError::AlreadyRunning) => {
                    std::process::exit(EXIT_ALREADY_RUNNING);
                }
                Err(SingleInstanceError::Failed) => {
                    std::process::exit(EXIT_LOCK_FAILED);
                }
            }

            Ok(())
        })
        .run(tauri::generate_context!());

    match outcome {
        Ok(()) => {}
        Err(_) => {
            std::process::exit(EXIT_LOCK_FAILED);
        }
    }
}
