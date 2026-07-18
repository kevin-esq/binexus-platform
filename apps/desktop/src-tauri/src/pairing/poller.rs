use parking_lot::Mutex;
use tokio::task::JoinHandle;

#[derive(Default)]
pub struct PairingPoller {
    task: Mutex<Option<JoinHandle<()>>>,
}

impl PairingPoller {
    pub fn replace(&self, task: JoinHandle<()>) {
        if let Some(previous) = self.task.lock().replace(task) {
            previous.abort();
        }
    }
    pub fn cancel(&self) {
        if let Some(task) = self.task.lock().take() {
            task.abort();
        }
    }
}
