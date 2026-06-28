//! Pratt parser mirroring `Zen.Managed.Parser`. Same binding powers and
//! associativity so both implementations build structurally identical trees.

use crate::lexer::{Tok, Token};
use crate::value::Value;

#[derive(Debug, Clone, Copy)]
pub enum BinOp { Add, Sub, Mul, Div, Mod, Pow }

#[derive(Debug, Clone, Copy)]
pub enum CmpOp { Eq, Ne, Lt, Gt, Le, Ge }

#[derive(Debug)]
pub enum Node {
    Literal(Value),
    Ident(String),
    Current,
    Array(Vec<Node>),
    Object { keys: Vec<String>, values: Vec<Node> },
    Unary { plus: bool, operand: Box<Node> },
    Binary { op: BinOp, l: Box<Node>, r: Box<Node> },
    Logical { and: bool, l: Box<Node>, r: Box<Node> },
    Compare { op: CmpOp, l: Box<Node>, r: Box<Node> },
    Ternary { cond: Box<Node>, then_b: Box<Node>, else_b: Box<Node> },
    Coalesce { l: Box<Node>, r: Box<Node> },
    Member { obj: Box<Node>, name: String },
    Index { obj: Box<Node>, index: Box<Node> },
    Call { name: String, args: Vec<Node> },
    Range { lo: Box<Node>, hi: Box<Node>, start_incl: bool, end_incl: bool },
    In { value: Box<Node>, range: Box<Node>, negated: bool },
}

pub struct Parser {
    toks: Vec<Token>,
    pos: usize,
}

impl Parser {
    pub fn parse(src: &str) -> Result<Node, String> {
        let toks = crate::lexer::tokenize(src)?;
        let mut p = Parser { toks, pos: 0 };
        let node = p.expr(0)?;
        if p.peek().kind != Tok::Eof {
            return Err(format!("Unexpected token {:?} after expression", p.peek().kind));
        }
        Ok(node)
    }

    fn peek(&self) -> &Token { &self.toks[self.pos] }
    fn advance(&mut self) -> Token { let t = self.toks[self.pos].clone(); self.pos += 1; t }
    fn expect(&mut self, kind: &Tok) -> Result<Token, String> {
        if self.peek().kind.as_str() != kind.as_str() {
            return Err(format!("Expected {:?} but found {:?}", kind, self.peek().kind));
        }
        Ok(self.advance())
    }

