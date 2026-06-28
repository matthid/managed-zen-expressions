using Zen.Managed.Ast;
using Zen.Managed.Lexing;

namespace Zen.Managed;

/// <summary>
/// Recursive-descent / precedence-climbing parser. Operator binding powers
/// follow the published GoRules Zen precedence table (highest to lowest):
///   grouping > member/index > unary +/- > ?? > ^ > * / % > not > binary +/- >
///   comparisons/in > and > or > ternary.
/// Left-associative operators share (leftBp,rightBp); right-associative ones
/// use (n+1, n) so the parser folds to the right.
/// </summary>
internal sealed class Parser
{
    private readonly Token[] _tokens;
    private int _pos;

    private Parser(Token[] tokens) { _tokens = tokens; }

    public static Node Parse(string source)
    {
        var tokens = Lexer.Tokenize(source);
        var p = new Parser(tokens);
        Node expr = p.ParseExpression(0);
        if (p.Peek().Kind != TokenKind.Eof)
            throw new ZenException($"Unexpected token {p.Peek()} after expression");
        return expr;
    }

    private Token Peek() => _tokens[_pos];
    private Token Advance() => _tokens[_pos++];
    private Token Expect(TokenKind kind)
    {
        var t = _tokens[_pos];
        if (t.Kind != kind) throw new ZenException($"Expected {kind} but found {t}");
        _pos++;
        return t;
    }

    // ---- main expression loop -------------------------------------------------

    private Node ParseExpression(int minBp)
    {
        Node left = ParsePrefix();

        while (true)
        {
            Token t = Peek();

            // Postfix member / index / call bind tightest.
            if (t.Kind is TokenKind.Dot or TokenKind.LBracket or TokenKind.LParen)
            {
                // These are higher than any infix bp, so always consume while minBp allows.
                if (PostfixBp() < minBp) break;
                left = ParsePostfix(left);
                continue;
            }

            var (ok, leftBp, rightBp) = InfixBinding(t);
            if (!ok || leftBp < minBp) break;

            Advance();

            // `not in` compound operator (membership negation): x not in [...]
            if (t.Kind == TokenKind.Not && _tokens[_pos + 1].Kind == TokenKind.In)
            {
                if (100 < minBp) break;
                Advance(); Advance(); // consume `not in`
                Node range = ParseInRight();
                left = Nodes.In(left, range, negated: true);
                continue;
            }

            if (t.Kind == TokenKind.In)
            {
                Node range = ParseInRight();
                left = Nodes.In(left, range, negated: false);
                continue;
            }

            Node right = ParseExpression(rightBp);

            switch (t.Kind)
            {
                case TokenKind.Coalesce: left = Nodes.Coalesce(left, right); break;
                case TokenKind.Caret:    left = Nodes.Binary(BinOp.Pow, left, right); break;
                case TokenKind.Star:     left = Nodes.Binary(BinOp.Mul, left, right); break;
                case TokenKind.Slash:    left = Nodes.Binary(BinOp.Div, left, right); break;
                case TokenKind.Percent:  left = Nodes.Binary(BinOp.Mod, left, right); break;
                case TokenKind.Plus:     left = Nodes.Binary(BinOp.Add, left, right); break;
                case TokenKind.Minus:    left = Nodes.Binary(BinOp.Sub, left, right); break;
                case TokenKind.Eq:       left = Nodes.Compare(CmpOp.Eq, left, right); break;
                case TokenKind.Ne:       left = Nodes.Compare(CmpOp.Ne, left, right); break;
                case TokenKind.Lt:       left = Nodes.Compare(CmpOp.Lt, left, right); break;
                case TokenKind.Gt:       left = Nodes.Compare(CmpOp.Gt, left, right); break;
                case TokenKind.Le:       left = Nodes.Compare(CmpOp.Le, left, right); break;
                case TokenKind.Ge:       left = Nodes.Compare(CmpOp.Ge, left, right); break;
                case TokenKind.And:      left = Nodes.Logical(left, right, and: true); break;
                case TokenKind.Or:       left = Nodes.Logical(left, right, and: false); break;
                case TokenKind.Question:
                    // ternary: cond ? a : b
                    {
                        Node thenBranch = ParseExpression(0);
                        Expect(TokenKind.Colon);
                        Node elseBranch = ParseExpression(0);
                        left = Nodes.Ternary(left, thenBranch, elseBranch);
                        break;
                    }
                default:
                    throw new ZenException($"Unhandled infix token {t}");
            }
        }

        return left;
    }

