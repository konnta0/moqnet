use std::os::raw::{c_char, c_void};
use std::sync::Mutex;

use crate::cancel;
use crate::consumer::BroadcastConsumer;
use crate::error::{closed, map_err};
use crate::ffi::{
    as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, to_cstring, write_out, MOQ_NONE, MOQ_OK,
};
use crate::producer::BroadcastProducer;
use crate::runtime;

#[derive(Clone)]
pub struct OriginProducer {
    inner: moq_net::origin::Producer,
}

#[derive(Clone)]
pub struct OriginConsumer {
    inner: moq_net::origin::Consumer,
}

pub struct Announced {
    origin: moq_net::origin::Consumer,
    inner: Mutex<moq_net::announce::Consumer>,
    cancelled: cancel::Flag,
}

pub struct AnnouncedBroadcast {
    origin: moq_net::origin::Consumer,
    path: moq_net::PathOwned,
    cancelled: cancel::Flag,
}

pub struct OriginDynamic {
    inner: Mutex<Option<moq_net::origin::Dynamic>>,
    cancelled: cancel::Flag,
}

pub struct BroadcastRequest {
    inner: Mutex<Option<moq_net::origin::Request>>,
}

impl OriginProducer {
    pub(crate) fn from_inner(inner: moq_net::origin::Producer) -> Self {
        Self { inner }
    }

    pub(crate) fn inner(&self) -> &moq_net::origin::Producer {
        &self.inner
    }
}

impl OriginConsumer {
    pub(crate) fn from_inner(inner: moq_net::origin::Consumer) -> Self {
        Self { inner }
    }

    #[allow(dead_code)]
    pub(crate) fn inner(&self) -> &moq_net::origin::Consumer {
        &self.inner
    }
}

fn spawn_origin(cache_capacity_bytes: i64) -> moq_net::origin::Producer {
    let mut config = moq_net::origin::Config::default();
    if cache_capacity_bytes >= 0 {
        config.pool = moq_net::cache::Pool::new(
            moq_net::cache::Config::default().with_capacity(cache_capacity_bytes as u64),
        );
    }
    moq_tokio::origin::spawn_config(config)
}

pub(crate) fn resolve_pair(
    publish: Option<&OriginProducer>,
    consume: Option<&OriginProducer>,
) -> (moq_net::origin::Producer, moq_net::origin::Producer) {
    if publish.is_none() && consume.is_none() {
        let shared = spawn_origin(-1);
        return (shared.clone(), shared);
    }
    let resolve = |origin: Option<&OriginProducer>| {
        origin
            .map(|o| o.inner().clone())
            .unwrap_or_else(|| spawn_origin(-1))
    };
    (resolve(publish), resolve(consume))
}

