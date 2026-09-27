using Zeroquery.Core.Introspection;

namespace Zeroquery.Tests.Introspection;

public class SchemaIntrospectorFactoryTests
{
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, typeof(SqlServerSchemaIntrospector))]
    [InlineData(DatabaseProvider.PostgreSql, typeof(PostgreSqlSchemaIntrospector))]
    [InlineData(DatabaseProvider.MySql, typeof(MySqlSchemaIntrospector))]
    public void Resolve_ReturnsCorrectIntrospector_ForEachProvider(DatabaseProvider provider, Type expectedType)
    {
        var factory = new SchemaIntrospectorFactory(new ISchemaIntrospector[]
        {
            new SqlServerSchemaIntrospector(),
            new PostgreSqlSchemaIntrospector(),
            new MySqlSchemaIntrospector()
        });

        var result = factory.Resolve(provider);

        Assert.IsType(expectedType, result);
    }

    [Fact]
    public void Resolve_Throws_WhenProviderNotRegistered()
    {
        var factory = new SchemaIntrospectorFactory(new ISchemaIntrospector[]
        {
            new SqlServerSchemaIntrospector()
        });

        var ex = Assert.Throws<SchemaIntrospectionException>(() => factory.Resolve(DatabaseProvider.PostgreSql));
        Assert.Contains("PostgreSql", ex.Message);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.MySql)]
    public async Task GetSchemaAsync_Throws_WhenConnectionStringIsEmpty(DatabaseProvider provider)
    {
        ISchemaIntrospector introspector = provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerSchemaIntrospector(),
            DatabaseProvider.PostgreSql => new PostgreSqlSchemaIntrospector(),
            DatabaseProvider.MySql => new MySqlSchemaIntrospector(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

        await Assert.ThrowsAsync<SchemaIntrospectionException>(() => introspector.GetSchemaAsync(string.Empty));
    }
}
