use std::os::raw::{c_char, c_void};
use std::sync::Mutex;

use serde_json::Value;

use crate::cancel;
use crate::consumer::BroadcastConsumer;
use crate::error::{closed, map_err};
use crate::ffi::{as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, to_cstring, write_out, MOQ_NONE, MOQ_OK};
use crate::producer::BroadcastProducer;
use crate::runtime;

fn json_compression(compression: u8) -> moq_json::Compression {
    if compression != 0 {
        moq_json::Compression::Deflate
    } else {
        moq_json::Compression::None
    }
}

pub struct JsonSnapshotProducer {
    inner: Mutex<Option<moq_json::snapshot::Producer<Value>>>,
}

pub struct JsonStreamProducer {
    inner: Mutex<Option<moq_json::stream::Producer<Value>>>,
}

pub struct JsonSnapshotConsumer {
    inner: Mutex<moq_json::snapshot::Consumer<Value>>,
    cancelled: cancel::Flag,
}

pub struct JsonStreamConsumer {
    inner: Mutex<moq_json::stream::Consumer<Value>>,
    cancelled: cancel::Flag,
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_json_snapshot(
    handle: *const c_void,
    name: *const c_char,
    delta_ratio: u32,
    compression: u8,
    out_producer: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let name = match read_cstr(name) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let mut config = moq_json::snapshot::Config::default();
    config.delta_ratio = delta_ratio;
    config.compression = json_compression(compression);
    match producer.with_state(|state| {
        let mut broadcast = state.broadcast.clone();
        let track = broadcast.create_track(name, None).map_err(map_err)?;
        Ok(into_raw(JsonSnapshotProducer {
            inner: Mutex::new(Some(moq_json::snapshot::Producer::<Value>::new(track, config))),
        }))
    }) {
        Ok(ptr) => write_out(out_producer, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_json_stream(
    handle: *const c_void,
    name: *const c_char,
    compression: u8,
    out_producer: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let name = match read_cstr(name) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let mut config = moq_json::stream::Config::default();
    config.compression = json_compression(compression);
    match producer.with_state(|state| {
        let mut broadcast = state.broadcast.clone();
        let track = broadcast.create_track(name, None).map_err(map_err)?;
        Ok(into_raw(JsonStreamProducer {
            inner: Mutex::new(Some(moq_json::stream::Producer::<Value>::new(track, config))),
        }))
    }) {
        Ok(ptr) => write_out(out_producer, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_producer_free(handle: *mut c_void) {
    drop_raw::<JsonSnapshotProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_producer_update(handle: *mut c_void, value: *const c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<JsonSnapshotProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let value = match read_cstr(value) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let parsed: Value = match serde_json::from_str(value) {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };
    let mut guard = match producer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    inner.update(&parsed).map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<JsonSnapshotProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let mut guard = match producer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut inner) = guard.take() else {
        return closed();
    };
    inner.finish().map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_producer_free(handle: *mut c_void) {
    drop_raw::<JsonStreamProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_producer_append(handle: *mut c_void, value: *const c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<JsonStreamProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let value = match read_cstr(value) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let parsed: Value = match serde_json::from_str(value) {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };
    let mut guard = match producer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    inner.append(&parsed).map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<JsonStreamProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let mut guard = match producer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut inner) = guard.take() else {
        return closed();
    };
    inner.finish().map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_json_snapshot(
    handle: *const c_void,
    name: *const c_char,
    compression: u8,
    out_consumer: *mut *mut c_void,
) -> i32 {
    clear_error();
    let consumer = match as_ref::<BroadcastConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let name = match read_cstr(name) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let mut config = moq_json::snapshot::consumer::Config::default();
    config.compression = json_compression(compression);
    let inner = consumer.inner().clone();
    match runtime::block_on(async move { inner.track(&name)?.subscribe(None).await }) {
        Ok(track) => write_out(
            out_consumer,
            into_raw(JsonSnapshotConsumer {
                inner: Mutex::new(moq_json::snapshot::Consumer::<Value>::new(track, config)),
                cancelled: cancel::new(),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_json_stream(
    handle: *const c_void,
    name: *const c_char,
    compression: u8,
    out_consumer: *mut *mut c_void,
) -> i32 {
    clear_error();
    let consumer = match as_ref::<BroadcastConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let name = match read_cstr(name) {
        Ok(v) => v.to_string(),
        Err(e) => return e,
    };
    let mut config = moq_json::stream::Config::default();
    config.compression = json_compression(compression);
    let inner = consumer.inner().clone();
    match runtime::block_on(async move { inner.track(&name)?.subscribe(None).await }) {
        Ok(track) => write_out(
            out_consumer,
            into_raw(JsonStreamConsumer {
                inner: Mutex::new(moq_json::stream::Consumer::<Value>::new(track, config)),
                cancelled: cancel::new(),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_consumer_free(handle: *mut c_void) {
    drop_raw::<JsonSnapshotConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let consumer = match as_ref::<JsonSnapshotConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    cancel::set(&consumer.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_snapshot_consumer_next(handle: *mut c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let consumer = match as_ref::<JsonSnapshotConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    if cancel::is_set(&consumer.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match consumer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let next = match runtime::block_on(cancel::or_set(&consumer.cancelled, inner.next())) {
        None => return fail("cancelled"),
        Some(value) => value,
    };
    match next {
        Ok(Some(value)) => match serde_json::to_string(&value) {
            Ok(s) => match to_cstring(s) {
                Ok(ptr) => write_out(out_json, ptr),
                Err(e) => e,
            },
            Err(err) => map_err(err),
        },
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_consumer_free(handle: *mut c_void) {
    drop_raw::<JsonStreamConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let consumer = match as_ref::<JsonStreamConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    cancel::set(&consumer.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_json_stream_consumer_next(handle: *mut c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let consumer = match as_ref::<JsonStreamConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    if cancel::is_set(&consumer.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match consumer.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let next = match runtime::block_on(cancel::or_set(&consumer.cancelled, inner.next())) {
        None => return fail("cancelled"),
        Some(value) => value,
    };
    match next {
        Ok(Some(value)) => match serde_json::to_string(&value) {
            Ok(s) => match to_cstring(s) {
                Ok(ptr) => write_out(out_json, ptr),
                Err(e) => e,
            },
            Err(err) => map_err(err),
        },
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
    }
}
