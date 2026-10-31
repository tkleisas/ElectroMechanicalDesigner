using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class ProjectValidatorTests
{
    private readonly ProjectValidator _validator = ProjectValidator.Default;

    [Fact]
    public void Valid_example_has_no_errors()
    {
        var project = LoadExample();

        var diagnostics = _validator.Validate(project);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Connection_to_missing_part_is_reported_as_EMD_XREF_001()
    {
        var project = Mutate(root =>
            root["connections"]![0]!["to"]!["part_id"] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-XREF-001");
    }

    [Fact]
    public void Connection_to_missing_port_is_reported_as_EMD_XREF_001()
    {
        var project = Mutate(root =>
            root["connections"]![0]!["to"]!["port_id"] = "does_not_exist");

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-XREF-001");
    }

    [Fact]
    public void Dangling_net_is_reported_as_EMD_ELEC_001()
    {
        var project = Mutate(root =>
        {
            var connections = root["connections"]!.AsArray();
            var driverPhaseA = connections.First(c => (string?)c!["key"] == "driver_phase_a");
            connections.Remove(driverPhaseA);
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ELEC-001");
    }

    [Fact]
    public void Firmware_pin_map_with_missing_net_is_reported_as_EMD_FW_001()
    {
        var project = Mutate(root =>
            root["firmware"]!["projects"]![0]!["pin_map"]![0]!["net_id"] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-FW-001");
    }

    [Fact]
    public void Simulation_binding_with_missing_artifact_is_reported_as_EMD_SIM_001()
    {
        var project = Mutate(root =>
        {
            var binding = root["connections"]!.AsArray()
                .First(c => (string?)c!["key"] == "motor_shaft_twin");
            binding!["to"]!["artifact_id"] = Guid.NewGuid().ToString();
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-SIM-001");
    }

    [Fact]
    public void Simulation_system_with_unknown_binding_is_reported_as_EMD_SIM_001()
    {
        var project = Mutate(root =>
            root["simulation"]!["systems"]![0]!["bindings"]![0] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-SIM-001");
    }

    [Fact]
    public void Duplicate_part_id_is_reported_as_EMD_ID_001()
    {
        var project = Mutate(root =>
        {
            var parts = root["parts"]!.AsArray();
            parts.Add(parts[0]!.DeepClone());
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ID-001");
    }

    [Fact]
    public void Missing_mechanical_source_artifact_is_reported_as_EMD_ART_001()
    {
        var project = Mutate(root =>
            root["mechanical"]!["source_artifact_id"] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ART-001");
    }

    [Fact]
    public void Missing_mechanical_assembly_root_is_reported_as_EMD_MECH_001()
    {
        var project = Mutate(root =>
            root["mechanical"]!["assembly_root_part_id"] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-MECH-001");
    }

    private static EmdProject LoadExample()
    {
        var result = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Project!;
    }

    private static EmdProject Mutate(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(File.ReadAllText(TestPaths.ExampleProject))!.AsObject();
        mutate(root);

        var result = ProjectLoader.Default.Load(root.ToJsonString());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Project!;
    }
}
