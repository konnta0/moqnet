use std::os::raw::{c_char, c_void};
use std::sync::Mutex;

use bytes::Buf;

use crate::cancel;
use crate::error::map_err;
use crate::ffi::{as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, to_cstring, write_out, MOQ_NONE, MOQ_OK};
use crate::runtime;

pub struct BroadcastConsumer {
    inner: moq_net::broadcast::Consumer,
}

pub struct RouteWatch {
    inner: Mutex<moq_net::broadcast::Consumer>,
    cancelled: cancel::Flag,
}

pub struct CatalogConsumer {
    inner: Mutex<moq_mux::catalog::hang::Consumer<moq_mux::catalog::hang::Extra>>,
    cancelled: cancel::Flag,
}

enum TrackRead {
    Arrival {
        subscriber: moq_net::track::Subscriber,
        group: Option<moq_net::group::Consumer>,
    },
    Ordered {
        subscriber: moq_net::track::Ordered,
        group: Option<moq_net::group::Consumer>,
    },
}

pub struct TrackConsumer {
    inner: Mutex<TrackRead>,
    cancelled: cancel::Flag,
}

pub struct GroupConsumer {
    sequence: u64,
    inner: Mutex<moq_net::group::Consumer>,
    cancelled: cancel::Flag,
}

pub struct MediaConsumer {
    inner: Mutex<moq_mux::container::Consumer<moq_mux::catalog::hang::Container>>,
    cancelled: cancel::Flag,
}

#[allow(dead_code)]
pub struct MediaGroupConsumer {
    sequence: u64,
    inner: Mutex<moq_mux::container::GroupConsumer<moq_mux::catalog::hang::Container>>,
    cancelled: cancel::Flag,
}

impl BroadcastConsumer {
    pub(crate) fn new(inner: moq_net::broadcast::Consumer) -> Self {
        Self { inner }
    }

    pub(crate) fn inner(&self) -> &moq_net::broadcast::Consumer {
        &self.inner
    }
}

impl TrackConsumer {
    pub(crate) fn new(track: moq_net::track::Subscriber, ordered: bool) -> Self {
        let inner = if ordered {
            TrackRead::Ordered {
                subscriber: track.ordered(),
                group: None,
            }
        } else {
            TrackRead::Arrival {
                subscriber: track,
                group: None,
            }
        };
        Self {
            inner: Mutex::new(inner),
            cancelled: cancel::new(),
        }
    }
}

impl TrackRead {
    async fn next_group(&mut self) -> Result<Option<moq_net::group::Consumer>, moq_net::Error> {
        match self {
            TrackRead::Arrival { subscriber, group } => {
                *group = None;
                subscriber.recv_group().await
            }
            TrackRead::Ordered { subscriber, group } => {
                *group = None;
                subscriber.next_group().await
            }
        }
    }

    async fn read_frame(&mut self) -> Result<Option<moq_net::frame::Frame>, moq_net::Error> {
        loop {
            if let Some(group) = self.current_group() {
                match group.read_frame().await? {
                    Some(frame) => return Ok(Some(frame)),
                    None => self.clear_group(),
                }
            }
            match self.next_group().await? {
                Some(group) => self.set_group(group),
                None => return Ok(None),
            }
        }
    }

    fn current_group(&mut self) -> Option<&mut moq_net::group::Consumer> {
        match self {
            TrackRead::Arrival { group, .. } | TrackRead::Ordered { group, .. } => group.as_mut(),
        }
    }

    fn set_group(&mut self, next: moq_net::group::Consumer) {
        match self {
            TrackRead::Arrival { group, .. } | TrackRead::Ordered { group, .. } => *group = Some(next),
        }
    }

    fn clear_group(&mut self) {
        match self {
            TrackRead::Arrival { group, .. } | TrackRead::Ordered { group, .. } => *group = None,
        }
    }
}

impl GroupConsumer {
    pub(crate) fn new(group: moq_net::group::Consumer) -> Self {
        Self {
            sequence: group.sequence,
            inner: Mutex::new(group),
            cancelled: cancel::new(),
        }
    }
}

