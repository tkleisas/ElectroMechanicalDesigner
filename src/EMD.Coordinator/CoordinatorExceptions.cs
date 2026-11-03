namespace Emd.Coordinator;

public sealed class CoordinatorConflictException(string message) : Exception(message);

public sealed class CoordinatorNotFoundException(string message) : Exception(message);
