using Emd.Core.Model;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class FirmwarePinMapVerifierTests
{
    [Fact]
    public void Example_has_no_firmware_pin_map_errors()
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid);

        var diagnostics = FirmwarePinMapVerifier.Default.Validate(loaded.Project!);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == Emd.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Pin_targeting_another_board_is_reported()
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        var firmware = loaded.Project!.Firmware!.Projects[0];
        var otherBoard = loaded.Project.Parts.First(part =>
            part.Kind == PartKind.Board && part.Id != firmware.BoardPartId);
        var project = loaded.Project with
        {
            Firmware = new FirmwareSection
            {
                Projects =
                [
                    firmware with
                    {
                        PinMap =
                        [
                            new PinBinding
                            {
                                McuPin = "PA0",
                                NetId = firmware.PinMap[0].NetId,
                                BoardPartId = otherBoard.Id,
                                PortId = "step"
                            }
                        ]
                    }
                ]
            }
        };

        var diagnostics = FirmwarePinMapVerifier.Default.Validate(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-FW-004");
    }
}
