using System.Text.Json;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;

namespace Zeroquery.Tests.ConfigGeneration;

public class DabConfigGeneratorTests
{
    private static ConfigGenerationRequest SampleRequest(IReadOnlyList<string>? writeActions = null) =>
        new(
            Provider: DatabaseProvider.SqlServer,
            ConnectionStringEnvVarName: "ZQ_DB_CONN",
            Entities: new[]
            {
                new EntitySelectionRequest(
                    Schema: "dbo",
                    TableName: "Orders",
                    IsView: false,
                    Columns: new[]
                    {
                        new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true),
                        new ColumnSelectionRequest("CustomerName", Include: true, IsPrimaryKey: false, Description: "Name of the customer"),
                        new ColumnSelectionRequest("InternalNotes", Include: false, IsPrimaryKey: false)
                    },
                    Description: "Customer orders",
                    WriteActions: writeActions)
            });

    [Fact]
    public void Generate_ProducesValidJson()
    {
        var json = DabConfigGenerator.Generate(SampleRequest());

        var act = () => JsonDocument.Parse(json);
        Assert.NotNull(act());
    }

    [Fact]
    public void Generate_NeverEmbedsRawConnectionString_UsesEnvFunction()
    {
        var json = DabConfigGenerator.Generate(SampleRequest());

        Assert.Contains("@env('ZQ_DB_CONN')", json);
    }

    [Fact]
    public void Generate_SetsMcpEnabled()
    {
        var json = DabConfigGenerator.Generate(SampleRequest());
        using var doc = JsonDocument.Parse(json);

        var mcpEnabled = doc.RootElement
            .GetProperty("runtime")
            .GetProperty("mcp")
            .GetProperty("enabled")
            .GetBoolean();

        Assert.True(mcpEnabled);
    }

    [Fact]
    public void Generate_DefaultsToReadOnly_WhenNoWriteActionsSpecified()
    {
        var json = DabConfigGenerator.Generate(SampleRequest());
        using var doc = JsonDocument.Parse(json);

        var permissions = doc.RootElement.GetProperty("entities").GetProperty("Orders").GetProperty("permissions")[0];
        var actions = permissions.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .ToList();

        Assert.Equal(new[] { "read" }, actions);
    }

    [Fact]
    public void Generate_IncludesOptedInWriteActions()
    {
        var json = DabConfigGenerator.Generate(SampleRequest(new[] { "create", "update" }));
        using var doc = JsonDocument.Parse(json);

        var permissions = doc.RootElement.GetProperty("entities").GetProperty("Orders").GetProperty("permissions")[0];
        var actions = permissions.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .ToList();

        Assert.Equal(new[] { "read", "create", "update" }, actions);
    }

    [Fact]
    public void Generate_ExcludesDeselectedColumns_FromFieldsAndPermissions()
    {
        var json = DabConfigGenerator.Generate(SampleRequest());
        using var doc = JsonDocument.Parse(json);

        var fieldNames = doc.RootElement.GetProperty("entities").GetProperty("Orders").GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .ToList();

        Assert.DoesNotContain("InternalNotes", fieldNames);
        Assert.Contains("Id", fieldNames);
        Assert.Contains("CustomerName", fieldNames);
    }

    [Fact]
    public void Generate_AlwaysIncludesPrimaryKey_EvenIfNotExplicitlySelected()
    {
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "Widgets", IsView: false,
                    Columns: new[]
                    {
                        new ColumnSelectionRequest("Id", Include: false, IsPrimaryKey: true),
                        new ColumnSelectionRequest("Name", Include: true, IsPrimaryKey: false)
                    })
            });

        var json = DabConfigGenerator.Generate(request);
        using var doc = JsonDocument.Parse(json);

        var fieldNames = doc.RootElement.GetProperty("entities").GetProperty("Widgets").GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("Id", fieldNames);
    }

    [Fact]
    public void Generate_MarksAllColumnsAsCompositeKey_WhenEntityHasNoDetectedPrimaryKey()
    {
        // Reproduces a real crash confirmed live against `dab start`: SQL views have no PK
        // constraint, so introspection reports IsPrimaryKey=false for every column. If the
        // generated config has zero "primary-key": true fields, DAB throws
        // "Primary key not configured on the given database object <name>" and the ENTIRE
        // dab start process fails — not just this entity. Every exposed column must be
        // marked as a (composite) key in this fallback case so DAB can start successfully.
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "Category Sales for 1997", IsView: true,
                    Columns: new[]
                    {
                        new ColumnSelectionRequest("CategoryName", Include: true, IsPrimaryKey: false),
                        new ColumnSelectionRequest("CategorySales", Include: true, IsPrimaryKey: false)
                    })
            });

        var json = DabConfigGenerator.Generate(request);
        using var doc = JsonDocument.Parse(json);

        var fields = doc.RootElement.GetProperty("entities")
            .EnumerateObject().Single().Value
            .GetProperty("fields")
            .EnumerateArray()
            .ToList();

        Assert.All(fields, f => Assert.True(f.GetProperty("primary-key").GetBoolean()));
    }

    [Fact]
    public void Generate_DoesNotMarkNonKeyColumns_WhenARealPrimaryKeyExists()
    {
        // Guards against the composite-key fallback firing when it shouldn't — a normal
        // table with a real PK must only mark that PK column, not every column.
        var json = DabConfigGenerator.Generate(SampleRequest());
        using var doc = JsonDocument.Parse(json);

        var fields = doc.RootElement.GetProperty("entities").GetProperty("Orders").GetProperty("fields")
            .EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => f);

        Assert.True(fields["Id"].GetProperty("primary-key").GetBoolean());
        Assert.False(fields["CustomerName"].TryGetProperty("primary-key", out _));
    }

    [Fact]
    public void Generate_Throws_WhenNoEntitiesSelected()
    {
        var request = new ConfigGenerationRequest(DatabaseProvider.SqlServer, "ZQ_DB_CONN", Array.Empty<EntitySelectionRequest>());

        Assert.Throws<InvalidOperationException>(() => DabConfigGenerator.Generate(request));
    }

    [Fact]
    public void Generate_Throws_WhenEntityHasNoIncludedColumns()
    {
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "Empty", IsView: false,
                    Columns: new[] { new ColumnSelectionRequest("Col", Include: false, IsPrimaryKey: false) })
            });

        Assert.Throws<InvalidOperationException>(() => DabConfigGenerator.Generate(request));
    }

    [Fact]
    public void Generate_Throws_OnUnsupportedWriteAction()
    {
        Assert.Throws<InvalidOperationException>(() => DabConfigGenerator.Generate(SampleRequest(new[] { "drop-table" })));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "mssql")]
    [InlineData(DatabaseProvider.PostgreSql, "postgresql")]
    [InlineData(DatabaseProvider.MySql, "mysql")]
    public void Generate_MapsDatabaseTypeCorrectly(DatabaseProvider provider, string expectedDatabaseType)
    {
        var request = new ConfigGenerationRequest(
            provider,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "T", IsView: false,
                    Columns: new[] { new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true) })
            });

        var json = DabConfigGenerator.Generate(request);
        using var doc = JsonDocument.Parse(json);

        var databaseType = doc.RootElement.GetProperty("data-source").GetProperty("database-type").GetString();
        Assert.Equal(expectedDatabaseType, databaseType);
    }

    [Fact]
    public void Generate_DeduplicatesEntityNames_WhenSameTableNameAcrossSchemas()
    {
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "sales", "Orders", IsView: false,
                    Columns: new[] { new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true) }),
                new EntitySelectionRequest(
                    "archive", "Orders", IsView: false,
                    Columns: new[] { new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true) })
            });

        var json = DabConfigGenerator.Generate(request);
        using var doc = JsonDocument.Parse(json);

        var entityNames = doc.RootElement.GetProperty("entities").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("sales_Orders", entityNames);
        Assert.Contains("archive_Orders", entityNames);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Generate_RespectsEnableRestAndEnableGraphQL(bool enableRest, bool enableGraphQL)
    {
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "Products", IsView: false,
                    Columns: new[] { new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true) })
            },
            EnableRest: enableRest,
            EnableGraphQL: enableGraphQL);

        var json = DabConfigGenerator.Generate(request);
        using var doc = JsonDocument.Parse(json);

        var restEnabled = doc.RootElement.GetProperty("runtime").GetProperty("rest").GetProperty("enabled").GetBoolean();
        var graphqlEnabled = doc.RootElement.GetProperty("runtime").GetProperty("graphql").GetProperty("enabled").GetBoolean();
        var mcpEnabled = doc.RootElement.GetProperty("runtime").GetProperty("mcp").GetProperty("enabled").GetBoolean();

        Assert.Equal(enableRest, restEnabled);
        Assert.Equal(enableGraphQL, graphqlEnabled);
        Assert.True(mcpEnabled);
    }
}
