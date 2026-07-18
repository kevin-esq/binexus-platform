//! Single-instance spike: file lock authority; plugin deferred to product shell.
use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::process::Command;
use std::time::{Duration, Instant};

use fs2::FileExt;
use serde::Serialize;

#[derive(Serialize)]
struct Step {
    scenario: &'static str,
    ok: bool,
    detail: String,
}

fn main() {
    if std::env::args().nth(1).as_deref() == Some("hold") {
        hold_lock();
        return;
    }

    let lock_path = lock_file_path();
    let mut steps = Vec::new();

    steps.push(acquire_lock(&lock_path, "first_acquire"));
    steps.push(release_lock(&lock_path, "release"));

    let _ = std::fs::remove_file(&lock_path);
    let _ = File::create(&lock_path);
    steps.push(acquire_lock(&lock_path, "recover_orphan_file"));

    let exe = std::env::current_exe().expect("exe");
    let mut child = Command::new(exe).arg("hold").spawn().expect("spawn child");
    std::thread::sleep(Duration::from_millis(300));
    steps.push(blocked_acquire(&lock_path, "second_process_blocked"));
    child.kill().ok();
    child.wait().ok();
    steps.push(acquire_lock(&lock_path, "acquire_after_child_exit"));

    println!("{}", serde_json::to_string_pretty(&steps).expect("json"));
    if steps.iter().any(|s| !s.ok) {
        std::process::exit(1);
    }
}

fn lock_file_path() -> PathBuf {
    let mut path = std::env::temp_dir();
    path.push("binexus-pr5-single-instance-spike.lock");
    path
}

fn acquire_lock(path: &PathBuf, scenario: &'static str) -> Step {
    match try_acquire(path) {
        Ok(_) => Step {
            scenario,
            ok: true,
            detail: "acquired".into(),
        },
        Err(e) => Step {
            scenario,
            ok: false,
            detail: e,
        },
    }
}

fn blocked_acquire(path: &PathBuf, scenario: &'static str) -> Step {
    match try_acquire(path) {
        Ok(_) => Step {
            scenario,
            ok: false,
            detail: "unexpected acquire".into(),
        },
        Err(e) => Step {
            scenario,
            ok: true,
            detail: e,
        },
    }
}

fn release_lock(path: &PathBuf, scenario: &'static str) -> Step {
    match OpenOptions::new().write(true).open(path) {
        Ok(file) => {
            let _ = file.unlock();
            Step {
                scenario,
                ok: true,
                detail: "released".into(),
            }
        }
        Err(e) => Step {
            scenario,
            ok: false,
            detail: e.to_string(),
        },
    }
}

fn try_acquire(path: &PathBuf) -> Result<File, String> {
    let mut file = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(false)
        .open(path)
        .map_err(|e| e.to_string())?;
    file.try_lock_exclusive().map_err(|e| e.to_string())?;
    file.write_all(format!("pid={}\n", std::process::id()).as_bytes())
        .map_err(|e| e.to_string())?;
    Ok(file)
}

fn hold_lock() {
    let path = lock_file_path();
    let _file = try_acquire(&path).expect("child acquire");
    let start = Instant::now();
    while start.elapsed() < Duration::from_secs(10) {
        std::thread::sleep(Duration::from_millis(100));
    }
}