#[no_mangle]
pub extern "C" fn moq_origin_producer_new(cache_capacity_bytes: i64) -> *mut c_void {
    clear_error();
    let _guard = runtime::enter();
    into_raw(OriginProducer::from_inner(spawn_origin(cache_capacity_bytes)))
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_producer_free(handle: *mut c_void) {
    drop_raw::<OriginProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_producer_consume(handle: *const c_void, out_consumer: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let origin = match as_ref::<OriginProducer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    write_out(
        out_consumer,
        into_raw(OriginConsumer::from_inner(origin.inner.consume())),
    )
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_producer_create_broadcast(
    handle: *const c_void,
    path: *const c_char,
    out_broadcast: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let origin = match as_ref::<OriginProducer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    let path = match read_cstr(path) {
        Ok(v) => v,
        Err(e) => return e,
    };
    match origin
        .inner
        .publish(path, moq_net::origin::Route::default())
    {
        Ok(broadcast) => match BroadcastProducer::from_inner(broadcast) {
            Ok(producer) => write_out(out_broadcast, into_raw(producer)),
            Err(e) => e,
        },
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_producer_dynamic(handle: *const c_void, out_dynamic: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let origin = match as_ref::<OriginProducer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    match origin.inner.dynamic("", moq_net::origin::Route::default()) {
        Ok(dynamic) => write_out(
            out_dynamic,
            into_raw(OriginDynamic {
                inner: Mutex::new(Some(dynamic)),
                cancelled: cancel::new(),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_consumer_free(handle: *mut c_void) {
    drop_raw::<OriginConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_consumer_announced(
    handle: *const c_void,
    prefix: *const c_char,
    out_announced: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let origin = match as_ref::<OriginConsumer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    let prefix = match read_cstr(prefix) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let scoped = match origin
        .inner
        .scope(prefix, &moq_net::Patterns::from(moq_net::Pattern::all()))
    {
        Ok(v) => v,
        Err(_) => return fail("unauthorized"),
    };
    write_out(
        out_announced,
        into_raw(Announced {
            origin: scoped.clone(),
            inner: Mutex::new(scoped.announced()),
            cancelled: cancel::new(),
        }),
    )
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_consumer_announced_broadcast(
    handle: *const c_void,
    path: *const c_char,
    out_wait: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let origin = match as_ref::<OriginConsumer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    let path = match read_cstr(path) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let named = moq_net::Path::new(&path).to_owned();
    if origin
        .inner
        .scope(&named, &moq_net::Patterns::from(moq_net::Pattern::all()))
        .is_err()
    {
        return fail("unauthorized");
    }
    write_out(
        out_wait,
        into_raw(AnnouncedBroadcast {
            origin: origin.inner.clone(),
            path: named,
            cancelled: cancel::new(),
        }),
    )
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_consumer_request_broadcast(
    handle: *const c_void,
    path: *const c_char,
    out_broadcast: *mut *mut c_void,
) -> i32 {
    clear_error();
    let origin = match as_ref::<OriginConsumer>(handle) {
        Ok(o) => o,
        Err(e) => return e,
    };
    let path = match read_cstr(path) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let inner = origin.inner.clone();
    match runtime::block_on(async move { inner.request_broadcast(path.as_str()).await }) {
        Ok(broadcast) => write_out(out_broadcast, into_raw(BroadcastConsumer::new(broadcast))),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_free(handle: *mut c_void) {
    drop_raw::<Announced>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let announced = match as_ref::<Announced>(handle) {
        Ok(a) => a,
        Err(e) => return e,
    };
    cancel::set(&announced.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_next(
    handle: *mut c_void,
    out_path: *mut *mut c_char,
    out_broadcast: *mut *mut c_void,
) -> i32 {
    clear_error();
    let announced = match as_ref::<Announced>(handle) {
        Ok(a) => a,
        Err(e) => return e,
    };
    if cancel::is_set(&announced.cancelled) {
        return fail("cancelled");
    }

    loop {
        let next = {
            let mut inner = match announced.inner.lock() {
                Ok(g) => g,
                Err(_) => return fail("lock poisoned"),
            };
            match runtime::block_on(cancel::or_set(&announced.cancelled, inner.next())) {
                None => return fail("cancelled"),
                Some(value) => value,
            }
        };
        match next {
            Some(update) => {
                if cancel::is_set(&announced.cancelled) {
                    return fail("cancelled");
                }
                if !update.kind.is_active() {
                    continue;
                }
                let path = update.prefix.to_string();
                let origin = announced.origin.clone();
                let broadcast = match runtime::block_on(origin.request_broadcast(path.as_str())) {
                    Ok(broadcast) => broadcast,
                    Err(moq_net::Error::Unroutable) => continue,
                    Err(err) => return map_err(err),
                };
                let path_ptr = match to_cstring(path) {
                    Ok(p) => p,
                    Err(e) => return e,
                };
                let rc = write_out(out_path, path_ptr);
                if rc != MOQ_OK {
                    return rc;
                }
                return write_out(out_broadcast, into_raw(BroadcastConsumer::new(broadcast)));
            }
            None => return MOQ_NONE,
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_broadcast_free(handle: *mut c_void) {
    drop_raw::<AnnouncedBroadcast>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_broadcast_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let wait = match as_ref::<AnnouncedBroadcast>(handle) {
        Ok(a) => a,
        Err(e) => return e,
    };
    cancel::set(&wait.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_announced_broadcast_available(
    handle: *mut c_void,
    out_broadcast: *mut *mut c_void,
) -> i32 {
    clear_error();
    let wait = match as_ref::<AnnouncedBroadcast>(handle) {
        Ok(a) => a,
        Err(e) => return e,
    };
    if cancel::is_set(&wait.cancelled) {
        return fail("cancelled");
    }
    let origin = wait.origin.clone();
    let path = wait.path.clone();
    match runtime::block_on(cancel::or_set(&wait.cancelled, origin.routed_broadcast(&path))) {
        None => fail("cancelled"),
        Some(Ok(broadcast)) => write_out(out_broadcast, into_raw(BroadcastConsumer::new(broadcast))),
        Some(Err(err)) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_dynamic_free(handle: *mut c_void) {
    drop_raw::<OriginDynamic>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_dynamic_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let dynamic = match as_ref::<OriginDynamic>(handle) {
        Ok(d) => d,
        Err(e) => return e,
    };
    cancel::set(&dynamic.cancelled);
    if let Ok(mut inner) = dynamic.inner.try_lock() {
        *inner = None;
    }
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_origin_dynamic_requested_broadcast(
    handle: *mut c_void,
    out_request: *mut *mut c_void,
) -> i32 {
    clear_error();
    let dynamic = match as_ref::<OriginDynamic>(handle) {
        Ok(d) => d,
        Err(e) => return e,
    };
    if cancel::is_set(&dynamic.cancelled) {
        return closed();
    }
    let mut slot = match dynamic.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let next = {
        let Some(inner) = slot.as_mut() else {
            return closed();
        };
        runtime::block_on(cancel::or_set(&dynamic.cancelled, inner.requested_broadcast()))
    };
    match next {
        None => {
            *slot = None;
            closed()
        }
        Some(Ok(request)) => write_out(
            out_request,
            into_raw(BroadcastRequest {
                inner: Mutex::new(Some(request)),
            }),
        ),
        Some(Err(err)) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_request_free(handle: *mut c_void) {
    drop_raw::<BroadcastRequest>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_request_path(handle: *const c_void, out_path: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<BroadcastRequest>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let guard = match request.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_ref() else {
        return closed();
    };
    match to_cstring(inner.path().to_string()) {
        Ok(ptr) => write_out(out_path, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_request_accept(handle: *mut c_void, broadcast: *const c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let request = match as_ref::<BroadcastRequest>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let producer = match as_ref::<BroadcastProducer>(broadcast) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let consumer = match producer.consume_inner() {
        Ok(c) => c,
        Err(e) => return e,
    };
    let mut guard = match request.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.take() else {
        return closed();
    };
    inner.accept(&consumer);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_request_abort(handle: *mut c_void, error_code: u16) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let request = match as_ref::<BroadcastRequest>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let mut guard = match request.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.take() else {
        return closed();
    };
    inner.reject(moq_net::Error::App(error_code));
    MOQ_OK
}
