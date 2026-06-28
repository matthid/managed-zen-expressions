//! AST evaluator mirroring `Zen.Managed.Evaluator`.

use std::collections::BTreeMap;

use crate::fns;
use crate::parser::{BinOp, CmpOp, Node};
use crate::value::{deep_equal, Value};

pub struct Evaluator {
    ctx: Value,
    stack: Vec<Value>,
}

impl Default for Evaluator {
    fn default() -> Self { Self::new() }
}

impl Evaluator {
    pub fn new() -> Self { Self { ctx: Value::Null, stack: Vec::with_capacity(8) } }

    pub fn evaluate(&mut self, root: &Node, ctx: Value) -> Result<Value, String> {
        self.ctx = ctx;
        self.stack.clear();
        self.eval(root)
    }

    pub fn current(&self) -> Value {
        self.stack.last().cloned().unwrap_or(Value::Null)
    }

    pub fn eval_with_element(&mut self, body: &Node, el: Value) -> Result<Value, String> {
        self.stack.push(el);
        let r = self.eval(body);
        self.stack.pop();
        r
    }

    pub fn eval_arg(&mut self, args: &[Node], i: usize) -> Result<Value, String> {
        if i < args.len() { self.eval(&args[i]) } else { Ok(Value::Null) }
    }

    fn eval(&mut self, n: &Node) -> Result<Value, String> {
        match n {
            Node::Literal(v) => Ok(v.clone()),
            Node::Ident(name) => Ok(self.resolve_ident(name)),
            Node::Current => Ok(self.current()),

            Node::Array(items) => {
                let mut out = Vec::with_capacity(items.len());
                for it in items.iter() { out.push(self.eval(it)?); }
                Ok(Value::arr(out))
            }

            Node::Object { keys, values } => {
                let mut map = BTreeMap::new();
                for (k, v) in keys.iter().zip(values.iter()) {
                    map.insert(k.clone(), self.eval(v)?);
                }
                Ok(Value::obj(map))
            }

            Node::Unary { plus, operand } => {
                let v = self.eval(operand)?;
                if *plus { return Ok(v); }
                let d = v.canon_number()?;
                Ok(Value::Num(-d))
            }

            Node::Binary { op, l, r } => self.eval_binary(*op, l, r),

            Node::Logical { and, l, r } => {
                let lb = self.eval(l)?.is_truthy();
                let truthy = if *and { lb && self.eval(r)?.is_truthy() } else { lb || self.eval(r)?.is_truthy() };
                Ok(Value::Bool(truthy))
            }

            Node::Compare { op, l, r } => {
                let lv = self.eval(l)?;
                let rv = self.eval(r)?;
                Ok(Value::Bool(self.eval_compare(*op, &lv, &rv)?))
            }

            Node::Ternary { cond, then_b, else_b } => {
                if self.eval(cond)?.is_truthy() { self.eval(then_b) } else { self.eval(else_b) }
            }

            Node::Coalesce { l, r } => {
                let lv = self.eval(l)?;
                if lv.is_null() { self.eval(r) } else { Ok(lv) }
            }

            Node::Member { obj, name } => {
                let o = self.eval(obj)?;
                Ok(Self::member(o, name))
            }

            Node::Index { obj, index } => {
                let o = self.eval(obj)?;
                let k = self.eval(index)?;
                Ok(self.index(o, k))
            }

            Node::Call { name, args } => fns::call(name, self, args),

            Node::In { value, range, negated } => {
                let v = self.eval(value)?;
                let result = self.eval_in(v, range)?;
                Ok(Value::Bool(if *negated { !result } else { result }))
            }

            Node::Range { .. } => Err("Range node must appear on the right of 'in'".to_string()),
        }
    }

    fn resolve_ident(&self, name: &str) -> Value {
        if name == "$" { return self.ctx.clone(); }
        match &self.ctx {
            Value::Obj(map) => map.get(name).cloned().unwrap_or(Value::Null),
            _ => Value::Null,
        }
    }

