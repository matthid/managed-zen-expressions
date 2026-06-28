//! Tokenizer mirroring `Zen.Managed.Lexing.Lexer`.

#[derive(Clone, Debug, PartialEq)]
pub enum Tok {
    Number(f64),
    Str(String),
    Ident(String),

    And, Or, Not, In, TrueKw, FalseKw, NullKw,

    Plus, Minus, Star, Slash, Percent, Caret,
    Eq, Ne, Lt, Gt, Le, Ge,
    Dot, DotDot, Hash, Comma, Colon, Question, Coalesce,
    LParen, RParen, LBracket, RBracket, LBrace, RBrace,
    Eof,
}

#[derive(Clone, Debug)]
pub struct Token {
    pub kind: Tok,
    pub pos: usize,
}

pub fn tokenize(src: &str) -> Result<Vec<Token>, String> {
    let bytes: Vec<char> = src.chars().collect();
    let n = bytes.len();
    let mut i = 0usize;
    let mut out: Vec<Token> = Vec::with_capacity(src.len() / 4 + 4);

    while i < n {
        let c = bytes[i];

        if c.is_whitespace() { i += 1; continue; }

        // line comment
        if c == '/' && i + 1 < n && bytes[i + 1] == '/' {
            i += 2;
            while i < n && bytes[i] != '\n' { i += 1; }
            continue;
        }

        // numbers
        if c.is_ascii_digit() || (c == '.' && i + 1 < n && bytes[i + 1].is_ascii_digit()) {
            let start = i;
            i += scan_number(&bytes, i)?;
            let text: String = bytes[start..i].iter().collect();
            let value: f64 = text.parse().map_err(|_| format!("Invalid number '{}'", text))?;
            out.push(Token { kind: Tok::Number(value), pos: start });
            continue;
        }

        // identifiers / keywords
        if is_ident_start(c) {
            let start = i;
            i += 1;
            while i < n && is_ident_part(bytes[i]) { i += 1; }
            let text: String = bytes[start..i].iter().collect();
            out.push(Token { kind: make_ident(&text), pos: start });
            continue;
        }

        // strings
        if c == '"' || c == '\'' {
            let start = i;
            let (value, end) = read_string(&bytes, i)?;
            i = end;
            out.push(Token { kind: Tok::Str(value), pos: start });
            continue;
        }

        // current element
        if c == '#' {
            out.push(Token { kind: Tok::Hash, pos: i });
            i += 1;
            continue;
        }

        // operators
        let start = i;
        let kind = scan_operator(&bytes, &mut i)?;
        match kind {
            Some(k) => out.push(Token { kind: k, pos: start }),
            None => return Err(format!("Unexpected character '{}' at position {}", c, start)),
        }
    }

    out.push(Token { kind: Tok::Eof, pos: n });
    Ok(out)
}

fn is_ident_start(c: char) -> bool { c.is_alphabetic() || c == '_' || c == '$' }
fn is_ident_part(c: char) -> bool { c.is_alphanumeric() || c == '_' || c == '$' }

fn scan_number(b: &[char], mut i: usize) -> Result<usize, String> {
    let n = b.len();
    let start = i;
    while i < n && b[i].is_ascii_digit() { i += 1; }
    if i < n && b[i] == '.' && i + 1 < n && b[i + 1].is_ascii_digit() {
        i += 1;
        while i < n && b[i].is_ascii_digit() { i += 1; }
    }
    if i < n && (b[i] == 'e' || b[i] == 'E') {
        let mut j = i + 1;
        if j < n && (b[j] == '+' || b[j] == '-') { j += 1; }
        if j < n && b[j].is_ascii_digit() {
            i = j;
            while i < n && b[i].is_ascii_digit() { i += 1; }
        }
    }
    Ok(i - start)
}

fn make_ident(t: &str) -> Tok {
    match t {
        "and" => Tok::And,
        "or" => Tok::Or,
        "not" => Tok::Not,
        "in" => Tok::In,
        "true" => Tok::TrueKw,
        "false" => Tok::FalseKw,
        "null" => Tok::NullKw,
        _ => Tok::Ident(t.to_string()),
    }
}

fn read_string(b: &[char], mut i: usize) -> Result<(String, usize), String> {
    let quote = b[i];
    i += 1;
    let n = b.len();
    let mut out = String::new();
    while i < n {
        let c = b[i];
        if c == quote { return Ok((out, i + 1)); }
        if c == '\\' {
            i += 1;
            if i >= n { break; }
            let e = b[i];
            let ch = match e {
                'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', '/' => '/',
                '"' => '"', '\'' => '\'', 'b' => '\u{0008}', 'f' => '\u{000C}',
                'u' => read_unicode_escape(b, &mut i),
                other => other,
            };
            out.push(ch);
            i += 1;
        } else {
            out.push(c);
            i += 1;
        }
    }
    Err("Unterminated string literal".to_string())
}

fn read_unicode_escape(b: &[char], i: &mut usize) -> char {
    let mut code: u32 = 0;
    for _ in 0..4 {
        *i += 1;
        if *i >= b.len() { break; }
        let c = b[*i];
        code = code * 16 + (c.to_digit(16).unwrap_or(0));
    }
    char::from_u32(code).unwrap_or('\u{FFFD}')
}

fn scan_operator(b: &[char], i: &mut usize) -> Result<Option<Tok>, String> {
    let c = b[*i];
    let next = if *i + 1 < b.len() { Some(b[*i + 1]) } else { None };
    macro_rules! adv1 { ($t:expr) => {{ *i += 1; return Ok(Some($t)); }}; }
    match c {
        '+' => adv1!(Tok::Plus),
        '-' => adv1!(Tok::Minus),
        '*' => adv1!(Tok::Star),
        '%' => adv1!(Tok::Percent),
        '^' => adv1!(Tok::Caret),
        '(' => adv1!(Tok::LParen),
        ')' => adv1!(Tok::RParen),
        '[' => adv1!(Tok::LBracket),
        ']' => adv1!(Tok::RBracket),
        '{' => adv1!(Tok::LBrace),
        '}' => adv1!(Tok::RBrace),
        ',' => adv1!(Tok::Comma),
        ':' => adv1!(Tok::Colon),
        '/' => adv1!(Tok::Slash),
        '.' => {
            if next == Some('.') { *i += 2; return Ok(Some(Tok::DotDot)); }
            adv1!(Tok::Dot);
        }
        '?' => {
            if next == Some('?') { *i += 2; return Ok(Some(Tok::Coalesce)); }
            adv1!(Tok::Question);
        }
        '=' => {
            if next == Some('=') { *i += 2; return Ok(Some(Tok::Eq)); }
            return Err("Unexpected '=' (did you mean '=='?)".to_string());
        }
        '!' => {
            if next == Some('=') { *i += 2; return Ok(Some(Tok::Ne)); }
            adv1!(Tok::Not);
        }
        '<' => {
            if next == Some('=') { *i += 2; return Ok(Some(Tok::Le)); }
            adv1!(Tok::Lt);
        }
        '>' => {
            if next == Some('=') { *i += 2; return Ok(Some(Tok::Ge)); }
            adv1!(Tok::Gt);
        }
        '&' => {
            if next == Some('&') { *i += 2; return Ok(Some(Tok::And)); }
            return Err("Unexpected '&'".to_string());
        }
        '|' => {
            if next == Some('|') { *i += 2; return Ok(Some(Tok::Or)); }
            return Err("Unexpected '|'".to_string());
        }
        _ => Ok(None),
    }
}
