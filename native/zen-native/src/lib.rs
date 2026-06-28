//! C ABI surface for the native Zen engine, plus a counting global allocator
//! that lets the managed side measure native heap consumption (which the .NET
//! GC metrics are blind to).

mod eval;
mod fns;
mod lexer;
mod parser;
mod value;

use std::alloc::{self, GlobalAlloc, Layout, System};
use std::collections::BTreeMap;
use std::ffi::c_char;
use std::sync::atomic::{AtomicU64, Ordering};

use eval::Evaluator;
use parser::{Node, Parser};
use value::Value;

// ---------------------------------------------------------------------------
// Counting global allocator
// ---------------------------------------------------------------------------

static ALLOC_BYTES: AtomicU64 = AtomicU64::new(0);
static DEALLOC_BYTES: AtomicU64 = AtomicU64::new(0);
static ALLOC_COUNT: AtomicU64 = AtomicU64::new(0);
static DEALLOC_COUNT: AtomicU64 = AtomicU64::new(0);

struct Counting;

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let ptr = System.alloc(layout);
        if !ptr.is_null() {
            ALLOC_BYTES.fetch_add(layout.size() as u64, Ordering::Relaxed);
            ALLOC_COUNT.fetch_add(1, Ordering::Relaxed);
        }
        ptr
    }

    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        DEALLOC_BYTES.fetch_add(layout.size() as u64, Ordering::Relaxed);
        DEALLOC_COUNT.fetch_add(1, Ordering::Relaxed);
        System.dealloc(ptr, layout);
    }

    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        let nptr = System.realloc(ptr, layout, new_size);
        if !nptr.is_null() {
            // account the old size as freed and the new size as allocated
            DEALLOC_BYTES.fetch_add(layout.size() as u64, Ordering::Relaxed);
            ALLOC_BYTES.fetch_add(new_size as u64, Ordering::Relaxed);
        }
        nptr
    }
}

#[global_allocator]
static GLOBAL: Counting = Counting;

// ---------------------------------------------------------------------------
// Opaque handles
// ---------------------------------------------------------------------------

pub struct CompiledExpr {
    root: Node,
}

fn box_handle<T>(value: T) -> usize {
    Box::into_raw(Box::new(value)) as usize
}

unsafe fn deref_handle<T>(handle: usize) -> &'static T {
    assert!(handle != 0, "null handle");
    &*(handle as *const T)
}

unsafe fn drop_handle<T>(handle: usize) {
    if handle != 0 {
        drop(Box::from_raw(handle as *mut T));
    }
}

// ---------------------------------------------------------------------------
// JSON glue
// ---------------------------------------------------------------------------

fn json_to_value(v: &serde_json::Value) -> Value {
    match v {
        serde_json::Value::Null => Value::Null,
        serde_json::Value::Bool(b) => Value::Bool(*b),
        serde_json::Value::Number(n) => Value::Num(n.as_f64().unwrap_or(0.0)),
        serde_json::Value::String(s) => Value::str(s.clone()),
        serde_json::Value::Array(a) => Value::arr(a.iter().map(json_to_value).collect()),
        serde_json::Value::Object(o) => {
            let mut m = BTreeMap::new();
            for (k, val) in o.iter() { m.insert(k.clone(), json_to_value(val)); }
            Value::obj(m)
        }
    }
}

fn write_json(v: &Value, out: &mut String) {
    match v {
        Value::Null => out.push_str("null"),
        Value::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
        Value::Num(n) => {
            if n.is_finite() {
                out.push_str(&n.to_string());
            } else {
                out.push_str("null");
            }
        }
        Value::Str(s) => {
            out.push('"');
            escape_json(s, out);
            out.push('"');
        }
        Value::Arr(a) => {
            out.push('[');
            for (i, e) in a.iter().enumerate() {
                if i > 0 { out.push(','); }
                write_json(e, out);
            }
            out.push(']');
        }
        Value::Obj(o) => {
            out.push('{');
            for (i, (k, val)) in o.iter().enumerate() {
                if i > 0 { out.push(','); }
                out.push('"');
                escape_json(k, out);
                out.push('"');
                out.push(':');
                write_json(val, out);
            }
            out.push('}');
        }
    }
}

