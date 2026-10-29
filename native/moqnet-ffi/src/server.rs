use std::os::raw::{c_char, c_void};
use std::path::PathBuf;
use std::sync::Mutex;

use crate::cancel;
use crate::client::Session;
use crate::ffi::{as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, read_cstr_opt, to_cstring, write_out, MOQ_NONE, MOQ_OK};
use crate::origin::{resolve_pair, OriginProducer};
use crate::runtime;

struct ServerState {
    config: moq_tokio::listen::Config,
    publish: Option<OriginProducer>,
    consume: Option<OriginProducer>,
    listener: Option<moq_tokio::Listener>,
}

pub struct Server {
    state: Mutex<ServerState>,
    // Watch so Close wakes Accept without taking `state` (Accept holds that lock).
    closed: cancel::Flag,
}

pub struct Request {
    inner: Mutex<Option<moq_tokio::server::Request>>,
    publish: Mutex<Option<OriginProducer>>,
    consume: Mutex<Option<OriginProducer>>,
    transport: String,
    url: Option<String>,
    path: String,
    query: Option<String>,
}

fn split_list(raw: &str) -> Vec<String> {
    raw.split(';')
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(str::to_string)
        .collect()
}

async fn close_listener(listener: moq_tokio::Listener) {
    listener.close().await;
}