    fn member(obj: Value, name: &str) -> Value {
        match obj {
            Value::Obj(map) => map.get(name).cloned().unwrap_or(Value::Null),
            Value::Null => Value::Null,
            _ => Value::Null,
        }
    }

    fn index(&mut self, obj: Value, key: Value) -> Value {
        match obj {
            Value::Null => Value::Null,
            Value::Arr(arr) => {
                let k = match key.canon_number() { Ok(k) => k, Err(_) => return Value::Null };
                let mut idx = k as isize;
                let len = arr.len() as isize;
                if idx < 0 { idx += len; }
                if idx < 0 || idx >= len { return Value::Null; }
                arr[idx as usize].clone()
            }
            Value::Obj(map) => {
                let s = match &key { Value::Str(s) => s.as_str().to_owned(), other => crate::value::stringify(other) };
                map.get(&s).cloned().unwrap_or(Value::Null)
            }
            _ => Value::Null,
        }
    }

    fn eval_binary(&mut self, op: BinOp, l: &Node, r: &Node) -> Result<Value, String> {
        let lv = self.eval(l)?;
        let rv = self.eval(r)?;

        if matches!(op, BinOp::Add) && (matches!(lv, Value::Str(_)) || matches!(rv, Value::Str(_))) {
            return Ok(Value::str(format!("{}{}", crate::value::stringify(&lv), crate::value::stringify(&rv))));
        }

        let a = lv.canon_number()?;
        let b = rv.canon_number()?;
        let res = match op {
            BinOp::Add => a + b,
            BinOp::Sub => a - b,
            BinOp::Mul => a * b,
            BinOp::Div => a / b,
            BinOp::Mod => a % b,
            BinOp::Pow => a.powf(b),
        };
        Ok(Value::Num(res))
    }

    fn eval_compare(&mut self, op: CmpOp, l: &Value, r: &Value) -> Result<bool, String> {
        if matches!(op, CmpOp::Eq) { return Ok(deep_equal(l, r)); }
        if matches!(op, CmpOp::Ne) { return Ok(!deep_equal(l, r)); }
        let c = self.compare(l, r)?;
        Ok(match op {
            CmpOp::Lt => c < 0,
            CmpOp::Gt => c > 0,
            CmpOp::Le => c <= 0,
            CmpOp::Ge => c >= 0,
            _ => false,
        })
    }

    fn compare(&self, l: &Value, r: &Value) -> Result<i32, String> {
        if let (Value::Str(a), Value::Str(b)) = (l, r) {
            return Ok(match a.cmp(b) { std::cmp::Ordering::Less => -1, std::cmp::Ordering::Equal => 0, std::cmp::Ordering::Greater => 1 });
        }
        let a = l.canon_number()?;
        let b = r.canon_number()?;
        Ok(a.partial_cmp(&b).map(|o| match o { std::cmp::Ordering::Less => -1, std::cmp::Ordering::Equal => 0, std::cmp::Ordering::Greater => 1 }).unwrap_or(0))
    }

    fn eval_in(&mut self, value: Value, range: &Node) -> Result<bool, String> {
        match range {
            Node::Range { lo, hi, start_incl, end_incl } => {
                let lo_v = self.eval(lo)?.canon_number()?;
                let hi_v = self.eval(hi)?.canon_number()?;
                match value.try_number() {
                    Some(x) => {
                        let low_ok = if *start_incl { x >= lo_v } else { x > lo_v };
                        let high_ok = if *end_incl { x <= hi_v } else { x < hi_v };
                        Ok(low_ok && high_ok)
                    }
                    None => Ok(false),
                }
            }
            _ => {
                let coll = self.eval(range)?;
                match coll {
                    Value::Arr(arr) => Ok(arr.iter().any(|e| deep_equal(e, &value))),
                    Value::Obj(map) => Ok(match &value { Value::Str(s) => map.contains_key(s.as_str()), _ => false }),
                    Value::Str(hay) => {
                        if let Value::Str(needle) = &value { Ok(hay.contains(needle.as_str())) } else { Ok(false) }
                    }
                    other => Err(format!("Right-hand side of 'in' must be array/object/string, got {:?}", other)),
                }
            }
        }
    }
}
