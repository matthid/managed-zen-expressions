namespace Zen.Managed.Ast;

internal enum NodeKind : byte
{
    Literal,
    Ident,
    Current,    // #
    Array,
    Object,
    Unary,
    Binary,     // arithmetic + power
    Logical,    // and / or (short-circuit)
    Compare,    // == != < > <= >=
    Ternary,
    Coalesce,   // ??
    Member,     // a.b
    Index,      // a[b]
    Call,       // name(args)  -> built-in function
    Range,      // lo..hi (with inclusive flags) - appears as `in` RHS
    In,         // membership / range check
}

internal static class Nodes
{
    public static Node Literal(ZenValue v) => new(NodeKind.Literal) { Value = v };
    public static Node Ident(string name) => new(NodeKind.Ident) { Name = name };
    public static Node Current() => new(NodeKind.Current);
    public static Node Array(Node[] items) => new(NodeKind.Array) { List = items };
    public static Node Object(string[] keys, Node[] values) => new(NodeKind.Object) { Keys = keys, List = values };
    public static Node Unary(bool plus, Node operand) => new(NodeKind.Unary) { A = operand, Inclusive = plus };
    public static Node Not(Node operand) => new(NodeKind.Unary) { A = operand, NotFlag = true };
    public static Node Binary(BinOp op, Node l, Node r) => new(NodeKind.Binary) { BinOp = op, A = l, B = r };
    public static Node Logical(Node l, Node r, bool and) => new(NodeKind.Logical) { A = l, B = r, Inclusive = and };
    public static Node Compare(CmpOp op, Node l, Node r) => new(NodeKind.Compare) { CmpOp = op, A = l, B = r };
    public static Node Ternary(Node cond, Node thenBranch, Node elseBranch) => new(NodeKind.Ternary) { A = cond, B = thenBranch, C = elseBranch };
    public static Node Coalesce(Node l, Node r) => new(NodeKind.Coalesce) { A = l, B = r };
    public static Node Member(Node obj, string name) => new(NodeKind.Member) { A = obj, Name = name };
    public static Node Index(Node obj, Node index) => new(NodeKind.Index) { A = obj, B = index };
    public static Node Call(string name, Node[] args) => new(NodeKind.Call) { Name = name, List = args };
    public static Node Range(Node lo, Node hi, bool startIncl, bool endIncl) => new(NodeKind.Range) { A = lo, B = hi, StartIncl = startIncl, EndIncl = endIncl };
    public static Node In(Node value, Node range, bool negated) => new(NodeKind.In) { A = value, B = range, Inclusive = negated };
}

internal enum BinOp : byte
{
    Add, Sub, Mul, Div, Mod, Pow,
}

internal enum CmpOp : byte
{
    Eq, Ne, Lt, Gt, Le, Ge,
}

/// <summary>
/// A single AST node reused across node kinds (data-oriented layout). The
/// active fields depend on <see cref="Kind"/>; comments document the mapping.
/// A flat node type keeps the tree compact and lets the evaluator dispatch with
/// one switch instead of many virtual calls.
/// </summary>
internal sealed class Node
{
    public NodeKind Kind;

    // Literal
    public ZenValue Value;

    // Ident (name), Member (property name), Call (function name)
    public string Name = "";

    // First / second / third child (A=left/cond, B=right, C=ternary-else / member-object)
    public Node? A;
    public Node? B;
    public Node? C;

    // Array (elements), Call (arguments)
    public Node[] List = Array.Empty<Node>();

    // Object: parallel keys + values
    public string[] Keys = Array.Empty<string>();

    public BinOp BinOp;     // Binary
    public CmpOp CmpOp;     // Compare
    public bool Inclusive;  // unary +/- (true = plus) / Range start incl / In negated
    public bool NotFlag;    // unary logical `not` (distinct from numeric negation)

    public bool StartIncl;  // Range
    public bool EndIncl;    // Range

    public Node(NodeKind kind) { Kind = kind; }
}
