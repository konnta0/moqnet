use std::os::raw::{c_char, c_void};
use std::sync::Mutex;

use moq_mux::catalog::hang::Extra;

use crate::cancel;
use crate::consumer::{BroadcastConsumer, GroupConsumer, TrackConsumer};
use crate::error::{closed, map_err};
use crate::ffi::{
    as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, to_cstring, write_out, MOQ_OK,
};
use crate::runtime;

pub(crate) struct BroadcastState {
    pub broadcast: moq_net::broadcast::Producer,
    pub catalog: moq_mux::catalog::Producer<Extra>,
}

pub struct BroadcastProducer {
    state: Mutex<Option<BroadcastState>>,
}

pub struct BroadcastDynamic {
    inner: Mutex<moq_net::broadcast::Dynamic>,
    cancelled: cancel::Flag,
}

pub struct TrackRequest {
    inner: Mutex<Option<moq_net::track::Request>>,
}

#[allow(dead_code)]
pub struct TrackDynamic {
    inner: Mutex<moq_net::track::Dynamic>,
    cancelled: Mutex<bool>,
}

#[allow(dead_code)]
pub struct GroupRequest {
    sequence: u64,
    priority: u8,
    inner: Mutex<Option<moq_net::group::Request>>,
}

pub struct TrackProducer {
    inner: Mutex<Option<moq_net::track::Producer>>,
}

pub struct GroupProducer {
    sequence: u64,
    inner: Mutex<Option<moq_net::group::Producer>>,
}

pub struct MediaProducer {
    decoder: Mutex<Option<MediaDecoder>>,
    demand: Option<moq_net::track::Demand>,
}

enum MediaDecoder {
    Track(Box<moq_mux::import::Track>),
    Container(moq_mux::import::Container<Extra>),
}

pub struct MediaStreamProducer {
    decoder: Mutex<Option<StreamDecoder>>,
}

enum StreamDecoder {
    Track(Box<moq_mux::import::TrackStream>),
    Container(moq_mux::import::ContainerStream<Extra>),
}

impl BroadcastProducer {
    pub(crate) fn from_inner(mut broadcast: moq_net::broadcast::Producer) -> Result<Self, i32> {
        let catalog = moq_mux::catalog::Producer::new(
            &mut broadcast,
            moq_mux::catalog::Config::default().with_catalog(moq_mux::catalog::hang::Catalog::<Extra>::default()),
        )
        .map_err(map_err)?;
        Ok(Self {
            state: Mutex::new(Some(BroadcastState { broadcast, catalog })),
        })
    }

    pub(crate) fn consume_inner(&self) -> Result<moq_net::broadcast::Consumer, i32> {
        let guard = self.state.lock().map_err(|_| fail("lock poisoned"))?;
        let state = guard.as_ref().ok_or_else(closed)?;
        Ok(state.broadcast.consume())
    }

    pub(crate) fn with_state<R>(&self, f: impl FnOnce(&mut BroadcastState) -> Result<R, i32>) -> Result<R, i32> {
        let mut guard = self.state.lock().map_err(|_| fail("lock poisoned"))?;
        let state = guard.as_mut().ok_or_else(closed)?;
        f(state)
    }
}

fn reserve_media_track(
    state: &mut BroadcastState,
    format: &moq_mux::import::Format,
    build: impl FnOnce(
        moq_net::track::Request,
        moq_mux::catalog::Reserved<Extra>,
    ) -> Result<moq_mux::import::Track, moq_mux::Error>,
) -> Result<*mut c_void, i32> {
    let mut broadcast = state.broadcast.clone();
    let name = broadcast.unique_name(&format!(".{format}"));
    let request = broadcast.reserve_track(name).map_err(map_err)?;
    let import = build(request, state.catalog.reserve()).map_err(map_err)?;
    let demand = import.demand();
    Ok(into_raw(MediaProducer {
        decoder: Mutex::new(Some(MediaDecoder::Track(Box::new(import)))),
        demand: Some(demand),
    }))
}

