use super::DesktopConfig;
use crate::error::{AppError, AppResult};
use fs2::FileExt;
use std::{
    fs::{self, File, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
};

pub struct ConfigStore {
    path: PathBuf,
}

impl ConfigStore {
    pub fn new(app_data_dir: impl Into<PathBuf>) -> Self {
        let app_data_dir = app_data_dir.into();
        Self {
            path: app_data_dir.join("config.json"),
        }
    }
    #[cfg(test)]
    pub fn at(path: PathBuf) -> Self {
        Self { path }
    }
    pub fn load(&self) -> AppResult<Option<DesktopConfig>> {
        match fs::read_to_string(&self.path) {
            Ok(raw) => serde_json::from_str(&raw)
                .map(Some)
                .map_err(|_| AppError::Configuration),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(AppError::Configuration),
        }
    }
    pub fn save(&self, config: &DesktopConfig) -> AppResult<()> {
        let parent = self.path.parent().ok_or(AppError::Configuration)?;
        fs::create_dir_all(parent).map_err(|_| AppError::Configuration)?;
        let lock = lock_file(parent)?;
        lock.lock_exclusive().map_err(|_| AppError::Configuration)?;
        let result = self.save_locked(config);
        let _ = lock.unlock();
        result
    }
    fn save_locked(&self, config: &DesktopConfig) -> AppResult<()> {
        let bytes = serde_json::to_vec_pretty(config).map_err(|_| AppError::Configuration)?;
        let tmp = self.path.with_extension("json.tmp");
        let backup = self.path.with_extension("json.bak");
        let mut file = File::create(&tmp).map_err(|_| AppError::Configuration)?;
        file.write_all(&bytes)
            .and_then(|_| file.sync_all())
            .map_err(|_| AppError::Configuration)?;
        if self.path.exists() {
            fs::copy(&self.path, &backup).map_err(|_| AppError::Configuration)?;
        }
        fs::rename(tmp, &self.path).map_err(|_| AppError::Configuration)
    }
}

fn lock_file(parent: &Path) -> AppResult<File> {
    OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .open(parent.join("config.lock"))
        .map_err(|_| AppError::Configuration)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Barrier};
    use std::thread;

    #[test]
    fn atomically_writes_and_loads_config() {
        let dir = tempfile::tempdir().unwrap();
        let store = ConfigStore::at(dir.path().join("config.json"));
        let config = DesktopConfig::default();
        store.save(&config).unwrap();
        assert_eq!(store.load().unwrap(), Some(config));
        assert!(!dir.path().join("config.json.tmp").exists());
    }

    #[test]
    fn leaves_bak_after_overwrite() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("config.json");
        let store = ConfigStore::at(path.clone());
        let first = DesktopConfig {
            schema_version: 1,
            ..DesktopConfig::default()
        };
        store.save(&first).unwrap();
        let second = DesktopConfig {
            schema_version: 2,
            ..first
        };
        store.save(&second).unwrap();
        assert!(path.with_extension("json.bak").exists());
        assert_eq!(store.load().unwrap().unwrap().schema_version, 2);
    }

    #[test]
    fn two_writers_serialize_via_lock() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("config.json");
        let store = Arc::new(ConfigStore::at(path));
        let barrier = Arc::new(Barrier::new(2));
        let mut handles = Vec::new();
        for i in 0..2 {
            let store = Arc::clone(&store);
            let barrier = Arc::clone(&barrier);
            handles.push(thread::spawn(move || {
                barrier.wait();
                let config = DesktopConfig {
                    schema_version: 10 + i,
                    ..DesktopConfig::default()
                };
                store.save(&config).unwrap();
            }));
        }
        for handle in handles {
            handle.join().unwrap();
        }
        let loaded = store.load().unwrap().unwrap();
        assert!(loaded.schema_version == 10 || loaded.schema_version == 11);
    }
}