fn subscription(priority: u8, latency_max_ms: u64, group_start: i64, group_end: i64) -> moq_net::track::Subscription {
    let mut sub = moq_net::track::Subscription::default()
        .with_priority(priority)
        .with_max_age(std::time::Duration::from_millis(latency_max_ms));
    if group_start >= 0 || group_end >= 0 {
        sub = match (group_start >= 0, group_end >= 0) {
            (true, true) => sub.with_groups(group_start as u64..=group_end as u64),
            (true, false) => sub.with_groups(group_start as u64..),
            (false, true) => sub.with_groups(..=group_end as u64),
            (false, false) => sub,
        };
    }
    sub
}

fn container_from_kind(kind: u8, init: &[u8]) -> Result<moq_mux::catalog::hang::Container, i32> {
    let hang_container = match kind {
        0 => hang::catalog::Container::Legacy,
        1 => hang::catalog::Container::Cmaf { init: init.to_vec().into() },
        2 => hang::catalog::Container::Loc,
        _ => return Err(fail("unknown container")),
    };
    moq_mux::catalog::hang::Container::new(&hang_container, moq_mux::container::Kind::Video)
        .map_err(|e| fail(format!("invalid container: {e}")))
}

fn write_bytes(out_ptr: *mut *mut u8, out_len: *mut usize, bytes: Vec<u8>) -> i32 {
    if out_ptr.is_null() || out_len.is_null() {
        return fail("null out pointer");
    }
    let mut bytes = bytes;
    let len = bytes.len();
    let ptr = bytes.as_mut_ptr();
    std::mem::forget(bytes);
    unsafe {
        *out_ptr = ptr;
        *out_len = len;
    }
    MOQ_OK
}

