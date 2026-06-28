namespace Zen.Managed.Lexing;

internal readonly struct Token
{
    public readonly TokenKind Kind;
    public readonly int Start;
    public readonly int Length;
    public readonly double Number;   // valid for Number
    public readonly string Text;     // valid for String (unescaped value) / Ident

    public Token(TokenKind kind, int start, int length, double number = 0, string text = "")
    {
        Kind = kind; Start = start; Length = length; Number = number; Text = text;
    }

    public override string ToString() => Kind == TokenKind.Number ? $"{Kind}@{Start}:{Number}" : $"{Kind}@{Start}:'{Text}'";
}