fn raw_track_info(priority: u8, _ordered: u8, latency_max_ms: i64, timescale: i64) -> Result<moq_net::track::Info, i32> {
    let mut info = moq_net::track::Info::default()
        .with_timescale(moq_net::Timescale::MICRO)
        .with_priority(priority);
    if latency_max_ms >= 0 {
        info = info.with_max_age(std::time::Duration::from_millis(latency_max_ms as u64));
    }
    if timescale > 0 {
        let scale = moq_net::Timescale::new(timescale as u64).map_err(|_| fail("invalid timescale"))?;
        info = info.with_timescale(scale);
    }
    Ok(info)
}

#[no_mangle]
pub extern "C" fn moq_broadcast_producer_new(out_broadcast: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    match BroadcastProducer::from_inner(moq_net::broadcast::Info::new().produce()) {
        Ok(producer) => write_out(out_broadcast, into_raw(producer)),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_free(handle: *mut c_void) {
    drop_raw::<BroadcastProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_consume(handle: *const c_void, out_consumer: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    match producer.consume_inner() {
        Ok(consumer) => write_out(out_consumer, into_raw(BroadcastConsumer::new(consumer))),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_dynamic(handle: *const c_void, out_dynamic: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    match producer.with_state(|state| {
        Ok(into_raw(BroadcastDynamic {
            inner: Mutex::new(state.broadcast.dynamic()),
            cancelled: cancel::new(),
        }))
    }) {
        Ok(ptr) => write_out(out_dynamic, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_set_announce(handle: *const c_void, announce: u8) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    producer
        .with_state(|state| {
            if announce != 0 {
                state
                    .broadcast
                    .announce(moq_net::origin::Route::default())
                    .map_err(map_err)
            } else {
                state.broadcast.unannounce();
                Ok(())
            }
        })
        .map(|_| MOQ_OK)
        .unwrap_or_else(|e| e)
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_set_catalog_section(
    handle: *const c_void,
    name: *const c_char,
    json: *const c_char,
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
    let json = match read_cstr(json) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let value: serde_json::Value = match serde_json::from_str(json) {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };
    producer
        .with_state(|state| {
            state
                .catalog
                .modify()
                .map_err(map_err)?
                .set_section(name, value)
                .map_err(map_err)
        })
        .map(|_| MOQ_OK)
        .unwrap_or_else(|e| e)
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_remove_catalog_section(
    handle: *const c_void,
    name: *const c_char,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let name = match read_cstr(name) {
        Ok(v) => v,
        Err(e) => return e,
    };
    producer
        .with_state(|state| {
            state.catalog.modify().map_err(map_err)?.remove_section(name);
            Ok(())
        })
        .map(|_| MOQ_OK)
        .unwrap_or_else(|e| e)
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_track(
    handle: *const c_void,
    name: *const c_char,
    priority: u8,
    ordered: u8,
    latency_max_ms: i64,
    timescale: i64,
    out_track: *mut *mut c_void,
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
    let info = match raw_track_info(priority, ordered, latency_max_ms, timescale) {
        Ok(v) => v,
        Err(e) => return e,
    };
    match producer.with_state(|state| {
        let mut broadcast = state.broadcast.clone();
        broadcast
            .create_track(name, Some(info))
            .map_err(map_err)
            .map(|track| {
                into_raw(TrackProducer {
                    inner: Mutex::new(Some(track)),
                })
            })
    }) {
        Ok(ptr) => write_out(out_track, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_media(
    handle: *const c_void,
    format: *const c_char,
    init_data: *const u8,
    init_len: usize,
    out_media: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let format = match read_cstr(format) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let parsed: moq_mux::import::Format = match format.parse() {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };
    let data = if init_data.is_null() {
        Vec::new()
    } else {
        std::slice::from_raw_parts(init_data, init_len).to_vec()
    };

    match producer.with_state(|state| match parsed {
        moq_mux::import::Format::Container(kind) => {
            let init = moq_mux::import::ContainerInit::new(kind, data);
            moq_mux::import::Container::new(state.broadcast.clone(), state.catalog.reserve(), &init)
                .map_err(map_err)
                .map(|container| {
                    into_raw(MediaProducer {
                        decoder: Mutex::new(Some(MediaDecoder::Container(container))),
                        demand: None,
                    })
                })
        }
        moq_mux::import::Format::Audio(kind) => {
            let init = moq_mux::import::AudioInit::new(kind, data);
            reserve_media_track(state, &parsed, |request, reserved| {
                moq_mux::import::Track::audio(request, reserved, init)
            })
        }
        moq_mux::import::Format::Video(kind) => {
            let init = moq_mux::import::VideoInit::new(kind, data);
            reserve_media_track(state, &parsed, |request, reserved| {
                moq_mux::import::Track::video(request, reserved, init)
            })
        }
        _ => Err(fail("unknown media format")),
    }) {
        Ok(ptr) => write_out(out_media, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_media_stream(
    handle: *const c_void,
    format: *const c_char,
    out_media: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let format = match read_cstr(format) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let parsed: moq_mux::import::Format = match format.parse() {
        Ok(v) => v,
        Err(err) => return map_err(err),
    };
    match producer.with_state(|state| match parsed {
        moq_mux::import::Format::Container(kind) => moq_mux::import::ContainerStream::new(
            state.broadcast.clone(),
            state.catalog.reserve(),
            kind,
        )
        .map_err(map_err)
        .map(|container| {
            into_raw(MediaStreamProducer {
                decoder: Mutex::new(Some(StreamDecoder::Container(container))),
            })
        }),
        moq_mux::import::Format::Video(kind) => {
            let init = moq_mux::import::VideoInit::new(kind, Vec::<u8>::new());
            let mut broadcast = state.broadcast.clone();
            let name = broadcast.unique_name(&format!(".{parsed}"));
            let request = broadcast.reserve_track(name).map_err(map_err)?;
            moq_mux::import::TrackStream::video(request, state.catalog.reserve(), init)
                .map_err(map_err)
                .map(|import| {
                    into_raw(MediaStreamProducer {
                        decoder: Mutex::new(Some(StreamDecoder::Track(Box::new(import)))),
                    })
                })
        }
        moq_mux::import::Format::Audio(_) => Err(fail("audio stream import is not supported")),
        _ => Err(fail("unknown media format")),
    }) {
        Ok(ptr) => write_out(out_media, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let producer = match as_ref::<BroadcastProducer>(handle) {
        Ok(p) => p,
        Err(e) => return e,
    };
    let mut guard = match producer.state.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut state) = guard.take() else {
        return closed();
    };
    state.broadcast.close();
    state.catalog.finish().map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_dynamic_free(handle: *mut c_void) {
    drop_raw::<BroadcastDynamic>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_dynamic_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let dynamic = match as_ref::<BroadcastDynamic>(handle) {
        Ok(d) => d,
        Err(e) => return e,
    };
    cancel::set(&dynamic.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_dynamic_requested_track(
    handle: *mut c_void,
    out_request: *mut *mut c_void,
) -> i32 {
    clear_error();
    let dynamic = match as_ref::<BroadcastDynamic>(handle) {
        Ok(d) => d,
        Err(e) => return e,
    };
    if cancel::is_set(&dynamic.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match dynamic.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let next = match runtime::block_on(cancel::or_set(&dynamic.cancelled, inner.requested_track())) {
        None => return fail("cancelled"),
        Some(value) => value,
    };
    match next {
        Ok(request) => write_out(
            out_request,
            into_raw(TrackRequest {
                inner: Mutex::new(Some(request)),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_request_free(handle: *mut c_void) {
    drop_raw::<TrackRequest>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_request_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
    clear_error();
    let request = match as_ref::<TrackRequest>(handle) {
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
    match to_cstring(inner.name().to_string()) {
        Ok(ptr) => write_out(out_name, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_request_accept(
    handle: *mut c_void,
    priority: u8,
    ordered: u8,
    latency_max_ms: i64,
    timescale: i64,
    out_track: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let request = match as_ref::<TrackRequest>(handle) {
        Ok(r) => r,
        Err(e) => return e,
    };
    let info = match raw_track_info(priority, ordered, latency_max_ms, timescale) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let mut guard = match request.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.take() else {
        return closed();
    };
    write_out(
        out_track,
        into_raw(TrackProducer {
            inner: Mutex::new(Some(inner.accept(Some(info)))),
        }),
    )
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_request_abort(handle: *mut c_void, error_code: u16) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let request = match as_ref::<TrackRequest>(handle) {
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

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_free(handle: *mut c_void) {
    drop_raw::<TrackProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_ref() else {
        return closed();
    };
    match to_cstring(inner.name().to_string()) {
        Ok(ptr) => write_out(out_name, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_append_group(handle: *mut c_void, out_group: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let mut guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    match inner.append_group() {
        Ok(group) => write_out(
            out_group,
            into_raw(GroupProducer {
                sequence: group.sequence,
                inner: Mutex::new(Some(group)),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_write_frame(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    if payload.is_null() && payload_len > 0 {
        return fail("null payload");
    }
    let bytes = if payload_len == 0 {
        Vec::new()
    } else {
        std::slice::from_raw_parts(payload, payload_len).to_vec()
    };
    let timestamp = match moq_net::Timestamp::from_micros(timestamp_us) {
        Ok(t) => t,
        Err(err) => return map_err(err),
    };
    let mut guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    inner.write_frame(timestamp, bytes).map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_append_datagram(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
    out_sequence: *mut u64,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let bytes = if payload.is_null() || payload_len == 0 {
        Vec::new()
    } else {
        std::slice::from_raw_parts(payload, payload_len).to_vec()
    };
    let timestamp = match moq_net::Timestamp::from_micros(timestamp_us) {
        Ok(t) => t,
        Err(err) => return map_err(err),
    };
    let mut guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    match inner.append_datagram(timestamp, bytes) {
        Ok(seq) => write_out(out_sequence, seq),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_used(handle: *const c_void) -> i32 {
    clear_error();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let cloned = match track.inner.lock() {
        Ok(g) => g.as_ref().cloned().ok_or_else(closed),
        Err(_) => Err(fail("lock poisoned")),
    };
    let inner = match cloned {
        Ok(v) => v,
        Err(e) => return e,
    };
    runtime::block_on(async move { inner.used().await })
        .map(|_| MOQ_OK)
        .unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_unused(handle: *const c_void) -> i32 {
    clear_error();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let cloned = match track.inner.lock() {
        Ok(g) => g.as_ref().cloned().ok_or_else(closed),
        Err(_) => Err(fail("lock poisoned")),
    };
    let inner = match cloned {
        Ok(v) => v,
        Err(e) => return e,
    };
    runtime::block_on(async move { inner.unused().await })
        .map(|_| MOQ_OK)
        .unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_consume(
    handle: *const c_void,
    out_consumer: *mut *mut c_void,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_ref() else {
        return closed();
    };
    write_out(out_consumer, into_raw(TrackConsumer::new(inner.subscribe(None), false)))
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let mut guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut inner) = guard.take() else {
        return closed();
    };
    if inner.final_sequence().is_none() {
        inner.finish().map(|_| MOQ_OK).unwrap_or_else(map_err)
    } else {
        MOQ_OK
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_producer_abort(handle: *mut c_void, error_code: u16) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let track = match as_ref::<TrackProducer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    let mut guard = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.take() else {
        return closed();
    };
    inner.abort(moq_net::Error::App(error_code)).map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_producer_free(handle: *mut c_void) {
    drop_raw::<GroupProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_producer_sequence(handle: *const c_void) -> u64 {
    as_ref::<GroupProducer>(handle).map(|g| g.sequence).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_producer_write_frame(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let group = match as_ref::<GroupProducer>(handle) {
        Ok(g) => g,
        Err(e) => return e,
    };
    let bytes = if payload.is_null() || payload_len == 0 {
        Vec::new()
    } else {
        std::slice::from_raw_parts(payload, payload_len).to_vec()
    };
    let timestamp = match moq_net::Timestamp::from_micros(timestamp_us) {
        Ok(t) => t,
        Err(err) => return map_err(err),
    };
    let mut guard = match group.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_mut() else {
        return closed();
    };
    inner.write_frame(timestamp, bytes).map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let group = match as_ref::<GroupProducer>(handle) {
        Ok(g) => g,
        Err(e) => return e,
    };
    let mut guard = match group.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut inner) = guard.take() else {
        return closed();
    };
    inner.finish().map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_producer_consume(handle: *const c_void, out_consumer: *mut *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let group = match as_ref::<GroupProducer>(handle) {
        Ok(g) => g,
        Err(e) => return e,
    };
    let guard = match group.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(inner) = guard.as_ref() else {
        return closed();
    };
    write_out(out_consumer, into_raw(GroupConsumer::new(inner.consume())))
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_producer_free(handle: *mut c_void) {
    drop_raw::<MediaProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let media = match as_ref::<MediaProducer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    let Some(demand) = media.demand.as_ref() else {
        return fail("demand unavailable for a multi-track container");
    };
    match to_cstring(demand.name().to_string()) {
        Ok(ptr) => write_out(out_name, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_producer_write_frame(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let media = match as_ref::<MediaProducer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    let bytes = if payload.is_null() || payload_len == 0 {
        Vec::new()
    } else {
        std::slice::from_raw_parts(payload, payload_len).to_vec()
    };
    let timestamp = match moq_net::Timestamp::from_micros(timestamp_us) {
        Ok(t) => t,
        Err(err) => return map_err(err),
    };
    let mut guard = match media.decoder.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(decoder) = guard.as_mut() else {
        return closed();
    };
    let result = match decoder {
        MediaDecoder::Track(t) => t.decode(&bytes, Some(timestamp)),
        MediaDecoder::Container(c) => c.decode(&bytes),
    };
    result.map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let media = match as_ref::<MediaProducer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    let mut guard = match media.decoder.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut decoder) = guard.take() else {
        return closed();
    };
    let result = match &mut decoder {
        MediaDecoder::Track(t) => t.finish(),
        MediaDecoder::Container(c) => c.finish(),
    };
    result.map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_stream_producer_free(handle: *mut c_void) {
    drop_raw::<MediaStreamProducer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_stream_producer_write(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let media = match as_ref::<MediaStreamProducer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    let bytes = if payload.is_null() || payload_len == 0 {
        Vec::new()
    } else {
        std::slice::from_raw_parts(payload, payload_len).to_vec()
    };
    let mut guard = match media.decoder.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(decoder) = guard.as_mut() else {
        return closed();
    };
    let result = match decoder {
        StreamDecoder::Track(t) => t.decode(&bytes),
        StreamDecoder::Container(c) => c.decode(&bytes),
    };
    result.map(|_| MOQ_OK).unwrap_or_else(map_err)
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_stream_producer_finish(handle: *mut c_void) -> i32 {
    clear_error();
    let _guard = runtime::enter();
    let media = match as_ref::<MediaStreamProducer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    let mut guard = match media.decoder.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    let Some(mut decoder) = guard.take() else {
        return closed();
    };
    let result = match &mut decoder {
        StreamDecoder::Track(t) => t.finish(),
        StreamDecoder::Container(c) => c.finish(),
    };
    result.map(|_| MOQ_OK).unwrap_or_else(map_err)
}