fn escape_json(s: &str, out: &mut String) {
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            '\u{0008}' => out.push_str("\\b"),
            '\u{000C}' => out.push_str("\\f"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
}

fn malloc_bytes(s: &str) -> (*mut c_char, usize) {
    let bytes = s.as_bytes();
    let len = bytes.len();
    let cap = len.max(1);
    let layout = Layout::from_size_align(cap, 1).unwrap();
    unsafe {
        let ptr = alloc::alloc(layout);
        if ptr.is_null() { return (std::ptr::null_mut(), 0); }
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), ptr, len);
        (ptr as *mut c_char, len)
    }
}

// ---------------------------------------------------------------------------
// C ABI
// ---------------------------------------------------------------------------

/// Returns a compiled-expression handle (0 on error, with *err set).
#[no_mangle]
pub extern "C" fn zen_compile(src: *const c_char, len: usize, err: *mut *mut c_char) -> usize {
    let source = unsafe { std::str::from_utf8_unchecked(std::slice::from_raw_parts(src as *const u8, len)) };
    match Parser::parse(source) {
        Ok(root) => box_handle(CompiledExpr { root }),
        Err(e) => {
            unsafe { let (p, _) = malloc_bytes(&e); *err = p; }
            0
        }
    }
}

#[no_mangle]
pub extern "C" fn zen_expr_free(handle: usize) {
    unsafe { drop_handle::<CompiledExpr>(handle); }
}

/// Parse a JSON context into an opaque context handle (0 on error).
#[no_mangle]
pub extern "C" fn zen_ctx_parse(json: *const c_char, len: usize, err: *mut *mut c_char) -> usize {
    let bytes = unsafe { std::slice::from_raw_parts(json as *const u8, len) };
    match serde_json::from_slice::<serde_json::Value>(bytes) {
        Ok(v) => box_handle(json_to_value(&v)),
        Err(e) => {
            unsafe { let (p, _) = malloc_bytes(&e.to_string()); *err = p; }
            0
        }
    }
}

#[no_mangle]
pub extern "C" fn zen_ctx_free(handle: usize) {
    unsafe { drop_handle::<Value>(handle); }
}

/// Evaluate a compiled expression against a pre-parsed context handle.
/// On success sets *out to a malloc'd UTF-8 JSON result (caller frees with zen_free).
#[no_mangle]
pub extern "C" fn zen_eval_ctx(
    expr: usize,
    ctx: usize,
    out: *mut *mut c_char,
    out_len: *mut usize,
    err: *mut *mut c_char,
) -> i32 {
    let compiled = unsafe { deref_handle::<CompiledExpr>(expr) };
    let context = unsafe { deref_handle::<Value>(ctx) };
    let mut ev = Evaluator::new();
    match ev.evaluate(&compiled.root, context.clone()) {
        Ok(result) => {
            let mut buf = String::new();
            write_json(&result, &mut buf);
            let (ptr, n) = malloc_bytes(&buf);
            unsafe {
                *out = ptr;
                *out_len = n;
            }
            0
        }
        Err(e) => {
            unsafe { let (p, _) = malloc_bytes(&e); *err = p; }
            1
        }
    }
}

/// Evaluate a compiled expression against a JSON context string (parse + eval).
#[no_mangle]
pub extern "C" fn zen_eval_json(
    expr: usize,
    json: *const c_char,
    len: usize,
    out: *mut *mut c_char,
    out_len: *mut usize,
    err: *mut *mut c_char,
) -> i32 {
    let bytes = unsafe { std::slice::from_raw_parts(json as *const u8, len) };
    let parsed = match serde_json::from_slice::<serde_json::Value>(bytes) {
        Ok(v) => json_to_value(&v),
        Err(e) => {
            unsafe { let (p, _) = malloc_bytes(&e.to_string()); *err = p; }
            return 1;
        }
    };
    let compiled = unsafe { deref_handle::<CompiledExpr>(expr) };
    let mut ev = Evaluator::new();
    match ev.evaluate(&compiled.root, parsed) {
        Ok(result) => {
            let mut buf = String::new();
            write_json(&result, &mut buf);
            let (ptr, n) = malloc_bytes(&buf);
            unsafe {
                *out = ptr;
                *out_len = n;
            }
            0
        }
        Err(e) => {
            unsafe { let (p, _) = malloc_bytes(&e); *err = p; }
            1
        }
    }
}

