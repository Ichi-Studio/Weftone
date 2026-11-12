namespace Weftone.Adapters.ProjectStore;

/// <summary>工程包非法（坏 zip / zip slip / schema 不符 / SHA256 不匹配 / 越界值）。</summary>
public sealed class InvalidProjectException : Exception
{
    public InvalidProjectException(string message) : base(message) { }

    public InvalidProjectException(string message, Exception inner) : base(message, inner) { }
}
