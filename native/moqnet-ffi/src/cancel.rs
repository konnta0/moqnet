use std::future::Future;

use tokio::sync::watch;

pub type Flag = watch::Sender<bool>;

pub fn new() -> Flag {
    watch::channel(false).0
}

pub fn set(flag: &Flag) {
    let _ = flag.send_replace(true);
}

pub fn is_set(flag: &Flag) -> bool {
    *flag.borrow()
}

pub async fn or_set<T>(flag: &Flag, fut: impl Future<Output = T>) -> Option<T> {
    if *flag.borrow() {
        return None;
    }

    let mut rx = flag.subscribe();
    tokio::select! {
        biased;
        _ = rx.changed() => None,
        value = fut => Some(value),
    }
}
