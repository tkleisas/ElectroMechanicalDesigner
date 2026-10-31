using Emd.Core.Diagnostics;
using Emd.Core.Projects;
using Emd.Core.Simulation;

namespace Emd.Cli;

public static class TraceCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || !string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
            return Usage(stderr, "trace expects the 'check' subcommand.");

        var projectPath = GetOption(args, "--project");
        var tracePath = GetOption(args, "--trace");
        var scenario = GetOption(args, "--scenario");
        if (string.IsNullOrWhiteSpace(projectPath))
            return Usage(stderr, "trace check requires --project <project.json>.");
        if (string.IsNullOrWhiteSpace(tracePath))
            return Usage(stderr, "trace check requires --trace <trace.csv>.");

        var project = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(project.Diagnostics, stderr);
        if (project.Project is null)
            return 1;

        var trace = TraceCsvLoader.Default.LoadFile(tracePath);
        PrintDiagnostics(trace.Diagnostics, stderr);
        if (trace.Trace is null)
            return 1;

        var result = TraceAssertionEvaluator.Default.Evaluate(project.Project, trace.Trace, scenario);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (!result.Passed)
            return 1;

        stdout.WriteLine(
            $"trace: {trace.Trace.Rows.Count} rows, scenario '{scenario ?? "all"}' passed");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
                return argument[prefix.Length..];
            if (argument == name && index + 1 < args.Length)
                return args[index + 1];
        }

        return null;
    }

    private static void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter writer)
    {
        foreach (var diagnostic in diagnostics)
            writer.WriteLine(diagnostic);
    }

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
