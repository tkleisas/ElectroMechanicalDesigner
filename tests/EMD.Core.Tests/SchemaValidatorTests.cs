using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class SchemaValidatorTests
{
    private readonly SchemaValidator _validator = SchemaValidator.Default;

    [Fact]
    public void Valid_example_has_no_diagnostics()
    {
        var diagnostics = _validator.Validate(File.ReadAllText(TestPaths.ExampleProject));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Missing_required_property_is_reported()
    {
        var root = JsonNode.Parse(File.ReadAllText(TestPaths.ExampleProject))!.AsObject();
        root.Remove("name");

        var diagnostics = _validator.Validate(root.ToJsonString());

        var error = Assert.Single(diagnostics, d => d.Code == "EMD-SCHEMA-001");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_property_is_reported()
    {
        var root = JsonNode.Parse(File.ReadAllText(TestPaths.ExampleProject))!.AsObject();
        root["bogus"] = true;

        var diagnostics = _validator.Validate(root.ToJsonString());

        Assert.Contains(diagnostics, d => d.Code == "EMD-SCHEMA-001");
    }

    [Fact]
    public void Model_port_connector_type_is_validated()
    {
        var root = JsonNode.Parse(File.ReadAllText(TestPaths.ExampleProject))!.AsObject();
        var input = root["behavior"]!["models"]![0]!["inputs"]![0]!.AsObject();
        input["type"] = "boolean";

        var diagnostics = _validator.Validate(root.ToJsonString());

        Assert.Empty(diagnostics);

        input["type"] = "matrix";

        diagnostics = _validator.Validate(root.ToJsonString());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-SCHEMA-001");
    }

    [Fact]
    public void Wrong_property_type_is_reported()
    {
        var root = JsonNode.Parse(File.ReadAllText(TestPaths.ExampleProject))!.AsObject();
        root["name"] = 42;

        var diagnostics = _validator.Validate(root.ToJsonString());

        Assert.Contains(diagnostics, d => d.Code == "EMD-SCHEMA-001");
    }
}
