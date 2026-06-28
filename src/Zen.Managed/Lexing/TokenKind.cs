namespace Zen.Managed.Lexing;

public enum TokenKind : byte
{
    // Literals
    Number,
    String,
    Ident,

    // Keywords
    And,      // and / &&
    Or,       // or  / ||
    Not,      // not / !
    In,       // in
    True,     // true
    False,    // false
    Null,     // null

    // Operators
    Plus,     // +
    Minus,    // -
    Star,     // *
    Slash,    // /
    Percent,  // %
    Caret,    // ^

    Eq,       // ==
    Ne,       // !=
    Lt,       // <
    Gt,       // >
    Le,       // <=
    Ge,       // >=

    Dot,      // .
    DotDot,   // ..
    Hash,     // #   (current element in closures)
    Comma,    // ,
    Colon,    // :
    Question, // ?
    Coalesce, // ??

    LParen,   // (
    RParen,   // )
    LBracket, // [
    RBracket, // ]
    LBrace,   // {
    RBrace,   // }

    Eof,
}
