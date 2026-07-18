use std::fs::{File, OpenOptions};
use std::io::{ErrorKind, Write};
use std::path::Path;

use fs2::FileExt;

/// Clean exit when another instance already holds the lock. Not a crash.
pub const EXIT_ALREADY_RUNNING: i32 = 0;

/// Boot failure acquiring or creating the lock (permissions, I/O, path).
pub const EXIT_LOCK_FAILED: i32 = 1;

#[derive(Debug)]
pub enum SingleInstanceError {
    /// Lock is held by another live process.
    AlreadyRunning,
    /// Real filesystem / permission failure — not duplicate-instance.
    Failed,
}

/// Process-lifetime exclusive lock. Dropping releases the OS lock (tests + crash recovery).
pub struct InstanceLock {
    _file: File,
}

impl std::fmt::Debug for InstanceLock {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("InstanceLock")
    }
}

/// Try to acquire an exclusive lock at `lock_path`.
///
/// - Contended lock (`WouldBlock` / Windows lock-violation) → [`SingleInstanceError::AlreadyRunning`]
/// - Create/open/IO failures → [`SingleInstanceError::Failed`]
pub fn try_acquire(lock_path: &Path) -> Result<InstanceLock, SingleInstanceError> {
    if let Some(parent) = lock_path.parent() {
        std::fs::create_dir_all(parent).map_err(|_| SingleInstanceError::Failed)?;
    }
    let mut file = OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .open(lock_path)
        .map_err(|_| SingleInstanceError::Failed)?;
    match file.try_lock_exclusive() {
        Ok(()) => {
            let _ = file.set_len(0);
            let _ = writeln!(file, "pid={}", std::process::id());
            Ok(InstanceLock { _file: file })
        }
        Err(error) if is_lock_contention(&error) => Err(SingleInstanceError::AlreadyRunning),
        Err(_) => Err(SingleInstanceError::Failed),
    }
}

fn is_lock_contention(error: &std::io::Error) -> bool {
    if error.kind() == ErrorKind::WouldBlock {
        return true;
    }
    // Windows: ERROR_SHARING_VIOLATION (32), ERROR_LOCK_VIOLATION (33)
    matches!(error.raw_os_error(), Some(32) | Some(33))
}

/// Safe operator-facing line for real lock boot failures (no paths, no OS dumps).
pub fn lock_failed_message() -> &'static str {
    "Binexus could not start because local storage is unavailable."
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::thread;
    use std::time::Duration;
    use tempfile::tempdir;

    #[test]
    fn first_instance_obtains_lock() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let lock = try_acquire(&path);
        assert!(lock.is_ok());
    }

    #[test]
    fn second_instance_detects_occupied_lock() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let _first = try_acquire(&path).expect("first");
        match try_acquire(&path) {
            Err(SingleInstanceError::AlreadyRunning) => {}
            other => panic!("expected AlreadyRunning, got {other:?}"),
        }
    }

    #[test]
    fn second_acquire_returns_already_running_not_failed() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let _first = try_acquire(&path).unwrap();
        assert!(matches!(
            try_acquire(&path),
            Err(SingleInstanceError::AlreadyRunning)
        ));
    }

    #[test]
    fn first_continues_while_second_is_rejected() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let first = try_acquire(&path).unwrap();
        assert!(matches!(
            try_acquire(&path),
            Err(SingleInstanceError::AlreadyRunning)
        ));
        // First guard still held — third attempt still AlreadyRunning.
        assert!(matches!(
            try_acquire(&path),
            Err(SingleInstanceError::AlreadyRunning)
        ));
        drop(first);
    }

    #[test]
    fn after_first_drops_another_can_acquire() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let first = try_acquire(&path).unwrap();
        drop(first);
        assert!(try_acquire(&path).is_ok());
    }

    #[test]
    fn filesystem_error_is_failed_not_already_running() {
        let dir = tempdir().unwrap();
        // Parent path is a file → create_dir_all / open fails as Failed.
        let blocker = dir.path().join("not-a-dir");
        fs::write(&blocker, b"x").unwrap();
        let path = blocker.join("child").join("lock");
        match try_acquire(&path) {
            Err(SingleInstanceError::Failed) => {}
            other => panic!("expected Failed, got {other:?}"),
        }
    }

    #[test]
    fn lock_releases_after_abrupt_drop_of_holder() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        {
            let _holder = try_acquire(&path).unwrap();
            // Simulate process crash: drop without orderly shutdown.
        }
        thread::sleep(Duration::from_millis(20));
        assert!(
            try_acquire(&path).is_ok(),
            "lock must be reusable after holder drop (crash recovery)"
        );
    }
}
