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
    public void Unconnected_electrical_port_is_reported_as_EMD_ELEC_004()
    {
        var project = Mutate(root =>
        {
            var connections = root["connections"]!.AsArray();
            var driverPhaseA = connections.First(c => (string?)c!["key"] == "driver_phase_a");
            connections.Remove(driverPhaseA);
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ELEC-004");
    }

    [Fact]
    public void Duplicate_electrical_refdes_is_reported_as_EMD_XREF_002()
    {
        var project = Mutate(root =>
            root["parts"]![1]!["domain_refs"]!["electrical"]!["ref"] = "A1");

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-XREF-002");
    }

    [Fact]
    public void Motor_phase_without_a_board_endpoint_is_reported_as_EMD_ELEC_006()
    {
        var project = Mutate(root =>
        {
            var connections = root["connections"]!.AsArray();
            var driverPhaseA = connections.First(c => (string?)c!["key"] == "driver_phase_a");
            connections.Remove(driverPhaseA);
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ELEC-006");
    }

    [Fact]
    public void Net_with_two_motor_phases_is_reported_as_EMD_ELEC_007()
    {
        var project = Mutate(root =>
        {
            var connection = root["connections"]!.AsArray()
                .First(c => (string?)c!["key"] == "motor_phase_b");
            connection!["to"]!["net_id"] = root["electrical"]!["nets"]!.AsArray()
                .First(n => (string?)n!["key"] == "m1a")!["id"]!.GetValue<string>();
        });

        var diagnostics = _validator.Validate(project);

        var diagnostic = Assert.Single(diagnostics, d => d.Code == "EMD-ELEC-007");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Valid_example_has_no_motor_topology_warnings()
    {
        var diagnostics = _validator.Validate(LoadExample());

        Assert.DoesNotContain(diagnostics, d => d.Code == "EMD-ELEC-006");
    }

    [Fact]
    public void Net_voltage_mismatch_is_reported_as_EMD_ELEC_005()
    {
        var project = Mutate(root =>
        {
            var net = root["electrical"]!["nets"]!.AsArray()
                .First(candidate => (string?)candidate!["key"] == "step");
            net!["voltage_v"] = 5.0;
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ELEC-005");
    }

    [Fact]
    public void Firmware_pin_on_the_wrong_board_port_net_is_reported_as_EMD_FW_003()
    {
        var project = Mutate(root =>
            root["firmware"]!["projects"]![0]!["pin_map"]![0]!["port_id"] = "dir");

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-FW-003");
    }

    [Fact]
    public void Duplicate_firmware_pin_is_reported_as_EMD_FW_002()
    {
        var project = Mutate(root =>
            root["firmware"]!["projects"]![0]!["pin_map"]![1]!["mcu_pin"] = "PA0");

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-FW-002");
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
    public void Simulation_binding_to_undeclared_variable_is_reported_as_EMD_SIM_003()
    {
        var project = Mutate(root =>
        {
            var binding = root["connections"]!.AsArray()
                .First(c => (string?)c!["key"] == "motor_shaft_twin");
            binding!["to"]!["variable"] = "missing.angle";
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-SIM-003");
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
    public void Mesh_geometry_with_missing_artifact_is_reported_as_EMD_ART_001()
    {
        var project = Mutate(root =>
            root["parts"]![1]!["geometry"] = new JsonObject
            {
                ["kind"] = "mesh",
                ["mesh_artifact_id"] = Guid.NewGuid().ToString()
            });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ART-001");
    }

    [Fact]
    public void Mechanical_joint_with_missing_parent_is_reported_as_EMD_MECH_004()
    {
        var project = Mutate(root =>
            root["mechanical"]!["joints"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["key"] = "bad_joint",
                    ["type"] = "slide",
                    ["parent_part_id"] = Guid.NewGuid().ToString(),
                    ["child_part_id"] = root["parts"]![1]!["id"]!.GetValue<string>(),
                    ["axis"] = new JsonArray(1, 0, 0)
                }
            });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-MECH-004");
    }

    [Fact]
    public void Duplicate_mechanical_joint_id_is_reported_as_EMD_ID_001()
    {
        var project = Mutate(root =>
        {
            var jointId = Guid.NewGuid().ToString();
            root["mechanical"]!["joints"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = jointId,
                    ["key"] = "joint_a",
                    ["type"] = "slide",
                    ["parent_part_id"] = root["parts"]![0]!["id"]!.GetValue<string>(),
                    ["child_part_id"] = root["parts"]![1]!["id"]!.GetValue<string>(),
                    ["axis"] = new JsonArray(1, 0, 0)
                },
                new JsonObject
                {
                    ["id"] = jointId,
                    ["key"] = "joint_b",
                    ["type"] = "hinge",
                    ["parent_part_id"] = root["parts"]![0]!["id"]!.GetValue<string>(),
                    ["child_part_id"] = root["parts"]![1]!["id"]!.GetValue<string>(),
                    ["axis"] = new JsonArray(0, 0, 1)
                }
            };
        });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-ID-001");
    }

    [Fact]
    public void Hinge_joint_with_zero_axis_is_reported_as_EMD_MECH_004()
    {
        var project = Mutate(root =>
            root["mechanical"]!["joints"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["key"] = "bad_axis",
                    ["type"] = "hinge",
                    ["parent_part_id"] = root["parts"]![0]!["id"]!.GetValue<string>(),
                    ["child_part_id"] = root["parts"]![1]!["id"]!.GetValue<string>(),
                    ["axis"] = new JsonArray(0, 0, 0)
                }
            });

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-MECH-004");
    }

    [Fact]
    public void Missing_scenario_trace_artifact_is_reported_as_EMD_SIM_004()
    {
        var project = Mutate(root =>
            root["simulation"]!["scenarios"]![0]!["trace_artifact_id"] = Guid.NewGuid().ToString());

        var diagnostics = _validator.Validate(project);

        Assert.Contains(diagnostics, d => d.Code == "EMD-SIM-004");
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