    private const int PostfixBpValue = 1000;
    private static int PostfixBp() => PostfixBpValue;

    // (present, leftBp, rightBp)
    private static (bool, int, int) InfixBinding(Token t)
    {
        switch (t.Kind)
        {
            // right-associative: ?? binds tighter than ^ (Zen precedence 4 > 5)
            case TokenKind.Coalesce: return (true, 170, 169);
            case TokenKind.Caret:    return (true, 160, 159);
            // * / %
            case TokenKind.Star:
            case TokenKind.Slash:
            case TokenKind.Percent:  return (true, 140, 140);
            // binary + -
            case TokenKind.Plus:
            case TokenKind.Minus:    return (true, 120, 120);
            // comparisons / in
            case TokenKind.Eq:
            case TokenKind.Ne:
            case TokenKind.Lt:
            case TokenKind.Gt:
            case TokenKind.Le:
            case TokenKind.Ge:
            case TokenKind.In:       return (true, 100, 100);
            // and
            case TokenKind.And:      return (true, 80, 80);
            // or
            case TokenKind.Or:       return (true, 60, 60);
            // ternary (lowest)
            case TokenKind.Question: return (true, 40, 40);
            default: return (false, 0, 0);
        }
    }

    // ---- prefix / primary -----------------------------------------------------

    private Node ParsePrefix()
    {
        Token t = Peek();

        switch (t.Kind)
        {
            case TokenKind.Plus: Advance(); return Nodes.Unary(plus: true, ParseExpression(PrefixBpPlusMinus));
            case TokenKind.Minus: Advance(); return Nodes.Unary(plus: false, ParseExpression(PrefixBpPlusMinus));
            case TokenKind.Not: Advance(); return Nodes.Not(ParseExpression(PrefixBpNot));
        }

        return ParsePrimary();
    }

    // prefix +/- binds tighter than ?? (Zen precedence 3 > 4)
    private const int PrefixBpPlusMinus = 180;
    // `not` (precedence 7) binds looser than * / % (6) but tighter than binary + - (8)
    private const int PrefixBpNot = 130;

    private Node ParsePrimary()
    {
        Token t = Advance();

        switch (t.Kind)
        {
            case TokenKind.Number: return Nodes.Literal(ZenValue.FromNumber(t.Number));
            case TokenKind.String: return Nodes.Literal(ZenValue.FromString(t.Text));
            case TokenKind.True:   return Nodes.Literal(ZenValue.True);
            case TokenKind.False:  return Nodes.Literal(ZenValue.False);
            case TokenKind.Null:   return Nodes.Literal(ZenValue.Null);
            case TokenKind.Ident:  return Nodes.Ident(t.Text);
            case TokenKind.Hash:   return Nodes.Current();
            case TokenKind.LParen:
                {
                    Node e = ParseExpression(0);
                    Expect(TokenKind.RParen);
                    return e;
                }
            case TokenKind.LBracket: return ParseArrayLiteral();
            case TokenKind.LBrace:   return ParseObjectLiteral();
        }

        throw new ZenException($"Unexpected token {t} (kind {t.Kind})");
    }

