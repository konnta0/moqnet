use std::cell::RefCell;
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_void};
use std::ptr;

pub const MOQ_OK: i32 = 0;
pub const MOQ_NONE: i32 = 1;
pub const MOQ_ERR: i32 = -1;

thread_local! {
    static LAST_ERROR: RefCell<Option<CString>> = const { RefCell::new(None) };
}

pub fn set_error(msg: impl ToString) {
    LAST_ERROR.with(|e| {
        *e.borrow_mut() = CString::new(msg.to_string().replace('\0', "")).ok();
    });
}

pub fn clear_error() {
    LAST_ERROR.with(|e| {
        *e.borrow_mut() = None;
    });
}

pub fn fail(msg: impl ToString) -> i32 {
    set_error(msg);
    MOQ_ERR
}

pub fn to_cstring(s: impl Into<String>) -> Result<*mut c_char, i32> {
    CString::new(s.into())
        .map(|c| c.into_raw())
        .map_err(|_| fail("string contained NUL"))
}

pub unsafe fn read_cstr<'a>(ptr: *const c_char) -> Result<&'a str, i32> {
    if ptr.is_null() {
        return Err(fail("null string"));
    }
    CStr::from_ptr(ptr)
        .to_str()
        .map_err(|_| fail("string was not UTF-8"))
}

pub unsafe fn read_cstr_opt<'a>(ptr: *const c_char) -> Result<Option<&'a str>, i32> {
    if ptr.is_null() {
        Ok(None)
    } else {
        Ok(Some(read_cstr(ptr)?))
    }
}

pub fn into_raw<T>(value: T) -> *mut c_void {
    Box::into_raw(Box::new(value)) as *mut c_void
}

pub unsafe fn as_ref<'a, T>(ptr: *const c_void) -> Result<&'a T, i32> {
    if ptr.is_null() {
        Err(fail("null handle"))
    } else {
        Ok(&*(ptr as *const T))
    }
}

pub unsafe fn as_mut<'a, T>(ptr: *mut c_void) -> Result<&'a mut T, i32> {
    if ptr.is_null() {
        Err(fail("null handle"))
    } else {
        Ok(&mut *(ptr as *mut T))
    }
}

pub unsafe fn drop_raw<T>(ptr: *mut c_void) {
    if !ptr.is_null() {
        drop(Box::from_raw(ptr as *mut T));
    }
}

pub fn write_out<T>(out: *mut T, value: T) -> i32 {
    if out.is_null() {
        return fail("null out pointer");
    }
    unsafe {
        *out = value;
    }
    MOQ_OK
}

/// Returns the last error message for this thread, or NULL.
/// Valid until the next FFI call on the same thread.
#[no_mangle]
pub extern "C" fn moq_last_error() -> *const c_char {
    LAST_ERROR.with(|e| match e.borrow().as_ref() {
        Some(s) => s.as_ptr(),
        None => ptr::null(),
    })
}

/// Frees a string allocated by this library.
#[no_mangle]
pub unsafe extern "C" fn moq_string_free(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(CString::from_raw(ptr));
    }
}

/// Frees a byte buffer allocated by this library.
#[no_mangle]
pub unsafe extern "C" fn moq_buffer_free(ptr: *mut u8, len: usize) {
    if !ptr.is_null() && len > 0 {
        drop(Vec::from_raw_parts(ptr, len, len));
    }
}