fn catalog_json(catalog: &moq_mux::catalog::hang::Catalog<moq_mux::catalog::hang::Extra>) -> String {
    let video: serde_json::Map<String, serde_json::Value> = catalog
        .video
        .renditions
        .iter()
        .map(|(name, config)| {
            (
                name.clone(),
                serde_json::json!({
                    "codec": config.codec.to_string(),
                    "sample_or_coded": null,
                    "bitrate": config.bitrate,
                    "stalled": config.stalled.unwrap_or(false),
                    "framerate": config.framerate,
                }),
            )
        })
        .collect();
    let audio: serde_json::Map<String, serde_json::Value> = catalog
        .audio
        .renditions
        .iter()
        .map(|(name, config)| {
            (
                name.clone(),
                serde_json::json!({
                    "codec": config.codec.to_string(),
                    "sample_rate": config.sample_rate,
                    "channel_count": config.channel_count,
                    "bitrate": config.bitrate,
                }),
            )
        })
        .collect();
    let sections: serde_json::Map<String, serde_json::Value> = catalog
        .ext
        .iter()
        .map(|(name, value)| (name.clone(), value.clone()))
        .collect();
    serde_json::json!({
        "video": video,
        "audio": audio,
        "rotation": catalog.video.rotation,
        "flip": catalog.video.flip,
        "sections": sections,
    })
    .to_string()
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_free(handle: *mut c_void) {
    drop_raw::<BroadcastConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_route_json(handle: *const c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let consumer = match as_ref::<BroadcastConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let json = serde_json::json!({
        "hops": serde_json::Value::Array(Vec::new()),
        "cost": 0,
        "announce": true,
        "path": consumer.inner.info().path.to_string(),
    });
    match to_cstring(json.to_string()) {
        Ok(ptr) => write_out(out_json, ptr),
        Err(e) => e,
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_route_updates(handle: *const c_void, out_watch: *mut *mut c_void) -> i32 {
    clear_error();
    let consumer = match as_ref::<BroadcastConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    write_out(
        out_watch,
        into_raw(RouteWatch {
            inner: Mutex::new(consumer.inner.clone()),
            cancelled: cancel::new(),
        }),
    )
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_catalog(
    handle: *const c_void,
    out_catalog: *mut *mut c_void,
) -> i32 {
    clear_error();
    let consumer = match as_ref::<BroadcastConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    let inner = consumer.inner.clone();
    match runtime::block_on(async move {
        let track = inner
            .track(hang::catalog::Catalog::DEFAULT_NAME)?
            .subscribe(hang::catalog::Catalog::default_subscription())
            .await?;
        Ok::<_, moq_net::Error>(moq_mux::catalog::hang::Consumer::from(track))
    }) {
        Ok(catalog) => write_out(
            out_catalog,
            into_raw(CatalogConsumer {
                inner: Mutex::new(catalog),
                cancelled: cancel::new(),
            }),
        ),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_track(
    handle: *const c_void,
    name: *const c_char,
    priority: u8,
    ordered: u8,
    latency_max_ms: u64,
    group_start: i64,
    group_end: i64,
    out_track: *mut *mut c_void,
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
    let sub = subscription(priority, latency_max_ms, group_start, group_end);
    let inner = consumer.inner.clone();
    match runtime::block_on(async move { inner.track(&name)?.subscribe(Some(sub)).await }) {
        Ok(track) => write_out(out_track, into_raw(TrackConsumer::new(track, ordered != 0))),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_media(
    handle: *const c_void,
    name: *const c_char,
    container_kind: u8,
    init: *const u8,
    init_len: usize,
    priority: u8,
    ordered: u8,
    latency_max_ms: u64,
    out_media: *mut *mut c_void,
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
    let init_bytes = if init.is_null() {
        &[][..]
    } else {
        std::slice::from_raw_parts(init, init_len)
    };
    let media = match container_from_kind(container_kind, init_bytes) {
        Ok(v) => v,
        Err(e) => return e,
    };
    let sub = subscription(priority, latency_max_ms, -1, -1);
    let _ = ordered;
    let inner = consumer.inner.clone();
    match runtime::block_on(async move { inner.track(&name)?.subscribe(Some(sub)).await }) {
        Ok(track) => {
            let consumer = moq_mux::container::Consumer::new(track, media);
            write_out(
                out_media,
                into_raw(MediaConsumer {
                    inner: Mutex::new(consumer),
                    cancelled: cancel::new(),
                }),
            )
        }
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_fetch_group(
    handle: *const c_void,
    name: *const c_char,
    sequence: u64,
    priority: u8,
    out_group: *mut *mut c_void,
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
    let options = moq_net::group::Fetch::default().with_priority(priority);
    let inner = consumer.inner.clone();
    match runtime::block_on(async move {
        let track = inner.track(&name)?;
        track.fetch_group(sequence, Some(options)).await
    }) {
        Ok(group) => write_out(out_group, into_raw(GroupConsumer::new(group))),
        Err(moq_net::Error::NotFound) => fail("not found"),
        Err(moq_net::Error::Unsupported) | Err(moq_net::Error::Version) => fail("unsupported"),
        Err(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_route_watch_free(handle: *mut c_void) {
    drop_raw::<RouteWatch>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_route_watch_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let watch = match as_ref::<RouteWatch>(handle) {
        Ok(w) => w,
        Err(e) => return e,
    };
    cancel::set(&watch.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_route_watch_next(handle: *mut c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let watch = match as_ref::<RouteWatch>(handle) {
        Ok(w) => w,
        Err(e) => return e,
    };
    if cancel::is_set(&watch.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match watch.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&watch.cancelled, inner.closed())) {
        None => fail("cancelled"),
        Some(moq_net::Error::Dropped) => MOQ_NONE,
        Some(err) => map_err(err),
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_catalog_consumer_free(handle: *mut c_void) {
    drop_raw::<CatalogConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_catalog_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let catalog = match as_ref::<CatalogConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    cancel::set(&catalog.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_catalog_consumer_next(handle: *mut c_void, out_json: *mut *mut c_char) -> i32 {
    clear_error();
    let catalog = match as_ref::<CatalogConsumer>(handle) {
        Ok(c) => c,
        Err(e) => return e,
    };
    if cancel::is_set(&catalog.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match catalog.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&catalog.cancelled, inner.next())) {
        None => return fail("cancelled"),
        Some(result) => match result {
        Ok(Some(next)) => match to_cstring(catalog_json(&next)) {
            Ok(ptr) => write_out(out_json, ptr),
            Err(e) => e,
        },
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_consumer_free(handle: *mut c_void) {
    drop_raw::<TrackConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let track = match as_ref::<TrackConsumer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    cancel::set(&track.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_consumer_read_frame(
    handle: *mut c_void,
    out_payload: *mut *mut u8,
    out_len: *mut usize,
    out_timestamp_us: *mut u64,
) -> i32 {
    clear_error();
    let track = match as_ref::<TrackConsumer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    if cancel::is_set(&track.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&track.cancelled, inner.read_frame())) {
        None => fail("cancelled"),
        Some(result) => match result {
        Ok(Some(frame)) => {
            let ts = match frame.timestamp.as_micros().try_into() {
                Ok(v) => v,
                Err(_) => return fail("timestamp overflow"),
            };
            unsafe {
                if !out_timestamp_us.is_null() {
                    *out_timestamp_us = ts;
                }
            }
            write_bytes(out_payload, out_len, frame.payload.to_vec())
        }
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_track_consumer_next_group(handle: *mut c_void, out_group: *mut *mut c_void) -> i32 {
    clear_error();
    let track = match as_ref::<TrackConsumer>(handle) {
        Ok(t) => t,
        Err(e) => return e,
    };
    if cancel::is_set(&track.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match track.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&track.cancelled, inner.next_group())) {
        None => fail("cancelled"),
        Some(result) => match result {
        Ok(Some(group)) => write_out(out_group, into_raw(GroupConsumer::new(group))),
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_consumer_free(handle: *mut c_void) {
    drop_raw::<GroupConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let group = match as_ref::<GroupConsumer>(handle) {
        Ok(g) => g,
        Err(e) => return e,
    };
    cancel::set(&group.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_consumer_sequence(handle: *const c_void) -> u64 {
    as_ref::<GroupConsumer>(handle).map(|g| g.sequence).unwrap_or(0)
}

#[no_mangle]
pub unsafe extern "C" fn moq_group_consumer_read_frame(
    handle: *mut c_void,
    out_payload: *mut *mut u8,
    out_len: *mut usize,
    out_timestamp_us: *mut u64,
) -> i32 {
    clear_error();
    let group = match as_ref::<GroupConsumer>(handle) {
        Ok(g) => g,
        Err(e) => return e,
    };
    if cancel::is_set(&group.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match group.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&group.cancelled, inner.read_frame())) {
        None => fail("cancelled"),
        Some(result) => match result {
            Ok(Some(frame)) => {
                let ts = match frame.timestamp.as_micros().try_into() {
                    Ok(v) => v,
                    Err(_) => return fail("timestamp overflow"),
                };
                unsafe {
                    if !out_timestamp_us.is_null() {
                        *out_timestamp_us = ts;
                    }
                }
                write_bytes(out_payload, out_len, frame.payload.to_vec())
            }
            Ok(None) => MOQ_NONE,
            Err(err) => map_err(err),
        },
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_consumer_free(handle: *mut c_void) {
    drop_raw::<MediaConsumer>(handle);
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_consumer_cancel(handle: *mut c_void) -> i32 {
    clear_error();
    let media = match as_ref::<MediaConsumer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    cancel::set(&media.cancelled);
    MOQ_OK
}

#[no_mangle]
pub unsafe extern "C" fn moq_media_consumer_next(
    handle: *mut c_void,
    out_payload: *mut *mut u8,
    out_len: *mut usize,
    out_timestamp_us: *mut u64,
    out_keyframe: *mut u8,
) -> i32 {
    clear_error();
    let media = match as_ref::<MediaConsumer>(handle) {
        Ok(m) => m,
        Err(e) => return e,
    };
    if cancel::is_set(&media.cancelled) {
        return fail("cancelled");
    }
    let mut inner = match media.inner.lock() {
        Ok(g) => g,
        Err(_) => return fail("lock poisoned"),
    };
    match runtime::block_on(cancel::or_set(&media.cancelled, inner.read())) {
        None => fail("cancelled"),
        Some(result) => match result {
        Ok(Some(mut frame)) => {
            let ts = match frame.timestamp.as_micros().try_into() {
                Ok(v) => v,
                Err(_) => return fail("timestamp overflow"),
            };
            unsafe {
                if !out_timestamp_us.is_null() {
                    *out_timestamp_us = ts;
                }
                if !out_keyframe.is_null() {
                    *out_keyframe = u8::from(frame.keyframe);
                }
            }
            let payload = frame.payload.copy_to_bytes(frame.payload.remaining()).to_vec();
            write_bytes(out_payload, out_len, payload)
        }
        Ok(None) => MOQ_NONE,
        Err(err) => map_err(err),
        }
    }
}