/// Free a buffer returned by the engine (result JSON or error message).
#[no_mangle]
pub extern "C" fn zen_free(ptr: *mut c_char, len: usize) {
    if ptr.is_null() { return; }
    let layout = Layout::from_size_align(len.max(1), 1).unwrap();
    unsafe { alloc::dealloc(ptr as *mut u8, layout); }
}

/// Trivial function used to isolate raw P/Invoke call overhead.
#[no_mangle]
pub extern "C" fn zen_add(a: f64, b: f64) -> f64 {
    a + b
}

// ---------------------------------------------------------------------------
// Memory stats (from the counting allocator)
// ---------------------------------------------------------------------------

#[no_mangle]
pub extern "C" fn zen_mem_allocated_bytes() -> u64 { ALLOC_BYTES.load(Ordering::Relaxed) }

#[no_mangle]
pub extern "C" fn zen_mem_deallocated_bytes() -> u64 { DEALLOC_BYTES.load(Ordering::Relaxed) }

#[no_mangle]
pub extern "C" fn zen_mem_in_use_bytes() -> u64 {
    ALLOC_BYTES.load(Ordering::Relaxed).saturating_sub(DEALLOC_BYTES.load(Ordering::Relaxed))
}

#[no_mangle]
pub extern "C" fn zen_mem_alloc_count() -> u64 { ALLOC_COUNT.load(Ordering::Relaxed) }

#[no_mangle]
pub extern "C" fn zen_mem_dealloc_count() -> u64 { DEALLOC_COUNT.load(Ordering::Relaxed) }

/// Reset the counters (does not free memory; used to take deltas in benchmarks).
#[no_mangle]
pub extern "C" fn zen_mem_reset() {
    ALLOC_BYTES.store(0, Ordering::Relaxed);
    DEALLOC_BYTES.store(0, Ordering::Relaxed);
    ALLOC_COUNT.store(0, Ordering::Relaxed);
    DEALLOC_COUNT.store(0, Ordering::Relaxed);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    fn eval_str(src: &str, ctx_json: &str) -> Value {
        let root = Parser::parse(src).unwrap();
        let ctx = json_to_value(&serde_json::from_str::<serde_json::Value>(ctx_json).unwrap());
        let mut ev = Evaluator::new();
        ev.evaluate(&root, ctx).unwrap()
    }

    #[test]
    fn basic_arithmetic() {
        assert_eq!(eval_str("1 + 2 * 3", "{}"), Value::Num(7.0));
        assert_eq!(eval_str("(1 + 2) * 3", "{}"), Value::Num(9.0));
        assert_eq!(eval_str("2 ^ 10", "{}"), Value::Num(1024.0));
        assert_eq!(eval_str("-2 ^ 2", "{}"), Value::Num(4.0)); // unary tighter than ^
    }

    #[test]
    fn context_access() {
        assert_eq!(eval_str("a + b", r#"{"a":2,"b":3}"#), Value::Num(5.0));
        assert_eq!(eval_str("order.items[0].price", r#"{"order":{"items":[{"price":9.99}]}}"#), Value::Num(9.99));
    }

    #[test]
    fn closures() {
        assert_eq!(eval_str("sum(map([1,2,3], # * 2))", "{}"), Value::Num(12.0));
        assert_eq!(eval_str("all([2,4,6], # > 0)", "{}"), Value::Bool(true));
    }

    #[test]
    fn membership_and_ranges() {
        assert_eq!(eval_str("x in [1,2,3]", r#"{"x":2}"#), Value::Bool(true));
        assert_eq!(eval_str("x in [1..10]", r#"{"x":5}"#), Value::Bool(true));
        assert_eq!(eval_str("x not in [1..10]", r#"{"x":50}"#), Value::Bool(true));
    }
}
