namespace Emd.Adapters.MechMaker;

public sealed record MechMakerToolResult(bool IsError, string Text);

public interface IMechMakerToolClient
{
    Task<MechMakerToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default);
}
