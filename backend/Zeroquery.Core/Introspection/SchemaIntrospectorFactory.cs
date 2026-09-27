namespace Zeroquery.Core.Introspection;

/// <summary>
/// Resolves the correct <see cref="ISchemaIntrospector"/> for a requested <see cref="DatabaseProvider"/>.
/// Registered as a singleton; introspectors themselves are stateless and safe to reuse.
/// </summary>
public sealed class SchemaIntrospectorFactory
{
    private readonly Dictionary<DatabaseProvider, ISchemaIntrospector> _introspectors;

    public SchemaIntrospectorFactory(IEnumerable<ISchemaIntrospector> introspectors)
    {
        _introspectors = introspectors.ToDictionary(i => i.Provider);
    }

    public ISchemaIntrospector Resolve(DatabaseProvider provider)
    {
        if (_introspectors.TryGetValue(provider, out var introspector))
        {
            return introspector;
        }

        throw new SchemaIntrospectionException($"No schema introspector is registered for provider '{provider}'.");
    }
}