#[no_mangle]
pub extern "C" fn moq_server_new() -> *mut c_void {
    clear_error();
    let _guard = runtime::enter();
    into_raw(Server {
        state: Mutex::new(ServerState {
            config: moq_tokio::listen::Config::default(),
            publish: None,
            consume: None,
            listener: None,
        }),
        closed: cancel::new(),
    })
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_free(handle: *mut c_void) {
    drop_raw::<Server>(handle);
}

/// Stops the server: a pending or later accept() returns none (as when the listener ends)
/// and the listener is closed. Does not block: if accept() is waiting it holds the state
/// lock, so close only sets the flag and accept itself drops the listener when it wakes up.
#[no_mangle]
pub unsafe extern "C" fn moq_server_close(handle: *mut c_void) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    cancel::set(&server.closed);
    if let Ok(mut state) = server.state.try_lock() {
        if let Some(listener) = state.listener.take() {
            runtime::block_on(close_listener(listener));
        }
    }
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_bind(handle: *mut c_void, addr: *const c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let addr = match read_cstr(addr) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let bind: moq_tokio::listen::Bind = match addr.parse() {
        Ok(v) => v,
        Err(_) => {
            let port_ok = addr.rsplit_once(':').is_some_and(|(_, port)| port.parse::<u16>().is_ok());
            if !port_ok {
                return fail(format!("invalid bind address: {addr}"));
            }
            return fail(format!("invalid bind address: {addr}"));
        }
    };
    match server.state.lock() {
        Ok(mut state) => {
            state.config.bind = Some(bind);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_tls_cert(handle: *mut c_void, paths: *const c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let paths = match read_cstr_opt(paths) {
        Ok(v) => v.unwrap_or(""),
        Err(e) => return e,
    };
    match server.state.lock() {
        Ok(mut state) => {
            state.config.tls.cert = split_list(paths).into_iter().map(PathBuf::from).collect();
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_tls_key(handle: *mut c_void, paths: *const c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let paths = match read_cstr_opt(paths) {
        Ok(v) => v.unwrap_or(""),
        Err(e) => return e,
    };
    match server.state.lock() {
        Ok(mut state) => {
            state.config.tls.key = split_list(paths).into_iter().map(PathBuf::from).collect();
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_tls_generate(handle: *mut c_void, hostnames: *const c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let hostnames = match read_cstr_opt(hostnames) {
        Ok(v) => v.unwrap_or(""),
        Err(e) => return e,
    };
    match server.state.lock() {
        Ok(mut state) => {
            state.config.tls.generate = split_list(hostnames);
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_publish(handle: *mut c_void, origin: *mut c_void) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
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
    match server.state.lock() {
        Ok(mut state) => {
            state.publish = origin;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_set_consume(handle: *mut c_void, origin: *mut c_void) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
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
    match server.state.lock() {
        Ok(mut state) => {
            state.consume = origin;
            MOQ_OK
        }
        Err(_) => fail("lock poisoned"),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_listen(handle: *mut c_void, out_addr: *mut *mut c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    if cancel::is_set(&server.closed) {
        return fail("closed");
    }
    let mut state = match server.state.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    if state.listener.is_some() {
        return fail("already listening");
    }
    let config = state.config.clone();
    match runtime::block_on(async move {
        let native = config.init(moq_tokio::quic::Config::default())?;
        native.listen().await
    }) {
        Ok(listener) => match listener.local_addr() {
            Ok(addr) => {
                let text = addr.to_string();
                state.listener = Some(listener);
                match to_cstring(text) {
                    Ok(ptr) => write_out(out_addr, ptr),
                    Err(e) => e,
                }
            }
            Err(err) => fail(format!("bind: {err}")),
        },
        Err(err) => fail(format!("bind: {err}")),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_accept(handle: *mut c_void, out_request: *mut *mut c_void) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    if cancel::is_set(&server.closed) {
        return MOQ_NONE;
    }
    let mut state = match server.state.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let publish = state.publish.clone();
    let consume = state.consume.clone();
    let Some(listener) = state.listener.as_mut() else {
        return fail("not listening; call listen() first");
    };
    match runtime::block_on(cancel::or_set(&server.closed, listener.accept())) {
        None => {
            if let Some(listener) = state.listener.take() {
                runtime::block_on(close_listener(listener));
            }
            MOQ_NONE
        }
        Some(Some(request)) => {
            let transport = request.transport().to_string();
            let url = request.url().map(|u| u.to_string());
            let path = request.path().to_string();
            let query = request.query().map(str::to_string);
            write_out(
                out_request,
                into_raw(Request {
                    inner: Mutex::new(Some(request)),
                    publish: Mutex::new(publish),
                    consume: Mutex::new(consume),
                    transport,
                    url,
                    path,
                    query,
                }),
            )
        }
        Some(None) => MOQ_NONE,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_server_cert_fingerprints(handle: *const c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let server = match as_ref::<Server>(handle) {
        Ok(s) => s,
        Err(e) => return e,
    };
    let state = match server.state.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(listener) = state.listener.as_ref() else {
        return fail("not listening; call listen() first");
    };
    let prints = listener.certificates().fingerprints();
    match to_cstring(serde_json::to_string(&prints).unwrap_or_else(|_| "[]".into())) {
        Ok(ptr) => write_out(out_json, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_free(handle: *mut c_void) {
    drop_raw::<Request>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_url(handle: *const c_void, out_url: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    match request.url.as_deref() {
        Some(url) => match to_cstring(url) {
            Ok(ptr) => write_out(out_url, ptr),
            Err(e) => e,
        },
        None => {
            unsafe {
                if !out_url.is_null() {
                    *out_url = std::ptr::null_mut();
                }
            }
            MOQ_OK
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_path(handle: *const c_void, out_path: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    match to_cstring(request.path.clone()) {
        Ok(ptr) => write_out(out_path, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_query(handle: *const c_void, out_query: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    match request.query.as_deref() {
        Some(query) => match to_cstring(query) {
            Ok(ptr) => write_out(out_query, ptr),
            Err(e) => e,
        },
        None => {
            unsafe {
                if !out_query.is_null() {
                    *out_query = std::ptr::null_mut();
                }
            }
            MOQ_OK
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_transport(handle: *const c_void, out_transport: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    match to_cstring(request.transport.clone()) {
        Ok(ptr) => write_out(out_transport, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_accept(handle: *mut c_void, out_session: *mut *mut c_void) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let native = match request.inner.lock() {
        Ok(mut g) => g.take(),
        Err(_) => return fail("lock poisoned"),
    };
    let Some(native) = native else {
        return fail("already responded");
    };
    let publish = request.publish.lock().ok().and_then(|g| g.clone());
    let consume = request.consume.lock().ok().and_then(|g| g.clone());
    match runtime::block_on(async move {
        let (publish, subscribe) = resolve_pair(publish.as_ref(), consume.as_ref());
        let session = native
            .with_publisher(&publish)
            .with_subscriber(subscribe.clone())
            .ok()
            .await
            .map_err(|err| fail(format!("connect: {err}")))?;
        Ok::<_, i32>(Session::accepted(session, publish, subscribe))
    }) {
        Ok(session) => write_out(out_session, into_raw(session)),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_request_reject(handle: *mut c_void, code: u16) -> i32 {
    clear_error();
    let request = match as_ref::<Request>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let native = match request.inner.lock() {
        Ok(mut g) => g.take(),
        Err(_) => return fail("lock poisoned"),
    };
    let Some(native) = native else {
        return fail("already responded");
    };
    runtime::block_on(native.reject(moq_tokio::server::Reject::App(code)))
        .map(|_| MOQ_OK)
        .unwrap_or_else(|err| fail(format!("reject: {err}")))
}
