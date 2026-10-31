using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record KiCadImportResult(
    EmdProject? Project,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Project is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Imports a KiCad XML netlist (<c>kicad-cli sch export netlist --format kicadxml</c>).
/// Reference-designator power symbols (<c>#PWR...</c>) are treated as net metadata,
/// not parts. Deterministic UUIDs keep repeated imports stable.
/// </summary>
public sealed class KiCadNetlistImporter
{
    public static KiCadNetlistImporter Default { get; } = new();

    public KiCadImportResult ImportFile(string path)
    {
        try
        {
            return Import(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new KiCadImportResult(null,
            [
                new Diagnostic("EMD-IO-001", DiagnosticSeverity.Error,
                    $"Could not read KiCad netlist '{path}': {exception.Message}", Entity: path)
            ]);
        }
    }

    public KiCadImportResult Import(string xml, string? source = null)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(
                new StringReader(xml),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            return new KiCadImportResult(null,
            [
                new Diagnostic("EMD-KICAD-001", DiagnosticSeverity.Error,
                    $"Invalid KiCad XML netlist: {exception.Message}", Entity: source)
            ]);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "export")
        {
            return new KiCadImportResult(null,
            [
                new Diagnostic("EMD-KICAD-001", DiagnosticSeverity.Error,
                    "XML is not a KiCad netlist: missing <export> root.", Entity: source)
            ]);
        }

        var diagnostics = new List<Diagnostic>();
        var ns = EmdNamespaces.KiCad;
        var design = root.Element("design");
        var designSource = design?.Element("source")?.Value;
        var name = ProjectName(designSource, source);
        var partsByRef = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var partPorts = new Dictionary<string, Dictionary<string, Port>>(StringComparer.Ordinal);
        var components = new List<ComponentInfo>();

        foreach (var component in root.Element("components")?.Elements("comp") ?? [])
        {
            var reference = ((string?)component.Attribute("ref"))?.Trim();
            if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('#'))
                continue;

            if (!partsByRef.TryAdd(reference, DeterministicGuid.Create(ns, $"component:{reference}")))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-KICAD-003",
                    DiagnosticSeverity.Error,
                    $"KiCad component reference '{reference}' appears more than once.",
                    Entity: reference));
                continue;
            }

            partPorts[reference] = new Dictionary<string, Port>(StringComparer.Ordinal);
            components.Add(new ComponentInfo(
                reference,
                component.Element("value")?.Value,
                component.Element("footprint")?.Value,
                component.Element("libsource")?.Attribute("part")?.Value));
        }

        var nets = new List<Net>();
        var connections = new List<Connection>();
        var netIndex = 0;

        foreach (var netElement in root.Element("nets")?.Elements("net") ?? [])
        {
            netIndex++;
            var code = ((string?)netElement.Attribute("code"))?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                code = netIndex.ToString();

            var netName = ((string?)netElement.Attribute("name"))?.Trim();
            if (string.IsNullOrWhiteSpace(netName))
                netName = $"Net-{code}";

            var nodes = netElement.Elements("node")
                .Select(node => new NodeInfo(
                    ((string?)node.Attribute("ref"))?.Trim() ?? "",
                    ((string?)node.Attribute("pin"))?.Trim() ?? "",
                    ((string?)node.Attribute("pinfunction"))?.Trim()))
                .Where(node =>
                    !string.IsNullOrWhiteSpace(node.Ref) &&
                    !node.Ref.StartsWith('#') &&
                    !string.IsNullOrWhiteSpace(node.Pin))
                .DistinctBy(node => (node.Ref, node.Pin))
                .ToList();

            if (nodes.Count == 0)
                continue;

            var netClass = ClassifyNet(netName);
            var netId = DeterministicGuid.Create(ns, $"net:{code}:{netName}");
            nets.Add(new Net
            {
                Id = netId,
                Key = Slug.Key(netName, $"net_{code}"),
                Name = netName,
                Class = netClass
            });

            foreach (var node in nodes)
            {
                if (!partsByRef.TryGetValue(node.Ref, out var partId))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-KICAD-002",
                        DiagnosticSeverity.Error,
                        $"KiCad net '{netName}' references missing component '{node.Ref}'.",
                        Entity: node.Ref));
                    continue;
                }

                var portId = EnsurePinPort(partPorts[node.Ref], node, netClass);
                connections.Add(new Connection
                {
                    Id = DeterministicGuid.Create(ns, $"node:{code}:{node.Ref}:{node.Pin}"),
                    Key = Slug.Key($"{netName}.{node.Ref}.{node.Pin}", "node"),
                    Kind = ConnectionKind.Wire,
                    From = new Endpoint { PartId = partId, PortId = portId },
                    To = new Endpoint { NetId = netId }
                });
            }
        }

        var parts = new List<Part>();
        foreach (var component in components)
        {
            var metadata = new JsonObject();
            if (!string.IsNullOrWhiteSpace(component.Value))
                metadata["value"] = component.Value;
            if (!string.IsNullOrWhiteSpace(component.Footprint))
                metadata["footprint"] = component.Footprint;
            if (!string.IsNullOrWhiteSpace(component.LibraryPart))
                metadata["libsource"] = component.LibraryPart;

            parts.Add(new Part
            {
                Id = partsByRef[component.Reference],
                Key = Slug.Key(component.Reference, "part"),
                Name = string.IsNullOrWhiteSpace(component.Value) ? component.Reference : component.Value,
                Kind = PartKind.Component,
                Category = PartCategory.Electronics,
                CatalogRef = new PartCatalogRef
                {
                    Catalog = "kicad",
                    Part = string.IsNullOrWhiteSpace(component.LibraryPart)
                        ? component.Reference
                        : component.LibraryPart
                },
                DomainRefs = new PartDomainRefs
                {
                    Electrical = new DomainRef { Tool = "kicad", Ref = component.Reference }
                },
                Ports = partPorts[component.Reference].Values
                    .OrderBy(port => port.Id, StringComparer.Ordinal)
                    .ToList(),
                Metadata = metadata.Count == 0 ? null : metadata
            });
        }

        var projectMetadata = new JsonObject
        {
            ["source"] = "kicad",
            ["source_name"] = name
        };
        if (!string.IsNullOrWhiteSpace(source))
            projectMetadata["source_path"] = source;
        var tool = design?.Element("tool")?.Value;
        if (!string.IsNullOrWhiteSpace(tool))
            projectMetadata["tool"] = tool;
        var date = design?.Element("date")?.Value;
        if (!string.IsNullOrWhiteSpace(date))
            projectMetadata["date"] = date;
        var netlistVersion = (string?)root.Attribute("version");
        if (!string.IsNullOrWhiteSpace(netlistVersion))
            projectMetadata["netlist_version"] = netlistVersion;

        var project = new EmdProject
        {
            Id = DeterministicGuid.Create(ns, $"project:{source ?? name}"),
            Key = Slug.Create(name),
            Name = name,
            Description = $"Imported from KiCad netlist for '{name}'.",
            License = "GPL-3.0-only",
            Units = new Units(),
            CoordinateSystem = new CoordinateSystem(),
            Parts = parts,
            Connections = connections,
            Electrical = new ElectricalSection
            {
                Nets = nets.OrderBy(net => net.Key, StringComparer.Ordinal).ToList(),
                Boards = []
            },
            Metadata = projectMetadata
        };

        return new KiCadImportResult(project, diagnostics);
    }

    private static string ProjectName(string? designSource, string? source)
    {
        if (!string.IsNullOrWhiteSpace(designSource))
        {
            var fromDesign = Path.GetFileNameWithoutExtension(designSource);
            if (!string.IsNullOrWhiteSpace(fromDesign))
                return fromDesign;
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var fromSource = Path.GetFileNameWithoutExtension(source);
            if (!string.IsNullOrWhiteSpace(fromSource))
                return fromSource;
        }

        return "kicad_netlist";
    }

    private static string EnsurePinPort(
        Dictionary<string, Port> ports,
        NodeInfo node,
        NetClass netClass)
    {
        var id = Slug.Key(node.Pin, "pin");
        ports.TryGetValue(id, out var existing);
        ports[id] = new Port
        {
            Id = id,
            Key = node.Pin,
            Name = string.IsNullOrWhiteSpace(node.Function)
                ? existing?.Name ?? node.Pin
                : node.Function,
            Domain = PortDomain.Electrical,
            Kind = "pin",
            Direction = PortDirection.Inout,
            Signal = new Signal { Type = SignalTypeFor(netClass) }
        };
        return id;
    }

    private static NetClass ClassifyNet(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("gnd", StringComparison.Ordinal) ||
            normalized.Contains("ground", StringComparison.Ordinal) ||
            normalized is "vss" or "0v")
            return NetClass.Ground;

        if (normalized.Contains("vcc", StringComparison.Ordinal) ||
            normalized.Contains("vdd", StringComparison.Ordinal) ||
            normalized.Contains("vmot", StringComparison.Ordinal) ||
            normalized.Contains("vbus", StringComparison.Ordinal) ||
            normalized.Contains("+3v3", StringComparison.Ordinal) ||
            normalized.Contains("+5v", StringComparison.Ordinal) ||
            normalized.Contains("+12v", StringComparison.Ordinal) ||
            normalized.Contains("+24v", StringComparison.Ordinal))
            return NetClass.Power;

        if (normalized.Contains("can", StringComparison.Ordinal) ||
            normalized.Contains("uart", StringComparison.Ordinal) ||
            normalized.Contains("i2c", StringComparison.Ordinal) ||
            normalized.Contains("scl", StringComparison.Ordinal) ||
            normalized.Contains("sda", StringComparison.Ordinal) ||
            normalized.Contains("spi", StringComparison.Ordinal) ||
            normalized.Contains("mosi", StringComparison.Ordinal) ||
            normalized.Contains("miso", StringComparison.Ordinal) ||
            normalized.Contains("sck", StringComparison.Ordinal) ||
            normalized.Contains("usb", StringComparison.Ordinal))
            return NetClass.Bus;

        return NetClass.Signal;
    }

    private static SignalType SignalTypeFor(NetClass netClass) => netClass switch
    {
        NetClass.Ground => SignalType.Ground,
        NetClass.Power => SignalType.Power,
        _ => SignalType.Digital
    };

    private sealed record ComponentInfo(
        string Reference,
        string? Value,
        string? Footprint,
        string? LibraryPart);

    private sealed record NodeInfo(string Ref, string Pin, string? Function);
}
