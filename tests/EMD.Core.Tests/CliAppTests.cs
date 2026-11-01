using Emd.Cli;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class CliAppTests
{
    [Fact]
    public void Validate_example_returns_zero_and_prints_a_summary()
    {
        var (exitCode, stdout, stderr) = Run("validate", TestPaths.ExampleProject);

        Assert.Equal(0, exitCode);
        Assert.Contains("valid:", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMD-", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_invalid_json_returns_one_and_reports_the_code()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bad.json");
        File.WriteAllText(path, "{ not json");

        var (exitCode, _, stderr) = Run("validate", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-JSON-001", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_mechmaker_writes_a_valid_project()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "imported.json");

        var (exitCode, stdout, stderr) = Run(
            "import", "mechmaker", TestPaths.Fixture("linear_axis.json"), "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("imported:", stdout, StringComparison.OrdinalIgnoreCase);

        var loaded = ProjectLoader.Default.LoadFile(output);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(3, loaded.Project!.Parts.Count);
    }

    [Fact]
    public void Import_kicad_writes_a_valid_project()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "netlist_import.json");

        var (exitCode, stdout, stderr) = Run(
            "import", "kicad", TestPaths.Fixture("kicad_netlist.xml"), "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("imported:", stdout, StringComparison.OrdinalIgnoreCase);

        var loaded = ProjectLoader.Default.LoadFile(output);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(2, loaded.Project!.Parts.Count);
    }

    [Fact]
    public void Import_freecad_adds_an_artifact_to_a_project()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(MinimalProject(), projectPath);
        var stepPath = Path.Combine(temp.Path, "axis.step");
        File.WriteAllText(stepPath, "ISO-10303-21;");

        var (exitCode, stdout, stderr) = Run(
            "import", "freecad", stepPath, "--project", projectPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("added:", stdout, StringComparison.OrdinalIgnoreCase);
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        var artifact = Assert.Single(loaded.Project!.Artifacts);
        Assert.Equal(artifact.Id, loaded.Project.Mechanical!.Exports!["step"]);
    }

    [Fact]
    public void Import_freecad_manifest_writes_a_valid_project()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "manifest_project.json");

        var (exitCode, stdout, stderr) = Run(
            "import", "freecad-manifest", TestPaths.Fixture("freecad_manifest.json"), "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("imported:", stdout, StringComparison.OrdinalIgnoreCase);

        var loaded = ProjectLoader.Default.LoadFile(output);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(2, loaded.Project!.Parts.Count);
        Assert.Single(loaded.Project.Mechanical!.Joints!);
    }

    [Fact]
    public void Export_mjcf_writes_a_scene()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "manifest_project.json");
        var imported = FreeCadManifestImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("freecad_manifest.json")), "axis.FCStd");
        ProjectWriter.Save(imported.Project!, projectPath);
        var scenePath = Path.Combine(temp.Path, "axis.xml");

        var (exitCode, stdout, stderr) = Run(
            "export", "mjcf", "--project", projectPath, "--out", scenePath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(scenePath), stderr);
        Assert.Contains("exported:", stdout, StringComparison.OrdinalIgnoreCase);
        var scene = System.Xml.Linq.XDocument.Load(scenePath);
        Assert.Equal("mujoco", scene.Root!.Name.LocalName);
        Assert.Contains(scene.Descendants("body"), body => (string?)body.Attribute("name") == "frame");
    }

    [Fact]
    public void Import_kicad_pcb_adds_a_board_part()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(MinimalProject(), projectPath);

        var (exitCode, stdout, stderr) = Run(
            "import", "kicad-pcb", TestPaths.Fixture("board.kicad_pcb"), "--project", projectPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("imported PCB:", stdout, StringComparison.OrdinalIgnoreCase);
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        var board = loaded.Project!.Parts.Single(part => part.Kind == PartKind.Board);
        Assert.Equal(2, board.Ports.Count);
        Assert.Single(loaded.Project.Electrical!.Boards);
    }

    [Fact]
    public void Import_kicad_step_attaches_to_the_board_part()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        var boardId = Guid.NewGuid();
        var project = MinimalProject() with
        {
            Parts =
            [
                .. MinimalProject().Parts,
                new Part
                {
                    Id = boardId,
                    Key = "board",
                    Name = "Board",
                    Kind = PartKind.Board,
                    Ports = []
                }
            ]
        };
        ProjectWriter.Save(project, projectPath);
        var stepPath = Path.Combine(temp.Path, "board.step");
        File.WriteAllText(stepPath, """
            ISO-10303-21;
            HEADER;
            FILE_NAME('board.step','2026-11-01T00:00:00',(''),(''),'','','');
            FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));
            ENDSEC;
            DATA;
            #1=CARTESIAN_POINT('',(0.,0.,0.));
            #2=CARTESIAN_POINT('',(100.,50.,1.6));
            ENDSEC;
            END-ISO-10303-21;
            """);

        var (exitCode, stdout, stderr) = Run(
            "import", "kicad-step", "--step", stepPath, "--project", projectPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("imported STEP:", stdout);
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        var board = loaded.Project!.Parts.Single(part => part.Id == boardId);
        Assert.Equal("board.step", board.DomainRefs!.Mechanical!.Ref);
        Assert.NotNull(loaded.Project.Mechanical!.Exports);
        Assert.True(loaded.Project.Mechanical.Exports!.ContainsKey("step"));
        Assert.Contains(loaded.Project.Artifacts, artifact => artifact.Format == "step");
    }

    [Fact]
    public void Export_mechmaker_writes_a_machine_json()
    {
        using var temp = new TempDirectory();
        var imported = MechMakerImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("linear_axis.json")), "linear_axis.json");
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(imported.Project!, projectPath);
        var machinePath = Path.Combine(temp.Path, "machine.json");

        var (exitCode, stdout, stderr) = Run(
            "export", "mechmaker", "--project", projectPath, "--out", machinePath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(machinePath), stderr);
        Assert.Contains("exported MechMaker", stdout, StringComparison.OrdinalIgnoreCase);
        var roundtrip = MechMakerImporter.Default.Import(File.ReadAllText(machinePath), "machine.json");
        Assert.Empty(roundtrip.Diagnostics);
        Assert.Equal(3, roundtrip.Project!.Parts.Count);
    }

    [Fact]
    public void Package_writes_a_reproducible_bundle()
    {
        using var temp = new TempDirectory();
        var outputDir = Path.Combine(temp.Path, "package");

        var (exitCode, stdout, stderr) = Run(
            "package", "--project", TestPaths.ExampleProject, "--out-dir", outputDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("packaged:", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(outputDir, "project.json")), stderr);
        Assert.True(File.Exists(Path.Combine(outputDir, "bom.csv")), stderr);
        Assert.True(File.Exists(Path.Combine(outputDir, "scene.xml")), stderr);
        Assert.True(File.Exists(Path.Combine(outputDir, "manifest.json")), stderr);
    }

    [Fact]
    public void Package_verify_accepts_a_bundle()
    {
        using var temp = new TempDirectory();
        var outputDir = Path.Combine(temp.Path, "package");

        var (packageExit, _, packageError) = Run(
            "package", "--project", TestPaths.ExampleProject, "--out-dir", outputDir);
        Assert.Equal(0, packageExit);

        var (verifyExit, stdout, stderr) = Run("package", "verify", "--dir", outputDir);

        Assert.Equal(0, verifyExit);
        Assert.Contains("verified package:", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(stderr), packageError + stderr);
    }

    [Fact]
    public void Trace_check_accepts_a_passing_trace()
    {
        using var temp = new TempDirectory();
        var tracePath = Path.Combine(temp.Path, "trace.csv");
        File.WriteAllText(tracePath, "time,x_carriage_m\n0,0\n1,0.0405\n");

        var (exitCode, stdout, stderr) = Run(
            "trace", "check", "--project", TestPaths.ExampleProject,
            "--trace", tracePath, "--scenario", "move_40mm");

        Assert.Equal(0, exitCode);
        Assert.Contains("trace:", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMD-TRACE-003", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_reports_an_added_part()
    {
        using var temp = new TempDirectory();
        var beforePath = Path.Combine(temp.Path, "before.json");
        var afterPath = Path.Combine(temp.Path, "after.json");
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        var before = loaded.Project!;
        var after = before with
        {
            Parts =
            [
                .. before.Parts,
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "extra",
                    Name = "Extra",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ]
        };
        ProjectWriter.Save(before, beforePath);
        ProjectWriter.Save(after, afterPath);

        var (exitCode, stdout, stderr) = Run(
            "diff", "--before", beforePath, "--after", afterPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("added part", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extra", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(stderr), stderr);
    }

    [Fact]
    public void Package_includes_a_trace_file()
    {
        using var temp = new TempDirectory();
        var outputDir = Path.Combine(temp.Path, "package");
        var trace = Path.Combine(temp.Path, "run.csv");
        File.WriteAllText(trace, "time,x\n0,0\n");

        var (exitCode, _, stderr) = Run(
            "package", "--project", TestPaths.ExampleProject, "--out-dir", outputDir,
            "--trace", trace);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outputDir, "traces", "run.csv")), stderr);
    }

    [Fact]
    public void Export_ssp_writes_an_archive()
    {
        using var temp = new TempDirectory();
        var controllerPath = Path.Combine(temp.Path, "controller.fmu");
        var plantPath = Path.Combine(temp.Path, "plant.fmu");
        File.WriteAllText(controllerPath, "controller");
        File.WriteAllText(plantPath, "plant");
        var controllerId = Guid.NewGuid();
        var plantId = Guid.NewGuid();
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "ssp cli",
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "frame",
                    Name = "Frame",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ],
            Artifacts =
            [
                new Artifact
                {
                    Id = controllerId,
                    Key = "controller_fmu",
                    Path = controllerPath,
                    Format = "fmu",
                    Role = ArtifactRole.Simulation
                },
                new Artifact
                {
                    Id = plantId,
                    Key = "plant_fmu",
                    Path = plantPath,
                    Format = "fmu",
                    Role = ArtifactRole.Simulation
                }
            ],
            Behavior = new BehaviorSection
            {
                Models =
                [
                    new BehaviorModel
                    {
                        Id = Guid.NewGuid(),
                        Key = "controller",
                        Kind = ModelKind.Fmu,
                        ArtifactId = controllerId,
                        Outputs = [new ModelPort { Name = "cmd", Variable = "cmd" }]
                    },
                    new BehaviorModel
                    {
                        Id = Guid.NewGuid(),
                        Key = "plant",
                        Kind = ModelKind.Fmu,
                        ArtifactId = plantId,
                        Inputs = [new ModelPort { Name = "cmd_in", Variable = "cmd_in" }]
                    }
                ]
            },
            Simulation = new SimulationSection
            {
                Systems =
                [
                    new SimSystem
                    {
                        Id = Guid.NewGuid(),
                        Key = "twin",
                        Standard = SimulationStandard.Ssp2,
                        Master = SimulationMaster.Omsimulator,
                        StepSizeS = 0.001,
                        StopTimeS = 1
                    }
                ]
            }
        };
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(project, projectPath);
        var output = Path.Combine(temp.Path, "twin.ssp");

        var (exitCode, stdout, stderr) = Run("export", "ssp", "--project", projectPath, "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("exported SSP", stdout, StringComparison.OrdinalIgnoreCase);
        using var archive = System.IO.Compression.ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, entry => entry.FullName == "SystemStructure.ssd");
    }

    [Fact]
    public void Export_bom_writes_a_csv()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "manifest_project.json");
        var imported = FreeCadManifestImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("freecad_manifest.json")), "axis.FCStd");
        ProjectWriter.Save(imported.Project!, projectPath);
        var bomPath = Path.Combine(temp.Path, "bom.csv");

        var (exitCode, stdout, stderr) = Run(
            "export", "bom", "--project", projectPath, "--out", bomPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(bomPath), stderr);
        Assert.Contains("exported BOM:", stdout, StringComparison.OrdinalIgnoreCase);
        var csv = File.ReadAllText(bomPath);
        Assert.Contains("item,quantity", csv, StringComparison.Ordinal);
        Assert.Contains("frame", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_harness_writes_wireviz_yaml()
    {
        using var temp = new TempDirectory();
        var harnessPath = Path.Combine(temp.Path, "harness.yml");

        var (exitCode, stdout, stderr) = Run(
            "export", "harness", "--project", TestPaths.ExampleProject, "--out", harnessPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(harnessPath), stderr);
        Assert.Contains("exported harness:", stdout, StringComparison.OrdinalIgnoreCase);
        var yaml = File.ReadAllText(harnessPath);
        Assert.Contains("connectors:", yaml, StringComparison.Ordinal);
        Assert.Contains("connections:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_without_out_returns_usage_error()
    {
        var (exitCode, _, stderr) = Run(
            "import", "mechmaker", TestPaths.Fixture("linear_axis.json"));

        Assert.Equal(2, exitCode);
        Assert.Contains("usage", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_plan_orders_artifact_dependencies()
    {
        var (exitCode, stdout, stderr) = Run("build", "plan", "--project", TestPaths.ExampleProject);

        Assert.Equal(0, exitCode);
        Assert.Contains("axis_fcstd", stdout, StringComparison.Ordinal);
        Assert.Contains("axis_step", stdout, StringComparison.Ordinal);
        Assert.True(
            stdout.IndexOf("axis_fcstd", StringComparison.Ordinal) <
            stdout.IndexOf("axis_step", StringComparison.Ordinal),
            stdout + stderr);
    }

    [Fact]
    public void Artifacts_verify_reports_a_missing_hash()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "artifact test",
            Artifacts =
            [
                new Artifact
                {
                    Id = Guid.NewGuid(),
                    Key = "missing",
                    Path = "files/missing.bin",
                    Format = "bin",
                    Role = ArtifactRole.Source,
                    Sha256 = new string('a', 64)
                }
            ],
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Name = "part",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ]
        };
        ProjectWriter.Save(project, projectPath);

        var (exitCode, _, stderr) = Run("artifacts", "verify", projectPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-ART-002", stderr, StringComparison.Ordinal);
    }

    private static EmdProject MinimalProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "CLI import test",
        Parts =
        [
            new Part
            {
                Id = Guid.NewGuid(),
                Name = "frame",
                Kind = PartKind.Component,
                Ports = []
            }
        ]
    };

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliApp.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
