namespace Emd.Adapters.Tests;

internal sealed class FakeToolProcessRunner : IToolProcessRunner
{
    private readonly ToolProcessResult _result;

    public FakeToolProcessRunner(ToolProcessResult result)
    {
        _result = result;
    }

    public string? Executable { get; private set; }

    public string[] Arguments { get; private set; } = [];

    public string? Stdin { get; private set; }

    public Task<ToolProcessResult> RunAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? stdin = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Executable = executable;
        Arguments = (arguments ?? []).ToArray();
        Stdin = stdin;
        return Task.FromResult(_result);
    }
}
