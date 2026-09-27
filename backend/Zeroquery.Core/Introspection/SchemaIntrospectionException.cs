namespace Zeroquery.Core.Introspection;

/// <summary>
/// Thrown when schema introspection fails (bad connection string, network failure,
/// insufficient permissions, unsupported provider, etc). The message is guaranteed to be
/// safe to show a user and safe to log — implementations must never let the raw
/// connection string or full provider exception text leak into this message.
/// </summary>
public sealed class SchemaIntrospectionException : Exception
{
    public SchemaIntrospectionException(string message) : base(message)
    {
    }

    public SchemaIntrospectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
