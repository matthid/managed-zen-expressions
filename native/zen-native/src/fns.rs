//! Built-in functions mirroring `Zen.Managed.Functions`.

use crate::eval::Evaluator;
use crate::parser::Node;
use crate::value::{stringify, Value};

pub fn call(name: &str, ev: &mut Evaluator, args: &[Node]) -> Result<Value, String> {
    match name {
        "len" => {
            let v = ev.eval_arg(args, 0)?;
            match v {
                Value::Arr(a) => Ok(Value::Num(a.len() as f64)),
                Value::Str(s) => Ok(Value::Num(s.chars().count() as f64)),
                Value::Obj(o) => Ok(Value::Num(o.len() as f64)),
                _ => Err("len() expects an array, string or object".to_string()),
            }
        }
        "sum" => {
            let v = ev.eval_arg(args, 0)?;
            match v { Value::Arr(a) => Ok(Value::Num(a.iter().map(|e| num(e)).sum())), _ => Err("sum() expects an array".to_string()) }
        }
        "avg" => {
            let v = ev.eval_arg(args, 0)?;
            match v {
                Value::Arr(a) => {
                    if a.is_empty() { return Ok(Value::Num(0.0)); }
                    let s: f64 = a.iter().map(|e| num(e)).sum();
                    Ok(Value::Num(s / a.len() as f64))
                }
                _ => Err("avg() expects an array".to_string()),
            }
        }
        "min" => minmax(ev, args, false),
        "max" => minmax(ev, args, true),
        "count" => {
            let v = ev.eval_arg(args, 0)?;
            Ok(match v { Value::Arr(a) => Value::Num(a.len() as f64), _ => Value::Num(0.0) })
        }

        "abs" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).abs())),
        "floor" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).floor())),
        "ceil" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).ceil())),
        "round" => {
            let x = num(&ev.eval_arg(args, 0)?);
            if args.len() > 1 {
                let dp = num(&ev.eval_arg(args, 1)?) as i32;
                let m = 10f64.powi(dp);
                Ok(Value::Num((x * m).round() / m))
            } else {
                Ok(Value::Num(x.round()))
            }
        }
        "sqrt" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).sqrt())),
        "pow" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).powf(num(&ev.eval_arg(args, 1)?)))),
        "int" => Ok(Value::Num(num(&ev.eval_arg(args, 0)?).trunc())),

        "number" => {
            let v = ev.eval_arg(args, 0)?;
            Ok(Value::Num(num(&v)))
        }
        "string" => Ok(Value::Str(stringify(&ev.eval_arg(args, 0)?))),
        "boolean" => Ok(Value::Bool(ev.eval_arg(args, 0)?.is_truthy())),

        "upper" => Ok(Value::Str(str_arg(ev, args, 0).to_uppercase())),
        "lower" => Ok(Value::Str(str_arg(ev, args, 0).to_lowercase())),
        "trim" => Ok(Value::Str(str_arg(ev, args, 0).trim().to_string())),
        "concat" => {
            let mut s = String::new();
            for i in 0..args.len() { s.push_str(&stringify(&ev.eval_arg(args, i)?)); }
            Ok(Value::Str(s))
        }
        "contains" => Ok(Value::Bool(str_arg(ev, args, 0).contains(&str_arg(ev, args, 1)))),
        "startsWith" => Ok(Value::Bool(str_arg(ev, args, 0).starts_with(&str_arg(ev, args, 1)))),
        "endsWith" => Ok(Value::Bool(str_arg(ev, args, 0).ends_with(&str_arg(ev, args, 1)))),
        "indexOf" => {
            let h = str_arg(ev, args, 0);
            let n = str_arg(ev, args, 1);
            Ok(Value::Num(h.find(&n.as_str()).map_or(-1.0, |b| h[..b].chars().count() as f64)))
        }
        "substring" => {
            let s = str_arg(ev, args, 0);
            let mut start = num(&ev.eval_arg(args, 1)?) as isize;
            if start < 0 { start = 0; }
            let start = start as usize;
            if args.len() > 2 {
                let mut end = num(&ev.eval_arg(args, 2)?) as isize;
                if end < start as isize { end = start as isize; }
                let chars: Vec<char> = s.chars().collect();
                let end = (end as usize).min(chars.len());
                let out: String = chars[start.min(end)..end].iter().collect();
                Ok(Value::Str(out))
            } else {
                let chars: Vec<char> = s.chars().collect();
                if start >= chars.len() { Ok(Value::Str(String::new())) }
                else { Ok(Value::Str(chars[start..].iter().collect())) }
            }
        }
        "replace" => {
            let s = str_arg(ev, args, 0);
            let from = str_arg(ev, args, 1);
            let to = str_arg(ev, args, 2);
            Ok(Value::Str(s.replace(&from, &to)))
        }
        "split" => {
            let s = str_arg(ev, args, 0);
            let sep = str_arg(ev, args, 1);
            let parts: Vec<Value> = if sep.is_empty() {
                vec![Value::Str(s)]
            } else {
                s.split(sep.as_str()).map(|p| Value::Str(p.to_string())).collect()
            };
            Ok(Value::Arr(parts))
        }

        "map" => higher_order(ev, args, "map"),
        "filter" => higher_order(ev, args, "filter"),
        "some" => higher_order(ev, args, "some"),
        "all" => higher_order(ev, args, "all"),

        _ => Err(format!("Unknown function '{}'", name)),
    }
}

fn num(v: &Value) -> f64 {
    v.canon_number().unwrap_or(0.0)
}

fn str_arg(ev: &mut Evaluator, args: &[Node], i: usize) -> String {
    match ev.eval_arg(args, i) { Ok(v) => stringify(&v), Err(_) => String::new() }
}

fn minmax(ev: &mut Evaluator, args: &[Node], max: bool) -> Result<Value, String> {
    let v = ev.eval_arg(args, 0)?;
    match v {
        Value::Arr(a) => {
            if a.is_empty() { return Ok(Value::Null); }
            let mut best = num(&a[0]);
            for e in a.iter().skip(1) {
                let c = num(e);
                if (max && c > best) || (!max && c < best) { best = c; }
            }
            Ok(Value::Num(best))
        }
        _ => Err("min()/max() expects an array".to_string()),
    }
}

fn higher_order(ev: &mut Evaluator, args: &[Node], mode: &str) -> Result<Value, String> {
    let src = ev.eval_arg(args, 0)?;
    let body = args.get(1).ok_or_else(|| format!("{}() needs a body argument", mode))?;
    let arr = match src {
        Value::Arr(a) => a,
        _ => return Err(format!("{}() expects an array as the first argument", mode)),
    };

    match mode {
        "map" => {
            let mut out = Vec::with_capacity(arr.len());
            for e in arr.iter() { out.push(ev.eval_with_element(body, e.clone())?); }
            Ok(Value::Arr(out))
        }
        "filter" => {
            let mut out = Vec::new();
            for e in arr.iter() { if ev.eval_with_element(body, e.clone())?.is_truthy() { out.push(e.clone()); } }
            Ok(Value::Arr(out))
        }
        "some" => {
            for e in arr.iter() { if ev.eval_with_element(body, e.clone())?.is_truthy() { return Ok(Value::Bool(true)); } }
            Ok(Value::Bool(false))
        }
        "all" => {
            for e in arr.iter() { if !ev.eval_with_element(body, e.clone())?.is_truthy() { return Ok(Value::Bool(false)); } }
            Ok(Value::Bool(true))
        }
        _ => Err("Unknown higher-order mode".to_string()),
    }
}
