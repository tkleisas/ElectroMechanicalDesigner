namespace Emd.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Entity = null,
    string? Path = null)
{
    public override string ToString()
    {
        var location = string.IsNullOrWhiteSpace(Path) ? string.Empty : $" at {Path}";
        var entity = string.IsNullOrWhiteSpace(Entity) ? string.Empty : $" ({Entity})";
        return $"{Code} [{Severity}]{location}{entity}: {Message}";
    }
}
