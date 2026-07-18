#![allow(dead_code)]
#![allow(clippy::too_many_arguments)]

pub mod commands;
pub mod config;
pub mod crypto;
pub mod error;
pub mod secrets;
pub mod single_instance;
pub mod state;

use tauri::Manager;

use commands::AppContext;
use error::AppError;
use single_instance::{try_acquire, SingleInstanceError, EXIT_ALREADY_RUNNING, EXIT_LOCK_FAILED};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let outcome = tauri::Builder::default()
        .setup(|app| {
            let app_data = app.path().app_data_dir().map_err(|_| AppError::Internal)?;
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

            let ctx = AppContext::new(app_data)?;
            app.manage(ctx);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_app_state,
            commands::initialize_device,
            commands::retire_device,
        ])
        .run(tauri::generate_context!());

    match outcome {
        Ok(()) => {}
        Err(_) => {
            std::process::exit(EXIT_LOCK_FAILED);
        }
    }
}
