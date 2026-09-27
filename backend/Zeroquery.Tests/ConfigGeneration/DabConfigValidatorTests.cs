using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;

namespace Zeroquery.Tests.ConfigGeneration;

public class DabConfigValidatorTests
{
    [Fact]
    public void Validate_ReturnsNoErrors_ForGeneratorOutput()
    {
        var request = new ConfigGenerationRequest(
            DatabaseProvider.SqlServer,
            "ZQ_DB_CONN",
            new[]
            {
                new EntitySelectionRequest(
                    "dbo", "Orders", IsView: false,
                    Columns: new[]
                    {
                        new ColumnSelectionRequest("Id", Include: true, IsPrimaryKey: true),
                        new ColumnSelectionRequest("CustomerName", Include: true, IsPrimaryKey: false)
                    },
                    Description: "Customer orders")
            });

        var json = DabConfigGenerator.Generate(request);

        var validator = new DabConfigValidator();
        var errors = validator.Validate(json);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_ForMalformedJson()
    {
        var validator = new DabConfigValidator();
        var errors = validator.Validate("{ not valid json");

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_WhenRequiredTopLevelPropertyMissing()
    {
        var validator = new DabConfigValidator();
        // Missing "data-source" and "entities", both required by DAB's schema.
        var errors = validator.Validate("""{ "$schema": "https://example.com/schema.json" }""");

        Assert.NotEmpty(errors);
    }
}
