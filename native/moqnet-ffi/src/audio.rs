use std::os::raw::{c_char, c_void};

#[cfg(not(feature = "audio"))]
use crate::error::not_supported;
#[cfg(not(feature = "audio"))]
use crate::ffi::clear_error;

#[cfg(feature = "audio")]
mod imp {
    use std::os::raw::{c_char, c_void};
    use std::sync::Mutex;
    use std::time::Duration;

    use crate::cancel;
    use crate::consumer::BroadcastConsumer;
    use crate::error::{closed, map_err};
    use crate::ffi::{as_ref, clear_error, drop_raw, fail, into_raw, read_cstr, to_cstring, write_out, MOQ_NONE, MOQ_OK};
    use crate::producer::BroadcastProducer;
    use crate::runtime;

    pub struct AudioProducer {
        inner: Mutex<Option<moq_audio::encode::Producer<moq_mux::catalog::hang::Extra>>>,
    }

    pub struct AudioConsumer {
        inner: Mutex<moq_audio::decode::Consumer>,
        cancelled: cancel::Flag,
    }

    fn format_from_u8(v: u8) -> Result<moq_audio::Format, i32> {
        Ok(match v {
            0 => moq_audio::Format::U8,
            1 => moq_audio::Format::S16,
            2 => moq_audio::Format::S32,
            3 => moq_audio::Format::F32,
            4 => moq_audio::Format::U8Planar,
            5 => moq_audio::Format::S16Planar,
            6 => moq_audio::Format::S32Planar,
            7 => moq_audio::Format::F32Planar,
            _ => return Err(fail("unknown audio format")),
        })
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

    pub unsafe fn publish_audio(
        handle: *const c_void,
        name: *const c_char,
        format: u8,
        sample_rate: u32,
        channels: u32,
        bitrate: i64,
        frame_duration_ms: u32,
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
        let fmt = match format_from_u8(format) {
            Ok(v) => v,
            Err(e) => return e,
        };
        let layout = match moq_audio::Layout::from_channels(channels) {
            Ok(v) => v,
            Err(err) => return map_err(err),
        };
        let mut input = moq_audio::encode::Input::new(sample_rate, layout);
        input.format = fmt;
        let mut options = moq_audio::encode::Options::default();
        options.track = Some(name);
        options.settings = moq_audio::encode::Settings::from_input(moq_audio::encode::Codec::Opus, &input);
        if bitrate >= 0 {
            options.settings.bitrate = Some(moq_net::bandwidth::Rate::from_bps(bitrate as u64));
        }
        options.settings.frame_duration = Duration::from_millis(frame_duration_ms.into());

        match producer.with_state(|state| {
            moq_audio::encode::Producer::new(&mut state.broadcast, state.catalog.clone(), input, &options)
                .map_err(map_err)
                .map(|p| {
                    into_raw(AudioProducer {
                        inner: Mutex::new(Some(p)),
                    })
                })
        }) {
            Ok(ptr) => write_out(out_producer, ptr),
            Err(e) => e,
        }
    }

    pub unsafe fn producer_free(handle: *mut c_void) {
        drop_raw::<AudioProducer>(handle);
    }

    pub unsafe fn producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<AudioProducer>(handle) {
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
        match to_cstring(inner.track_name().to_string()) {
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
        let producer = match as_ref::<AudioProducer>(handle) {
            Ok(p) => p,
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
        let frame = moq_audio::Frame::new(bytes.into(), timestamp);
        let mut guard = match producer.inner.lock() {
            Ok(g) => g,
            Err(_) => return fail("lock poisoned"),
        };
        let Some(inner) = guard.as_mut() else {
            return closed();
        };
        inner.write(&frame).map(|_| MOQ_OK).unwrap_or_else(map_err)
    }

    pub unsafe fn producer_finish(handle: *mut c_void) -> i32 {
        clear_error();
        let _guard = runtime::enter();
        let producer = match as_ref::<AudioProducer>(handle) {
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

    pub unsafe fn subscribe_audio(
        handle: *const c_void,
        name: *const c_char,
        codec: *const c_char,
        sample_rate: u32,
        channel_count: u32,
        format: u8,
        latency_max_ms: i64,
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
        let codec = match read_cstr(codec) {
            Ok(v) => v,
            Err(e) => return e,
        };
        let parsed: hang::catalog::AudioCodec = match codec.parse() {
            Ok(v) => v,
            Err(_) => return fail("unsupported"),
        };
        if !matches!(
            &parsed,
            hang::catalog::AudioCodec::Opus | hang::catalog::AudioCodec::AAC(_)
        ) {
            return fail("unsupported");
        }
        let cfg = hang::catalog::AudioConfig::new(parsed, sample_rate, channel_count);
        let fmt = match format_from_u8(format) {
            Ok(v) => v,
            Err(e) => return e,
        };
        let mut options = moq_audio::decode::Options::default();
        options.output.format = fmt;
        if latency_max_ms >= 0 {
            options.max_age = Duration::from_millis(latency_max_ms as u64);
        }

        match runtime::block_on(moq_audio::decode::Consumer::new(consumer.inner(), &cfg, name, options)) {
            Ok(inner) => write_out(
                out_consumer,
                into_raw(AudioConsumer {
                    inner: Mutex::new(inner),
                    cancelled: cancel::new(),
                }),
            ),
            Err(err) => map_err(err),
        }
    }

    pub unsafe fn consumer_free(handle: *mut c_void) {
        drop_raw::<AudioConsumer>(handle);
    }

    pub unsafe fn consumer_cancel(handle: *mut c_void) -> i32 {
        clear_error();
        let consumer = match as_ref::<AudioConsumer>(handle) {
            Ok(c) => c,
            Err(e) => return e,
        };
        cancel::set(&consumer.cancelled);
        MOQ_OK
    }

    pub unsafe fn consumer_next(
        handle: *mut c_void,
        out_payload: *mut *mut u8,
        out_len: *mut usize,
        out_timestamp_us: *mut u64,
    ) -> i32 {
        clear_error();
        let consumer = match as_ref::<AudioConsumer>(handle) {
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
        let next = match runtime::block_on(cancel::or_set(&consumer.cancelled, inner.read())) {
            None => return fail("cancelled"),
            Some(value) => value,
        };
        match next {
            Ok(Some(frame)) => {
                let ts = u64::try_from(frame.timestamp.as_micros()).unwrap_or(u64::MAX);
                if !out_timestamp_us.is_null() {
                    *out_timestamp_us = ts;
                }
                write_bytes(out_payload, out_len, frame.data.to_vec())
            }
            Ok(None) => MOQ_NONE,
            Err(err) => map_err(err),
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_producer_publish_audio(
    handle: *const c_void,
    name: *const c_char,
    format: u8,
    sample_rate: u32,
    channels: u32,
    bitrate: i64,
    frame_duration_ms: u32,
    out_producer: *mut *mut c_void,
) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::publish_audio(
            handle,
            name,
            format,
            sample_rate,
            channels,
            bitrate,
            frame_duration_ms,
            out_producer,
        );
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = (handle, name, format, sample_rate, channels, bitrate, frame_duration_ms, out_producer);
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_producer_free(handle: *mut c_void) {
    #[cfg(feature = "audio")]
    {
        imp::producer_free(handle);
        return;
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = handle;
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_producer_name(handle: *const c_void, out_name: *mut *mut c_char) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::producer_name(handle, out_name);
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = (handle, out_name);
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_producer_write(
    handle: *mut c_void,
    payload: *const u8,
    payload_len: usize,
    timestamp_us: u64,
) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::producer_write(handle, payload, payload_len, timestamp_us);
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = (handle, payload, payload_len, timestamp_us);
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_producer_finish(handle: *mut c_void) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::producer_finish(handle);
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = handle;
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_broadcast_consumer_subscribe_audio(
    handle: *const c_void,
    name: *const c_char,
    codec: *const c_char,
    sample_rate: u32,
    channel_count: u32,
    format: u8,
    latency_max_ms: i64,
    out_consumer: *mut *mut c_void,
) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::subscribe_audio(
            handle,
            name,
            codec,
            sample_rate,
            channel_count,
            format,
            latency_max_ms,
            out_consumer,
        );
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = (handle, name, codec, sample_rate, channel_count, format, latency_max_ms, out_consumer);
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_consumer_free(handle: *mut c_void) {
    #[cfg(feature = "audio")]
    {
        imp::consumer_free(handle);
        return;
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = handle;
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_consumer_cancel(handle: *mut c_void) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::consumer_cancel(handle);
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = handle;
        clear_error();
        not_supported("audio")
    }
}

#[no_mangle]
pub unsafe extern "C" fn moq_audio_consumer_next(
    handle: *mut c_void,
    out_payload: *mut *mut u8,
    out_len: *mut usize,
    out_timestamp_us: *mut u64,
) -> i32 {
    #[cfg(feature = "audio")]
    {
        return imp::consumer_next(handle, out_payload, out_len, out_timestamp_us);
    }
    #[cfg(not(feature = "audio"))]
    {
        let _ = (handle, out_payload, out_len, out_timestamp_us);
        clear_error();
        not_supported("audio")
    }
}
