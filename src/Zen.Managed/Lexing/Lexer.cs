using System.Globalization;
using System.Text;

namespace Zen.Managed.Lexing;

/// <summary>
/// Tokenizer for the Zen expression language. Produces a flat token array in a
/// single pass. Numbers are parsed to double at lex time so the parser never
/// touches the source string again.
/// </summary>
internal static class Lexer
{
    public static Token[] Tokenize(string source)
    {
        var tokens = new List<Token>(source.Length / 4 + 4);
        int i = 0;
        int n = source.Length;

        while (i < n)
        {
            char c = source[i];

            // Whitespace
            if (c <= ' ')
            {
                i++;
                continue;
            }

            // Line comments: // ...
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                i += 2;
                while (i < n && source[i] != '\n') i++;
                continue;
            }

            // Numbers
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
            {
                int start = i;
                i += ScanNumber(source, i, out double value);
                tokens.Add(new Token(TokenKind.Number, start, i - start, value));
                continue;
            }

            // Identifiers / keywords
            if (IsIdentStart(c))
            {
                int start = i;
                i++;
                while (i < n && IsIdentPart(source[i])) i++;
                string text = source.Substring(start, i - start);
                tokens.Add(MakeIdentOrKeyword(text, start));
                continue;
            }

            // Strings
            if (c == '"' || c == '\'')
            {
                int start = i;
                string value = ReadString(source, ref i, c);
                tokens.Add(new Token(TokenKind.String, start, i - start, 0, value));
                continue;
            }

            // Operators / punctuation
            if (c == '#')
            {
                tokens.Add(new Token(TokenKind.Hash, i, 1));
                i++;
                continue;
            }

            int pstart = i;
            TokenKind kind = ScanOperator(source, ref i);
            if (kind == TokenKind.Eof)
                throw new ZenException($"Unexpected character '{source[pstart]}' at position {pstart}");
            tokens.Add(new Token(kind, pstart, i - pstart));
        }

        tokens.Add(new Token(TokenKind.Eof, n, 0));
        return tokens.ToArray();
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int ScanNumber(string s, int i, out double value)
    {
        int start = i;
        int n = s.Length;
        // integer part
        while (i < n && char.IsDigit(s[i])) i++;
        // fraction
        if (i < n && s[i] == '.')
        {
            // peek: must be a digit to be a fraction (otherwise it's a member access on a number? rare; treat as fraction only if digit)
            if (i + 1 < n && char.IsDigit(s[i + 1]))
            {
                i++;
                while (i < n && char.IsDigit(s[i])) i++;
            }
        }
        // exponent
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < n && (s[j] == '+' || s[j] == '-')) j++;
            if (j < n && char.IsDigit(s[j]))
            {
                i = j;
                while (i < n && char.IsDigit(s[i])) i++;
            }
        }

        value = double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        return i - start;
    }

    private static Token MakeIdentOrKeyword(string text, int start)
    {
        TokenKind kind = text switch
        {
            "and" => TokenKind.And,
            "or" => TokenKind.Or,
            "not" => TokenKind.Not,
            "in" => TokenKind.In,
            "true" => TokenKind.True,
            "false" => TokenKind.False,
            "null" => TokenKind.Null,
            _ => TokenKind.Ident,
        };
        return new Token(kind, start, text.Length, 0, text);
    }

    private static string ReadString(string s, ref int i, char quote)
    {
        i++; // opening quote
        int n = s.Length;
        var sb = new StringBuilder();
        while (i < n)
        {
            char c = s[i];
            if (c == quote) { i++; return sb.ToString(); }
            if (c == '\\')
            {
                i++;
                if (i >= n) break;
                char e = s[i];
                sb.Append(e switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '/' => '/',
                    '"' => '"',
                    '\'' => '\'',
                    'b' => '\b',
                    'f' => '\f',
                    'u' => ReadUnicodeEscape(s, ref i),
                    _ => e,
                });
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        throw new ZenException("Unterminated string literal");
    }

    private static char ReadUnicodeEscape(string s, ref int i)
    {
        int code = 0;
        for (int k = 0; k < 4; k++)
        {
            i++;
            if (i >= s.Length) break;
            code = code * 16 + HexValue(s[i]);
        }
        return (char)code;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    private static TokenKind ScanOperator(string s, ref int i)
    {
        char c = s[i];
        char next = i + 1 < s.Length ? s[i + 1] : '\0';
        char next2 = i + 2 < s.Length ? s[i + 2] : '\0';

        switch (c)
        {
            case '+': i++; return TokenKind.Plus;
            case '-': i++; return TokenKind.Minus;
            case '*': i++; return TokenKind.Star;
            case '%': i++; return TokenKind.Percent;
            case '^': i++; return TokenKind.Caret;
            case '(': i++; return TokenKind.LParen;
            case ')': i++; return TokenKind.RParen;
            case '[': i++; return TokenKind.LBracket;
            case ']': i++; return TokenKind.RBracket;
            case '{': i++; return TokenKind.LBrace;
            case '}': i++; return TokenKind.RBrace;
            case ',': i++; return TokenKind.Comma;
            case ':': i++; return TokenKind.Colon;
            case '/': i++; return TokenKind.Slash;
            case '.':
                if (next == '.') { i += 2; return TokenKind.DotDot; }
                i++; return TokenKind.Dot;
            case '?':
                if (next == '?') { i += 2; return TokenKind.Coalesce; }
                i++; return TokenKind.Question;
            case '=':
                if (next == '=') { i += 2; return TokenKind.Eq; }
                break;
            case '!':
                if (next == '=') { i += 2; return TokenKind.Ne; }
                i++; return TokenKind.Not;
            case '<':
                if (next == '=') { i += 2; return TokenKind.Le; }
                i++; return TokenKind.Lt;
            case '>':
                if (next == '=') { i += 2; return TokenKind.Ge; }
                i++; return TokenKind.Gt;
            case '&':
                if (next == '&') { i += 2; return TokenKind.And; }
                break;
            case '|':
                if (next == '|') { i += 2; return TokenKind.Or; }
                break;
        }
        return TokenKind.Eof; // signals unknown
    }
}
