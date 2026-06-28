namespace Zen.Managed;

public class ZenException : Exception
{
    public ZenException(string message) : base(message) { }
    public ZenException(string message, Exception inner) : base(message, inner) { }
}