    private Node ParseArrayLiteral()
    {
        var elements = new List<Node>();
        // Allow `[1..10]` range used directly as a value -> represented as Range node in array.
        if (Peek().Kind != TokenKind.RBracket)
        {
            elements.Add(ParseExpression(0));
            while (Peek().Kind == TokenKind.Comma)
            {
                Advance();
                if (Peek().Kind == TokenKind.RBracket) break; // trailing comma
                elements.Add(ParseExpression(0));
            }
        }
        Expect(TokenKind.RBracket);
        return Nodes.Array(elements.ToArray());
    }

    private Node ParseObjectLiteral()
    {
        var keys = new List<string>();
        var values = new List<Node>();
        if (Peek().Kind != TokenKind.RBrace)
        {
            ParseObjectMember(keys, values);
            while (Peek().Kind == TokenKind.Comma)
            {
                Advance();
                if (Peek().Kind == TokenKind.RBrace) break;
                ParseObjectMember(keys, values);
            }
        }
        Expect(TokenKind.RBrace);
        return Nodes.Object(keys.ToArray(), values.ToArray());
    }

    private void ParseObjectMember(List<string> keys, List<Node> values)
    {
        Token key = Peek();
        string name;
        if (key.Kind == TokenKind.String || key.Kind == TokenKind.Ident)
        {
            Advance();
            name = key.Text;
        }
        else if (key.Kind == TokenKind.Number)
        {
            Advance();
            name = key.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            throw new ZenException($"Invalid object key {key} (dynamic [expr] keys are not supported)");
        }
        Expect(TokenKind.Colon);
        Node v = ParseExpression(0);
        keys.Add(name);
        values.Add(v);
    }

    // ---- postfix --------------------------------------------------------------

    private Node ParsePostfix(Node obj)
    {
        Token t = Advance();
        switch (t.Kind)
        {
            case TokenKind.Dot:
                {
                    Token name = Expect(TokenKind.Ident);
                    return Nodes.Member(obj, name.Text);
                }
            case TokenKind.LBracket:
                {
                    Node index = ParseExpression(0);
                    Expect(TokenKind.RBracket);
                    return Nodes.Index(obj, index);
                }
            case TokenKind.LParen:
                {
                    // Function call: callee must be an Ident.
                    if (obj.Kind != NodeKind.Ident)
                        throw new ZenException("Only named functions can be called");
                    var args = new List<Node>();
                    if (Peek().Kind != TokenKind.RParen)
                    {
                        args.Add(ParseExpression(0));
                        while (Peek().Kind == TokenKind.Comma)
                        {
                            Advance();
                            args.Add(ParseExpression(0));
                        }
                    }
                    Expect(TokenKind.RParen);
                    return Nodes.Call(obj.Name, args.ToArray());
                }
            default:
                throw new ZenException($"Unexpected postfix token {t}");
        }
    }

    // ---- `in` right-hand side (array or range) --------------------------------

    private Node ParseInRight()
    {
        Token t = Peek();
        if (t.Kind == TokenKind.LBracket || t.Kind == TokenKind.LParen)
        {
            bool startIncl = t.Kind == TokenKind.LBracket;
            Advance();
            Node lo = ParseExpression(0);
            if (Peek().Kind == TokenKind.DotDot)
            {
                Advance();
                Node hi = ParseExpression(0);
                Token close = Peek();
                bool endIncl = close.Kind == TokenKind.RBracket;
                if (close.Kind is not (TokenKind.RBracket or TokenKind.RParen))
                    throw new ZenException($"Unterminated range, found {close}");
                Advance();
                return Nodes.Range(lo, hi, startIncl, endIncl);
            }
            // It was an array membership list with [ ... ] grouping.
            var items = new List<Node> { lo };
            while (Peek().Kind == TokenKind.Comma)
            {
                Advance();
                items.Add(ParseExpression(0));
            }
            TokenKind closeKind = startIncl ? TokenKind.RBracket : TokenKind.RParen;
            Expect(closeKind);
            return Nodes.Array(items.ToArray());
        }

        // Plain value (e.g. a variable holding an array).
        return ParseExpression(0);
    }
}
