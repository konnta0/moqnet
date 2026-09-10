use std::sync::LazyLock;

use tokio::runtime::{Handle, Runtime};

static RUNTIME: LazyLock<Runtime> = LazyLock::new(|| {
    tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .thread_name("moqnet-ffi")
        .build()
        .expect("failed to create moqnet-ffi runtime")
});

pub fn handle() -> Handle {
    RUNTIME.handle().clone()
}

pub fn enter() -> tokio::runtime::EnterGuard<'static> {
    RUNTIME.enter()
}

pub fn block_on<F, T>(future: F) -> T
where
    F: std::future::Future<Output = T> + Send,
    T: Send,
{
    if Handle::try_current().is_ok() {
        let handle = handle();
        std::thread::scope(|s| s.spawn(|| handle.block_on(future)).join().expect("worker panicked"))
    } else {
        RUNTIME.block_on(future)
    }
}
