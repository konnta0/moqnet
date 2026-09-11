use std::os::raw::{c_char, c_void};

#[cfg(not(feature = "video"))]
use crate::error::not_supported;
#[cfg(not(feature = "video"))]
use crate::ffi::clear_error;

#[cfg(feature = "video")]
mod imp {
    use std::os::raw::{c_char, c_void};
    use std::sync::Mutex;

    use crate::error::{closed, map_err};
    use crate::ffi::{as_ref, clear_error, drop_raw, fail, into_raw, read_cstr_opt, to_cstring, write_out, MOQ_OK};
    use crate::producer::BroadcastProducer;
    use crate::runtime;

    struct VideoProducer {
        encoder: moq_video::encode::Sink,
        producer: moq_video::encode::Producer<moq_mux::catalog::hang::Extra>,
        format: u8,
        size: moq_video::Size,
    }

    pub struct Handle {
        inner: Mutex<Option<VideoProducer>>,
    }

    fn block_on<T>(future: impl std::future::Future<Output = T>) -> T {
        pollster::block_on(future)
    }

    pub unsafe fn publish_video(
        handle: *const c_void,
        format: u8,
        width: u32,
        height: u32,
        framerate: u32,
        codec: u8,
        track: *const c_char,
        bitrate: i64,
        gop: i32,
        kind: u8,
        kind_name: *const c_char,
        out_producer: *mut *mut c_void,
    ) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<BroadcastProducer>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let mut config = moq_video::encode::Config::new(width, height, framerate);
        config.codec = match codec {
            0 => moq_video::encode::Codec::H264,
            1 => moq_video::encode::Codec::H265,
            _ => return fail("unknown video codec"),
        };
        config.kind = match kind {
            0 => moq_video::encode::Kind::Auto,
            1 => moq_video::encode::Kind::Hardware,
            2 => moq_video::encode::Kind::Software,
            3 => match crate::ffi::read_cstr(kind_name) {
                Ok(name) => moq_video::encode::Kind::Named(name.to_string()),
                Err(e) => return e,
            },
            _ => return fail("unknown encoder kind"),
        };
        config.bitrate = if bitrate >= 0 { Some(bitrate as u64) } else { None };
        if gop >= 0 {
            config.gop = gop as u32;
        }

        let rendition = match block_on(config.probe()) {
            Ok(v) => v,
            Err(err) => return map_err(err),
        };
        let encoder = match block_on(moq_video::encode::Sink::open(&config)) {
            Ok(v) => v,
            Err(err) => return map_err(err),
        };
        let track_name = match read_cstr_opt(track) {
            Ok(v) => v.map(str::to_string),
            Err(e) => return e,
        };

