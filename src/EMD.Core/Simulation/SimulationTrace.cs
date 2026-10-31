namespace Emd.Core.Simulation;

public sealed record SimulationTrace(
    IReadOnlyList<string> Signals,
    IReadOnlyList<IReadOnlyDictionary<string, double>> Rows);
