use std::collections::BTreeMap;
use std::rc::Rc;

/// A first-class Zen value. Mirrors `ZenValue` in the managed implementation
/// byte-for-byte: all numbers are f64, objects are key/value maps.
///
/// Containers (strings, arrays, objects) are reference-counted so that cloning
/// a `Value` is cheap. This matters for member access: `a.b.c` must not deep-
/// copy intermediate objects. (In the managed world, `ZenValue` is a struct that
/// already holds object/array fields *by reference*, so cloning is free there.)
#[derive(Clone, Debug, PartialEq)]
pub enum Value {
    Null,
    Bool(bool),
    Num(f64),
    Str(Rc<String>),
    Arr(Rc<Vec<Value>>),
    Obj(Rc<BTreeMap<String, Value>>),
}

impl Value {
    pub fn str<S: Into<String>>(s: S) -> Self { Value::Str(Rc::new(s.into())) }
    pub fn arr(v: Vec<Value>) -> Self { Value::Arr(Rc::new(v)) }
    pub fn obj(m: BTreeMap<String, Value>) -> Self { Value::Obj(Rc::new(m)) }

    pub fn is_null(&self) -> bool { matches!(self, Value::Null) }
    pub fn is_truthy(&self) -> bool {
        match self {
            Value::Null => false,
            Value::Bool(b) => *b,
            Value::Num(n) => *n != 0.0,
            Value::Str(s) => !s.is_empty(),
            Value::Arr(a) => !a.is_empty(),
            Value::Obj(o) => !o.is_empty(),
        }
    }

    /// Canonical number coercion, matching the managed `CanonNumber`.
    pub fn canon_number(&self) -> Result<f64, String> {
        match self {
            Value::Num(n) => Ok(*n),
            Value::Bool(true) => Ok(1.0),
            Value::Bool(false) => Ok(0.0),
            Value::Null => Ok(0.0),
            Value::Str(s) => s.parse::<f64>().map_err(|_| format!("Cannot convert string '{}' to number", s)),
            other => Err(format!("Cannot convert {:?} to number", other)),
        }
    }

    pub fn try_number(&self) -> Option<f64> {
        match self {
            Value::Num(n) => Some(*n),
            Value::Bool(b) => Some(if *b { 1.0 } else { 0.0 }),
            Value::Str(s) => s.parse::<f64>().ok(),
            _ => None,
        }
    }
}

/// Deep structural equality, matching the managed `DeepEqual`.
pub fn deep_equal(a: &Value, b: &Value) -> bool {
    match (a, b) {
        (Value::Null, Value::Null) => true,
        (Value::Bool(x), Value::Bool(y)) => x == y,
        (Value::Num(x), Value::Num(y)) => x == y,
        (Value::Str(x), Value::Str(y)) => x == y,
        (Value::Arr(x), Value::Arr(y)) => x.len() == y.len() && x.iter().zip(y.iter()).all(|(p, q)| deep_equal(p, q)),
        (Value::Obj(x), Value::Obj(y)) => {
            x.len() == y.len() && x.iter().all(|(k, v)| y.get(k).map(|w| deep_equal(v, w)).unwrap_or(false))
        }
        _ => false,
    }
}

pub fn stringify(v: &Value) -> String {
    match v {
        Value::Null => "null".to_string(),
        Value::Bool(true) => "true".to_string(),
        Value::Bool(false) => "false".to_string(),
        Value::Str(s) => s.as_str().to_owned(),
        Value::Num(n) => number_to_string(*n),
        other => format!("{:?}", other),
    }
}

pub fn number_to_string(v: f64) -> String {
    if v.is_nan() { return "NaN".to_string(); }
    if v.is_infinite() { return if v > 0.0 { "Infinity".into() } else { "-Infinity".into() }; }
    if v == 0.0 { return "0".to_string(); }
    let t = v.trunc();
    if t == v && v.abs() < 1e15 {
        return format!("{}", v as i64);
    }
    // Shortest round-tripping representation (Rust default Display for f64).
    format!("{}", v)
}
