use crate::ffi::{fail, MOQ_ERR};

pub fn map_err(err: impl std::fmt::Display) -> i32 {
    fail(err.to_string())
}

pub fn map_connect(err: moq_tokio::Error) -> i32 {
    match err.connect_error() {
        Some(moq_tokio::ConnectError::Unauthorized) => fail("unauthorized"),
        Some(moq_tokio::ConnectError::Forbidden) => fail("forbidden"),
        _ => fail(format!("connect: {err}")),
    }
}

pub fn closed() -> i32 {
    fail("closed")
}

#[allow(dead_code)]
pub fn cancelled() -> i32 {
    fail("cancelled")
}

#[allow(dead_code)]
pub fn not_supported(feature: &str) -> i32 {
    fail(format!("built without {feature} feature"))
}

#[allow(dead_code)]
pub fn ok_or_none<T>(value: Option<T>) -> Result<T, i32> {
    value.ok_or(MOQ_ERR)
}