        match producer.with_state(|state| match track_name.clone() {
            Some(name) => {
                let track = state
                    .broadcast
                    .create_track(name, state.catalog.track_info())
                    .map_err(map_err)?;
                moq_video::encode::Producer::with_track(track, state.catalog.clone(), rendition.clone()).map_err(map_err)
            }
            None => moq_video::encode::Producer::new(state.broadcast.clone(), state.catalog.clone(), rendition.clone())
                .map_err(map_err),
        }) {
            Ok(video_producer) => write_out(
                out_producer,
                into_raw(Handle {
                    inner: Mutex::new(Some(VideoProducer {
                        encoder,
                        producer: video_producer,
                        format,
                        size: config.size(),
                    })),
                }),
            ),
            Err(e) => e,
        }
    }

    pub unsafe fn producer_free(handle: *mut c_void) {
        drop_raw::<Handle>(handle);
    }

    pub unsafe fn producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<Handle>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.as_ref() else {
            return closed();
        };
        match to_cstring(inner.producer.demand().name().to_string()) {
            Ok(ptr) => write_out(out_name, ptr),
            Err(e) => e,
        }
    }

    pub unsafe fn producer_write(
        handle: *mut c_void,
        payload: *const u8,
        payload_len: usize,
        timestamp_us: u64,
    ) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<Handle>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let bytes = if payload.is_null() || payload_len == 0 {
            Vec::new()
        } else {
            std::slice::from_raw_parts(payload, payload_len).to_vec()
        };
        let mut guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.as_mut() else {
            return closed();
        };
        let surface = match inner.format {
            0 => match moq_video::I420::new(inner.size.width, inner.size.height, bytes) {
                Ok(s) => moq_video::Surface::I420(s),
                Err(err) => return map_err(err),
            },
            1 => match moq_video::Surface::rgba(&bytes, inner.size) {
                Ok(s) => s,
                Err(err) => return map_err(err),
            },
            _ => return fail("unknown pixel format"),
        };
        let timestamp = match moq_net::Timestamp::from_micros(timestamp_us) {
            Ok(t) => t,
            Err(err) => return map_err(err),
        };
        let frame = moq_video::Frame::new(surface, timestamp);
        let encoded = match block_on(inner.encoder.encode(frame)) {
            Ok(v) => v,
            Err(err) => return map_err(err),
        };
        inner.producer.publish(&encoded).map(|_| MOQ_OK).unwrap_or_else(map_err)
    }

    pub unsafe fn producer_cut(handle: *mut c_void) -> i32 {
        clear_error();
        let producer = match as_ref::<Handle>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let mut guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.as_mut() else {
            return closed();
        };
        inner.encoder.keyframe();
        MOQ_OK
    }

    pub unsafe fn producer_set_bitrate(handle: *mut c_void, bitrate: u64) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<Handle>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let mut guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.as_mut() else {
            return closed();
        };
        block_on(inner.encoder.set_bitrate(bitrate))
            .map(|_| MOQ_OK)
            .unwrap_or_else(map_err)
    }

    pub unsafe fn producer_finish(handle: *mut c_void) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<Handle>(handle) {
            Ok(p) => p,
            Err(e) => return e,
        };
        let mut guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.take() else {
            return closed();
        };
        let VideoProducer {
            encoder,
            mut producer,
            ..
        } = inner;
        match block_on(encoder.finish()).and_then(|encoded| producer.publish(&encoded)) {
            Ok(()) => producer.finish().map(|_| MOQ_OK).unwrap_or_else(map_err),
            Err(err) => {
                producer.abort(moq_net::Error::Transport(err.to_string()));
                map_err(err)
            }
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_video(
    handle: *const c_void,
    format: u8,
    width: u32,
    height: u32,
    framerate: u32,
    codec: u8,
    track: *const c_char,
    bitrate: i64,
    gop: i32,
    kind: u8,
    kind_name: *const c_char,
    out_producer: *mut *mut c_void,
) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::publish_video(
            handle, format, width, height, framerate, codec, track, bitrate, gop, kind, kind_name, out_producer,
        );
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = (
            handle, format, width, height, framerate, codec, track, bitrate, gop, kind, kind_name, out_producer,
        );
        clear_error();
        not_supported("video")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_free(handle: *mut c_void) {
    #[cfg(feature = "video")]
    {
        imp::producer_free(handle);
        return;
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = handle;
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::producer_name(handle, out_name);
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = (handle, out_name);
        clear_error();
        not_supported("video")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_write(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::producer_write(handle, payload, payload_len, timestamp_us);
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = (handle, payload, payload_len, timestamp_us);
        clear_error();
        not_supported("video")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_cut(handle: *mut c_void) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::producer_cut(handle);
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = handle;
        clear_error();
        not_supported("video")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_set_bitrate(handle: *mut c_void, bitrate: u64) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::producer_set_bitrate(handle, bitrate);
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = (handle, bitrate);
        clear_error();
        not_supported("video")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_video_producer_finish(handle: *mut c_void) -> i32 {
    #[cfg(feature = "video")]
    {
        return imp::producer_finish(handle);
    }
    #[cfg(not(feature = "video"))]
    {
        let _ = handle;
        clear_error();
        not_supported("video")
    }
}