    fn expr(&mut self, min_bp: i32) -> Result<Node, String> {
        let mut left = self.prefix()?;

        loop {
            let kind = self.peek().kind.clone();

            // postfix: member / index / call bind tightest
            if matches!(kind, Tok::Dot | Tok::LBracket | Tok::LParen) {
                if POSTFIX_BP < min_bp { break; }
                left = self.postfix(left)?;
                continue;
            }

            // `not in` compound
            if kind == Tok::Not && self.toks.get(self.pos + 1).map(|t| t.kind == Tok::In).unwrap_or(false) {
                if 100 < min_bp { break; }
                self.advance(); self.advance();
                let range = self.parse_in_right()?;
                left = Node::In { value: Box::new(left), range: Box::new(range), negated: true };
                continue;
            }

            if kind == Tok::In {
                if 100 < min_bp { break; }
                self.advance();
                let range = self.parse_in_right()?;
                left = Node::In { value: Box::new(left), range: Box::new(range), negated: false };
                continue;
            }

            let (ok, left_bp, right_bp) = infix_binding(&kind);
            if !ok || left_bp < min_bp { break; }
            let tok = self.advance();

            match tok.kind {
                Tok::Coalesce => {
                    let r = self.expr(right_bp)?;
                    left = Node::Coalesce { l: Box::new(left), r: Box::new(r) };
                }
                Tok::Caret => {
                    let r = self.expr(right_bp)?;
                    left = Node::Binary { op: BinOp::Pow, l: Box::new(left), r: Box::new(r) };
                }
                Tok::Star => { let r = self.expr(right_bp)?; left = Node::Binary { op: BinOp::Mul, l: Box::new(left), r: Box::new(r) }; }
                Tok::Slash => { let r = self.expr(right_bp)?; left = Node::Binary { op: BinOp::Div, l: Box::new(left), r: Box::new(r) }; }
                Tok::Percent => { let r = self.expr(right_bp)?; left = Node::Binary { op: BinOp::Mod, l: Box::new(left), r: Box::new(r) }; }
                Tok::Plus => { let r = self.expr(right_bp)?; left = Node::Binary { op: BinOp::Add, l: Box::new(left), r: Box::new(r) }; }
                Tok::Minus => { let r = self.expr(right_bp)?; left = Node::Binary { op: BinOp::Sub, l: Box::new(left), r: Box::new(r) }; }
                Tok::Eq => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Eq, l: Box::new(left), r: Box::new(r) }; }
                Tok::Ne => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Ne, l: Box::new(left), r: Box::new(r) }; }
                Tok::Lt => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Lt, l: Box::new(left), r: Box::new(r) }; }
                Tok::Gt => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Gt, l: Box::new(left), r: Box::new(r) }; }
                Tok::Le => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Le, l: Box::new(left), r: Box::new(r) }; }
                Tok::Ge => { let r = self.expr(right_bp)?; left = Node::Compare { op: CmpOp::Ge, l: Box::new(left), r: Box::new(r) }; }
                Tok::And => { let r = self.expr(right_bp)?; left = Node::Logical { and: true, l: Box::new(left), r: Box::new(r) }; }
                Tok::Or => { let r = self.expr(right_bp)?; left = Node::Logical { and: false, l: Box::new(left), r: Box::new(r) }; }
                Tok::Question => {
                    let then_b = self.expr(0)?;
                    self.expect(&Tok::Colon)?;
                    let else_b = self.expr(0)?;
                    left = Node::Ternary { cond: Box::new(left), then_b: Box::new(then_b), else_b: Box::new(else_b) };
                }
                other => return Err(format!("Unhandled infix token {:?}", other)),
            }
        }

        Ok(left)
    }

    fn prefix(&mut self) -> Result<Node, String> {
        let kind = self.peek().kind.clone();
        match kind {
            Tok::Plus => { self.advance(); let o = self.expr(PREFIX_PLUSMINUS_BP)?; Ok(Node::Unary { plus: true, operand: Box::new(o) }) }
            Tok::Minus => { self.advance(); let o = self.expr(PREFIX_PLUSMINUS_BP)?; Ok(Node::Unary { plus: false, operand: Box::new(o) }) }
            Tok::Not => { self.advance(); let o = self.expr(PREFIX_NOT_BP)?; Ok(Node::Unary { plus: false, operand: Box::new(o) }) }
            _ => self.primary(),
        }
    }

    fn primary(&mut self) -> Result<Node, String> {
        let tok = self.advance();
        match tok.kind {
            Tok::Number(n) => Ok(Node::Literal(Value::Num(n))),
            Tok::Str(s) => Ok(Node::Literal(Value::Str(s))),
            Tok::TrueKw => Ok(Node::Literal(Value::Bool(true))),
            Tok::FalseKw => Ok(Node::Literal(Value::Bool(false))),
            Tok::NullKw => Ok(Node::Literal(Value::Null)),
            Tok::Ident(name) => Ok(Node::Ident(name)),
            Tok::Hash => Ok(Node::Current),
            Tok::LParen => {
                let e = self.expr(0)?;
                self.expect(&Tok::RParen)?;
                Ok(e)
            }
            Tok::LBracket => self.array_literal(),
            Tok::LBrace => self.object_literal(),
            other => Err(format!("Unexpected token {:?} in primary", other)),
        }
    }

    fn array_literal(&mut self) -> Result<Node, String> {
        let mut items = Vec::new();
        if self.peek().kind != Tok::RBracket {
            items.push(self.expr(0)?);
            while self.peek().kind == Tok::Comma {
                self.advance();
                if self.peek().kind == Tok::RBracket { break; }
                items.push(self.expr(0)?);
            }
        }
        self.expect(&Tok::RBracket)?;
        Ok(Node::Array(items))
    }

    fn object_literal(&mut self) -> Result<Node, String> {
        let mut keys = Vec::new();
        let mut values = Vec::new();
        if self.peek().kind != Tok::RBrace {
            self.object_member(&mut keys, &mut values)?;
            while self.peek().kind == Tok::Comma {
                self.advance();
                if self.peek().kind == Tok::RBrace { break; }
                self.object_member(&mut keys, &mut values)?;
            }
        }
        self.expect(&Tok::RBrace)?;
        Ok(Node::Object { keys, values })
    }

    fn object_member(&mut self, keys: &mut Vec<String>, values: &mut Vec<Node>) -> Result<(), String> {
        let key_tok = self.advance();
        let name = match key_tok.kind {
            Tok::Str(s) | Tok::Ident(s) => s,
            Tok::Number(n) => crate::value::number_to_string(n),
            other => return Err(format!("Invalid object key {:?}", other)),
        };
        self.expect(&Tok::Colon)?;
        let v = self.expr(0)?;
        keys.push(name);
        values.push(v);
        Ok(())
    }

    fn postfix(&mut self, obj: Node) -> Result<Node, String> {
        let tok = self.advance();
        match tok.kind {
            Tok::Dot => {
                let name_tok = self.expect(&Tok::Ident(String::new()))?;
                match name_tok.kind {
                    Tok::Ident(name) => Ok(Node::Member { obj: Box::new(obj), name }),
                    other => Err(format!("Expected property name after '.', got {:?}", other)),
                }
            }
            Tok::LBracket => {
                let index = self.expr(0)?;
                self.expect(&Tok::RBracket)?;
                Ok(Node::Index { obj: Box::new(obj), index: Box::new(index) })
            }
            Tok::LParen => {
                let name = match &obj {
                    Node::Ident(n) => n.clone(),
                    _ => return Err("Only named functions can be called".to_string()),
                };
                let mut args = Vec::new();
                if self.peek().kind != Tok::RParen {
                    args.push(self.expr(0)?);
                    while self.peek().kind == Tok::Comma {
                        self.advance();
                        args.push(self.expr(0)?);
                    }
                }
                self.expect(&Tok::RParen)?;
                Ok(Node::Call { name, args })
            }
            other => Err(format!("Unexpected postfix token {:?}", other)),
        }
    }

    fn parse_in_right(&mut self) -> Result<Node, String> {
        let kind = self.peek().kind.clone();
        if matches!(kind, Tok::LBracket | Tok::LParen) {
            let start_incl = kind == Tok::LBracket;
            self.advance();
            let lo = self.expr(0)?;
            if self.peek().kind == Tok::DotDot {
                self.advance();
                let hi = self.expr(0)?;
                let close = self.peek().kind.clone();
                let end_incl = close == Tok::RBracket;
                if !matches!(close, Tok::RBracket | Tok::RParen) {
                    return Err("Unterminated range".to_string());
                }
                self.advance();
                return Ok(Node::Range { lo: Box::new(lo), hi: Box::new(hi), start_incl, end_incl });
            }
            // array membership list
            let mut items = vec![lo];
            while self.peek().kind == Tok::Comma {
                self.advance();
                items.push(self.expr(0)?);
            }
            let close_kind = if start_incl { Tok::RBracket } else { Tok::RParen };
            self.expect(&close_kind)?;
            return Ok(Node::Array(items));
        }
        self.expr(0)
    }
}

