namespace Zeroquery.Core.ConfigGeneration;

/// <summary>
/// Orchestrates generation + schema validation of a dab-config.json in one call, so API/UI
/// callers get both the config text and a pass/fail verdict without wiring the generator and
/// validator together themselves (see doc/Plan.md Section 2.3 — "fail fast" requirement).
/// </summary>
public sealed class DabConfigService
{
    private readonly DabConfigValidator _validator;

    public DabConfigService(DabConfigValidator validator)
    {
        _validator = validator;
    }

    public ConfigGenerationResult GenerateAndValidate(ConfigGenerationRequest request)
    {
        var json = DabConfigGenerator.Generate(request);
        var errors = _validator.Validate(json);
        return new ConfigGenerationResult(json, errors.Count == 0, errors);
    }
}
