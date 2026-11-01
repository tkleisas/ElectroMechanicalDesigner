using System.Text;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record AssemblyDocExportResult(
    string? Markdown,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class AssemblyDocExporter
{
    public static AssemblyDocExporter Default { get; } = new();

    public AssemblyDocExportResult Export(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var bom = BomExporter.Default.Export(project);
        diagnostics.AddRange(bom.Diagnostics);
        var harness = HarnessExporter.Default.Export(project);
        diagnostics.AddRange(harness.Diagnostics);

        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Name} assembly");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(project.Revision))
            builder.AppendLine($"Revision: `{project.Revision}`");
        if (!string.IsNullOrWhiteSpace(project.License))
            builder.AppendLine($"License: `{project.License}`");
        builder.AppendLine();

        builder.AppendLine("## Bill of materials");
        builder.AppendLine();
        if (bom.Csv is not null)
        {
            builder.AppendLine("```csv");
            builder.AppendLine(bom.Csv.TrimEnd());
            builder.AppendLine("```");
        }
        else
        {
            builder.AppendLine("_BOM unavailable._");
        }
        builder.AppendLine();

        builder.AppendLine("## Harness");
        builder.AppendLine();
        if (harness.Yaml is not null)
        {
            builder.AppendLine("```yaml");
            builder.AppendLine(harness.Yaml.TrimEnd());
            builder.AppendLine("```");
        }
        else
        {
            builder.AppendLine("_Harness unavailable._");
        }
        builder.AppendLine();

        builder.AppendLine("## Boards");
        builder.AppendLine();
        var boards = project.Electrical?.Boards ?? [];
        if (boards.Count == 0)
        {
            builder.AppendLine("_No boards._");
        }
        else
        {
            builder.AppendLine("| Refdes | Key | Layers | Stackup |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var board in boards)
            {
                builder.AppendLine(
                    $"| {board.Refdes ?? ""} | {board.Key ?? ""} | " +
                    $"{board.Layers?.ToString() ?? ""} | {board.Stackup ?? ""} |");
            }
        }
        builder.AppendLine();

        builder.AppendLine("## Manufacturing");
        builder.AppendLine();
        var processes = project.Manufacturing?.Processes ?? [];
        if (processes.Count == 0)
        {
            builder.AppendLine("_No manufacturing processes._");
        }
        else
        {
            builder.AppendLine("| Process | Kind | Postprocessor | Parameters |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var process in processes)
            {
                builder.AppendLine(
                    $"| {process.Name ?? process.Key} | {process.Kind} | " +
                    $"{process.Postprocessor ?? ""} | " +
                    $"{(process.Parameters is null ? "" : process.Parameters.ToJsonString())} |");
            }
        }
        builder.AppendLine();

        builder.AppendLine("## Firmware");
        builder.AppendLine();
        var firmware = project.Firmware?.Projects ?? [];
        if (firmware.Count == 0)
        {
            builder.AppendLine("_No firmware projects._");
        }
        else
        {
            foreach (var item in firmware)
            {
                builder.AppendLine($"- **{item.Name ?? item.Key}**: {item.Mcu ?? ""} " +
                                   $"({item.BuildSystem?.ToString() ?? "unknown build system"})");
                foreach (var pin in item.PinMap)
                    builder.AppendLine(
                        $"  - `{pin.McuPin}` -> `{pin.Function ?? pin.PortId ?? pin.NetId.ToString()}`");
            }
        }

        return new AssemblyDocExportResult(builder.ToString(), diagnostics);
    }
}
