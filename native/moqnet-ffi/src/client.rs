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
    config: moq_tokio::connect::Config,
    publish: Option<OriginProducer>,
    consume: Option<OriginProducer>,
    cancelled: bool,
}

#[derive(Clone)]
enum SessionInner {
    Connection(moq_tokio::Connection),
    Session(moq_net::Session),
}

pub struct Session {
    inner: Option<SessionInner>,
    publisher: OriginProducer,
    consumer: crate::origin::OriginConsumer,
}

impl Session {
    pub(crate) fn connected(
        connection: moq_tokio::Connection,
        publish: moq_net::origin::Producer,
        subscribe: moq_net::origin::Producer,
    ) -> Self {
        Self {
            inner: Some(SessionInner::Connection(connection)),
            publisher: OriginProducer::from_inner(publish),
            consumer: crate::origin::OriginConsumer::from_inner(subscribe.consume()),
        }
    }

    pub(crate) fn accepted(
        session: moq_net::Session,
        publish: moq_net::origin::Producer,
        subscribe: moq_net::origin::Producer,
    ) -> Self {
        Self {
            inner: Some(SessionInner::Session(session)),
            publisher: OriginProducer::from_inner(publish),
            consumer: crate::origin::OriginConsumer::from_inner(subscribe.consume()),
        }
    }

    fn abort(&self, err: moq_net::Error) {
        match &self.inner {
            Some(SessionInner::Connection(connection)) => connection.abort(err),
            Some(SessionInner::Session(session)) => session.abort(err),
            None => {}
        }
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        let _guard = runtime::enter();
        if let Some(inner) = self.inner.take() {
            match inner {
                SessionInner::Connection(connection) => connection.abort(moq_net::Error::Cancel),
                SessionInner::Session(session) => session.abort(moq_net::Error::Cancel),
            }
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

fn default_client_config() -> moq_tokio::connect::Config {
    let mut config = moq_tokio::connect::Config::default();
    // Preserve the previous one-shot connect: ClosedAsync returns when this
    // transport ends, rather than waiting out the reconnect loop.
    config.once = Some(true);
    config
}

#[no_mangle]
pub extern "C" fn moq_client_new() -> *mut c_void {
    clear_error();
    let _guard = runtime::enter();
    into_raw(Mutex::new(Client {
        config: default_client_config(),
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
            state.config.tls.insecure = Some(disable != 0);
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
            state.config.bind = Some(parsed);
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
        let native = config
            .init(moq_tokio::quic::Config::default())
            .map_err(map_connect)?;
        let (publish, subscribe) = resolve_pair(publish.as_ref(), consume.as_ref());
        let connection = native
            .with_publisher(&publish)
            .with_subscriber(subscribe.clone())
            .connect(parsed)
            .established()
            .await
            .map_err(map_connect)?;
        Ok::<Session, i32>(Session::connected(connection, publish, subscribe))
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
    session.abort(moq_net::SessionError::from_code(code).into());
    MOQ_OK
}

fn stats_json(stats: moq_net::session::Stats) -> serde_json::Value {
    serde_json::json!({
        "rtt_us": stats.rtt.map(|d| d.as_micros() as u64),
        "send_rate_bps": stats.estimated_send_rate.map(moq_net::bandwidth::Rate::as_bps),
        "recv_rate_bps": stats.estimated_recv_rate.map(moq_net::bandwidth::Rate::as_bps),
        "bytes_sent": stats.bytes_sent,
        "bytes_received": stats.bytes_received,
        "bytes_lost": stats.bytes_lost,
        "packets_sent": stats.packets_sent,
        "packets_received": stats.packets_received,
        "packets_lost": stats.packets_lost,
    })
}

#[no_mangle]
pub unsafe extern "C" fn moq_session_stats_json(handle: *const c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let session = match as_ref::<Session>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let stats = match &session.inner {
        Some(SessionInner::Connection(connection)) => connection.monitor().stats().unwrap_or_default(),
        Some(SessionInner::Session(session)) => session.stats(),
        None => moq_net::session::Stats::default(),
    };
    match to_cstring(stats_json(stats).to_string()) {
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
    match session.inner.clone() {
        Some(SessionInner::Connection(connection)) => match runtime::block_on(connection.closed()) {
            Ok(()) => closed(),
            Err(err) => fail(err.to_string()),
        },
        Some(SessionInner::Session(inner)) => {
            set_from_closed(runtime::block_on(inner.closed()));
            MOQ_ERR
        }
        None => closed(),
    }
}

fn set_from_closed(err: moq_net::Error) {
    crate::ffi::set_error(err.to_string());
}
