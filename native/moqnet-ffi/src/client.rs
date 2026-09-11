use std::os::raw::{c_char, c_void};
use std::sync::Mutex;

use url::Url;

use crate::error::{closed, map_connect, map_err};
use crate::ffi::{
    as_mut, as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, read_cstr_opt, to_cstring, write_out, MOQ_ERR,
    MOQ_OK,
};
use crate::origin::{resolve_pair, OriginProducer};
use crate::runtime;

pub struct Client {
    config: moq_native::ClientConfig,
    publish: Option<OriginProducer>,
    consume: Option<OriginProducer>,
    cancelled: bool,
}

pub struct Session {
    inner: Option<moq_net::Session>,
    publisher: OriginProducer,
    consumer: crate::origin::OriginConsumer,
}

impl Session {
    pub(crate) fn new(
        session: moq_net::Session,
        publish: moq_net::origin::Producer,
        subscribe: moq_net::origin::Producer,
    ) -> Self {
        Self {
            inner: Some(session),
            publisher: OriginProducer::from_inner(publish),
            consumer: crate::origin::OriginConsumer::from_inner(subscribe.consume()),
        }
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        let _guard = runtime::enter();
        if let Some(session) = self.inner.take() {
            session.abort(moq_net::Error::Cancel);
        }
    }
}

fn split_list(raw: &str) -> Vec<String> {
    raw.split(';')
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(str::to_string)
        .collect()
}

#[no_mangle]
pub extern "C" fn moq_client_new() -> *mut c_void {
    clear_error();
    let _guard = runtime::enter();
    into_raw(Mutex::new(Client {
        config: moq_native::ClientConfig::default(),
        publish: None,
        consume: None,
        cancelled: false,
    }))
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_free(handle: *mut c_void) {
    drop_raw::<Mutex<Client>>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_disable_verify(handle: *mut c_void, disable: u8) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.disable_verify = Some(disable != 0);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_roots(handle: *mut c_void, paths: *const c_char) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let paths = match read_cstr_opt(paths) {
        Ok(v) => v.unwrap_or(""),
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.root = split_list(paths).into_iter().map(Into::into).collect();
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_system_roots(handle: *mut c_void, system_roots: u8) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.system_roots = Some(system_roots != 0);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_fingerprints(handle: *mut c_void, fingerprints: *const c_char) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let fingerprints = match read_cstr_opt(fingerprints) {
        Ok(v) => v.unwrap_or(""),
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.fingerprint = split_list(fingerprints);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_cert(handle: *mut c_void, path: *const c_char) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let path = match read_cstr_opt(path) {
        Ok(v) => v.map(str::to_string),
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.cert = path.map(Into::into);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_tls_key(handle: *mut c_void, path: *const c_char) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let path = match read_cstr_opt(path) {
        Ok(v) => v.map(str::to_string),
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.tls.key = path.map(Into::into);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_bind(handle: *mut c_void, addr: *const c_char) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let addr = match read_cstr(addr) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let parsed: std::net::SocketAddr = match addr.parse() {
        Ok(v) => v,
        Err(err) => return fail(format!("invalid bind address: {err}")),
    };
    match client.lock() {
        Ok(mut state) => {
            state.config.bind = parsed;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_publish(handle: *mut c_void, origin: *mut c_void) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let origin = if origin.is_null() {
        None
    } else {
        match as_ref::<OriginProducer>(origin) {
            Ok(o) => Some(o.clone()),
            Err(e) => return e,
        }
    };
    match client.lock() {
        Ok(mut state) => {
            state.publish = origin;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_set_consume(handle: *mut c_void, origin: *mut c_void) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let origin = if origin.is_null() {
        None
    } else {
        match as_ref::<OriginProducer>(origin) {
            Ok(o) => Some(o.clone()),
            Err(e) => return e,
        }
    };
    match client.lock() {
        Ok(mut state) => {
            state.consume = origin;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_connect(
    handle: *mut c_void,
    url: *const c_char,
    out_session: *mut *mut c_void,
) -> i32 {
    clear_error();
    let url = match read_cstr(url) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let parsed = match Url::parse(&url) {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };

    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };

    let (config, publish, consume, cancelled) = match client.lock() {
        Ok(state) => (
            state.config.clone(),
            state.publish.clone(),
            state.consume.clone(),
            state.cancelled,
        ),
        Err(_) => return fail("lock poisoned"),
    };
    if cancelled {
        return fail("cancelled");
    }

    let result = runtime::block_on(async move {
        let native = config.init().map_err(map_connect)?;
        let (publish, subscribe) = resolve_pair(publish.as_ref(), consume.as_ref());
        let session = native
            .with_publisher(&publish)
            .with_subscriber(subscribe.clone())
            .connect(parsed)
            .await
            .map_err(map_connect)?;
        Ok::<Session, i32>(Session::new(session, publish, subscribe))
    });

    match result {
        Ok(session) => write_out(out_session, into_raw(session)),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_client_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let client = match as_ref::<Mutex<Client>>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    match client.lock() {
        Ok(mut state) => {
            state.cancelled = true;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_free(handle: *mut c_void) {
    drop_raw::<Session>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_publisher(handle: *const c_void, out_origin: *mut *mut c_void) -> i32 {
    clear_error();
    let session = match as_ref::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    write_out(out_origin, into_raw(session.publisher.clone()))
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_consumer(handle: *const c_void, out_origin: *mut *mut c_void) -> i32 {
    clear_error();
    let session = match as_ref::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    write_out(out_origin, into_raw(session.consumer.clone()))
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_shutdown(handle: *mut c_void) -> i32 {
    moq_session_cancel(handle, 0)
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_cancel(handle: *mut c_void, code: u32) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let session = match as_mut::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    if let Some(inner) = &session.inner {
        inner.abort(moq_net::Error::Remote(code));
    }
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_stats_json(handle: *const c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let session = match as_ref::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let stats = session
        .inner
        .as_ref()
        .map(moq_net::Session::stats)
        .unwrap_or_default();
    let json = serde_json::json!({
        "rtt_us": stats.rtt.map(|d| d.as_micros() as u64),
        "send_rate_bps": stats.estimated_send_rate,
        "recv_rate_bps": stats.estimated_recv_rate,
        "bytes_sent": stats.bytes_sent,
        "bytes_received": stats.bytes_received,
        "bytes_lost": stats.bytes_lost,
        "packets_sent": stats.packets_sent,
        "packets_received": stats.packets_received,
        "packets_lost": stats.packets_lost,
    });
    match to_cstring(json.to_string()) {
        Ok(ptr) => write_out(out_json, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_closed(handle: *mut c_void) -> i32 {
    clear_error();
    let session = match as_ref::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let Some(inner) = session.inner.clone() else {
        return closed();
    };
    match runtime::block_on(inner.closed()) {
        err => {
            set_from_closed(err);
            MOQ_ERR
        }
    }
}

fn set_from_closed(err: moq_net::Error) {
    crate::ffi::set_error(err.to_string());
}
