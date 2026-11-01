using Emd.Adapters;
using Emd.Cli;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class RenodeCommandTests
{
    [Fact]
    public void Run_uses_project_firmware_artifacts()
    {
        using var temp = new TempDirectory();
        var firmware = Path.Combine(temp.Path, "axis.elf");
        var platform = Path.Combine(temp.Path, "axis.repl");
        var log = Path.Combine(temp.Path, "renode.log");
        var projectPath = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(firmware, "elf");
        File.WriteAllText(platform, "platform");
        var firmwareArtifact = Guid.NewGuid();
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "firmware project",
            Parts = [new Part { Id = Guid.NewGuid(), Name = "mcu", Kind = PartKind.Board, Ports = [] }],
            Artifacts =
            [
                new Artifact
                {
                    Id = firmwareArtifact,
                    Key = "firmware",
                    Path = firmware,
                    Format = "elf",
                    Role = ArtifactRole.Generated
                }
            ],
            Firmware = new FirmwareSection
            {
                Projects =
                [
                    new FirmwareProject
                    {
                        Id = Guid.NewGuid(),
                        Key = "axis_fw",
                        SourceArtifactId = firmwareArtifact,
                        BinaryArtifactId = firmwareArtifact,
                        BoardPartId = Guid.NewGuid(),
                        RenodePlatform = platform
                    }
                ]
            }
        };
        ProjectWriter.Save(project, projectPath);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RenodeCommand.Run(
            ["run", "--project", projectPath, "--out", log],
            stdout, stderr, new RenodeAdapter(new Runner()));

        Assert.True(exitCode == 0, stderr.ToString());
        Assert.True(File.Exists(log));
        Assert.Contains("renode:", stdout.ToString());
    }

    private sealed class Runner : IToolProcessRunner
    {
        public Task<ToolProcessResult> RunAsync(
            string executable, IEnumerable<string>? arguments = null, string? stdin = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolProcessResult(0, "uart: boot", "", []));
    }
}
