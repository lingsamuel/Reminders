use std::sync::{
    atomic::{AtomicBool, Ordering},
    Mutex,
};

/// 只同步最新窗口意图；排队的旧工作不能把较新的状态覆盖回去。
#[derive(Default)]
pub struct VisibilityCoordinator {
    desired: AtomicBool,
    gate: Mutex<()>,
}

impl VisibilityCoordinator {
    pub fn set(&self, visible: bool) {
        self.desired.store(visible, Ordering::SeqCst);
    }

    pub fn synchronize<T>(
        &self,
        apply: impl FnOnce(bool) -> Result<T, String>,
    ) -> Result<T, String> {
        let _guard = self.gate.lock().map_err(|_| "窗口状态同步失败")?;
        apply(self.desired.load(Ordering::SeqCst))
    }

    pub fn synchronize_or_recover(
        &self,
        mut apply: impl FnMut(bool) -> Result<(), String>,
        report_error: impl FnOnce(&str),
    ) -> Result<(), String> {
        self.synchronize(|latest| match apply(latest) {
            Ok(()) => Ok(()),
            Err(error) => {
                // 通信失败时，显示错误比收起窗口优先；恢复和补发也必须持有同一把锁。
                self.set(true);
                report_error(&error);
                let _ = apply(true);
                Err(error)
            }
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Barrier};

    #[test]
    fn a_late_old_worker_applies_latest_visibility() {
        let coordinator = Arc::new(VisibilityCoordinator::default());
        let barrier = Arc::new(Barrier::new(2));
        coordinator.set(false);
        let old = coordinator.clone();
        let wait = barrier.clone();
        let worker = std::thread::spawn(move || {
            wait.wait();
            old.synchronize(Ok).unwrap()
        });
        coordinator.set(true);
        assert!(coordinator.synchronize(Ok).unwrap());
        barrier.wait();
        assert!(worker.join().unwrap());
    }

    #[test]
    fn failure_does_not_poison_subsequent_sync() {
        let coordinator = VisibilityCoordinator::default();
        assert!(coordinator
            .synchronize::<()>(|_| Err("disconnected".into()))
            .is_err());
        coordinator.set(true);
        assert!(coordinator.synchronize(Ok).unwrap());
    }

    #[test]
    fn failed_hide_recovers_window_and_backend_in_the_same_transaction() {
        let coordinator = VisibilityCoordinator::default();
        let mut applied = Vec::new();
        let result = coordinator.synchronize_or_recover(
            |visible| {
                applied.push(visible);
                if visible {
                    Ok(())
                } else {
                    Err("timeout".into())
                }
            },
            |error| assert_eq!(error, "timeout"),
        );
        assert_eq!(result, Err("timeout".into()));
        assert_eq!(applied, vec![false, true]);
        assert!(coordinator.synchronize(Ok).unwrap());
    }
}