const POSTFIX_BP: i32 = 1000;
const PREFIX_PLUSMINUS_BP: i32 = 180;
const PREFIX_NOT_BP: i32 = 130;

fn infix_binding(t: &Tok) -> (bool, i32, i32) {
    match t {
        Tok::Coalesce => (true, 170, 169),
        Tok::Caret => (true, 160, 159),
        Tok::Star | Tok::Slash | Tok::Percent => (true, 140, 140),
        Tok::Plus | Tok::Minus => (true, 120, 120),
        Tok::Eq | Tok::Ne | Tok::Lt | Tok::Gt | Tok::Le | Tok::Ge | Tok::In => (true, 100, 100),
        Tok::And => (true, 80, 80),
        Tok::Or => (true, 60, 60),
        Tok::Question => (true, 40, 40),
        _ => (false, 0, 0),
    }
}

// Allow comparing Tok variants by discriminant for expect().
impl Tok {
    fn as_str(&self) -> &'static str {
        match self {
            Tok::Number(_) => "Number", Tok::Str(_) => "Str", Tok::Ident(_) => "Ident",
            Tok::And => "And", Tok::Or => "Or", Tok::Not => "Not", Tok::In => "In",
            Tok::TrueKw => "True", Tok::FalseKw => "False", Tok::NullKw => "Null",
            Tok::Plus => "Plus", Tok::Minus => "Minus", Tok::Star => "Star", Tok::Slash => "Slash",
            Tok::Percent => "Percent", Tok::Caret => "Caret",
            Tok::Eq => "Eq", Tok::Ne => "Ne", Tok::Lt => "Lt", Tok::Gt => "Gt", Tok::Le => "Le", Tok::Ge => "Ge",
            Tok::Dot => "Dot", Tok::DotDot => "DotDot", Tok::Hash => "Hash", Tok::Comma => "Comma",
            Tok::Colon => "Colon", Tok::Question => "Question", Tok::Coalesce => "Coalesce",
            Tok::LParen => "LParen", Tok::RParen => "RParen", Tok::LBracket => "LBracket",
            Tok::RBracket => "RBracket", Tok::LBrace => "LBrace", Tok::RBrace => "RBrace",
            Tok::Eof => "Eof",
        }
    }
}
