namespace Emd.Adapters;

public interface IToolProcessRunner
{
    Task<ToolProcessResult> RunAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? stdin = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
