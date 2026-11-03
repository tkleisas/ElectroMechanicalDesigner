using System.Globalization;
using Emd.Contracts;
using Microsoft.Data.Sqlite;

namespace Emd.Coordinator;

public sealed class CoordinatorService
{
    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public CoordinatorService(string rootPath, TimeProvider? time = null)
    {
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        _time = time ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(RootPath, "coordinator.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        Initialize().GetAwaiter().GetResult();
    }

    public string RootPath { get; }

    public async Task<WorkspaceSummary> CreateWorkspaceAsync(
        CreateWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var connection = await OpenAsync(cancellationToken);
        var now = _time.GetUtcNow();
        var workspace = new WorkspaceSummary(
            Guid.NewGuid(), request.Key, request.Name, request.Repository, "main", now, now);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspaces
              (id, workspace_key, name, repository, revision, created_at, updated_at)
            VALUES ($id, $key, $name, $repository, $revision, $created, $updated);
            """;
        command.Parameters.AddWithValue("$id", workspace.Id.ToString());
        command.Parameters.AddWithValue("$key", workspace.Key);
        command.Parameters.AddWithValue("$name", workspace.Name);
        command.Parameters.AddWithValue("$repository", workspace.Repository);
        command.Parameters.AddWithValue("$revision", workspace.Revision);
        command.Parameters.AddWithValue("$created", Timestamp(workspace.CreatedAt));
        command.Parameters.AddWithValue("$updated", Timestamp(workspace.UpdatedAt));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new CoordinatorConflictException($"Workspace key '{request.Key}' already exists.");
        }

        return workspace;
    }

    public async Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await PurgeExpiredLeasesAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, workspace_key, name, repository, revision, created_at, updated_at
            FROM workspaces ORDER BY workspace_key;
            """;
        var result = new List<WorkspaceSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadWorkspace(reader));
        return result;
    }

    public async Task<WorkspaceDetail?> GetWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await PurgeExpiredLeasesAsync(connection, cancellationToken);
        var workspace = await FindWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (workspace is null)
            return null;
        var lease = await FindLeaseAsync(connection, workspaceId, cancellationToken);
        var revisions = await ReadRevisionsAsync(connection, workspaceId, cancellationToken);
        return new WorkspaceDetail(workspace, lease, revisions);
    }

    public async Task<WorkspaceLease> AcquireLeaseAsync(
        Guid workspaceId,
        AcquireLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Owner))
            throw new ArgumentException("Owner is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Branch))
            throw new ArgumentException("Branch is required.", nameof(request));
        if (request.TtlSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "TTL must be positive.");

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var workspace = await FindWorkspaceAsync(connection, workspaceId, cancellationToken, transaction)
            ?? throw new CoordinatorNotFoundException($"Workspace '{workspaceId}' was not found.");
        await PurgeExpiredLeasesAsync(connection, cancellationToken, transaction);
        var now = _time.GetUtcNow();
        var existing = await FindLeaseAsync(connection, workspaceId, cancellationToken, transaction);
        if (existing is not null)
        {
            if (existing.Owner != request.Owner || existing.Branch != request.Branch)
            {
                throw new CoordinatorConflictException(
                    $"Workspace '{workspace.Key}' is leased by '{existing.Owner}' on '{existing.Branch}'.");
            }

            var renewed = existing with { ExpiresAt = now.AddSeconds(request.TtlSeconds) };
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE leases SET expires_at = $expires WHERE id = $id;";
            update.Parameters.AddWithValue("$expires", Timestamp(renewed.ExpiresAt));
            update.Parameters.AddWithValue("$id", renewed.Id.ToString());
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return renewed;
        }

        var lease = new WorkspaceLease(
            Guid.NewGuid(), workspaceId, request.Owner.Trim(), request.Branch.Trim(),
            now, now.AddSeconds(request.TtlSeconds));
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO leases (id, workspace_id, owner, branch, acquired_at, expires_at)
            VALUES ($id, $workspace, $owner, $branch, $acquired, $expires);
            """;
        insert.Parameters.AddWithValue("$id", lease.Id.ToString());
        insert.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        insert.Parameters.AddWithValue("$owner", lease.Owner);
        insert.Parameters.AddWithValue("$branch", lease.Branch);
        insert.Parameters.AddWithValue("$acquired", Timestamp(lease.AcquiredAt));
        insert.Parameters.AddWithValue("$expires", Timestamp(lease.ExpiresAt));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return lease;
    }

    public async Task ReleaseLeaseAsync(
        Guid workspaceId,
        Guid leaseId,
        string owner,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var lease = await FindLeaseAsync(connection, workspaceId, cancellationToken, transaction)
            ?? throw new CoordinatorNotFoundException($"Lease '{leaseId}' was not found.");
        if (lease.Id != leaseId)
            throw new CoordinatorNotFoundException($"Lease '{leaseId}' was not found.");
        if (lease.Owner != owner)
            throw new CoordinatorConflictException("Only the lease owner may release it.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM leases WHERE id = $id;";
        command.Parameters.AddWithValue("$id", leaseId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WorkspaceRevision> CommitRevisionAsync(
        Guid workspaceId,
        CommitRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Commit))
            throw new ArgumentException("Commit is required.", nameof(request));

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var workspace = await FindWorkspaceAsync(connection, workspaceId, cancellationToken, transaction)
            ?? throw new CoordinatorNotFoundException($"Workspace '{workspaceId}' was not found.");
        await PurgeExpiredLeasesAsync(connection, cancellationToken, transaction);
        var lease = await FindLeaseAsync(connection, workspaceId, cancellationToken, transaction)
            ?? throw new CoordinatorConflictException($"Workspace '{workspace.Key}' has no active lease.");
        if (lease.Owner != request.Owner || lease.Branch != request.Branch)
            throw new CoordinatorConflictException(
                $"The active lease belongs to '{lease.Owner}' on '{lease.Branch}'.");

        var now = _time.GetUtcNow();
        var revision = new WorkspaceRevision(
            Guid.NewGuid(), workspaceId, request.Branch.Trim(), request.Commit.Trim(),
            request.Message, now, request.Artifacts?.ToArray() ?? []);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO revisions (id, workspace_id, branch, commit_sha, message, created_at)
            VALUES ($id, $workspace, $branch, $commit, $message, $created);
            """;
        insert.Parameters.AddWithValue("$id", revision.Id.ToString());
        insert.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        insert.Parameters.AddWithValue("$branch", revision.Branch);
        insert.Parameters.AddWithValue("$commit", revision.Commit);
        insert.Parameters.AddWithValue("$message", (object?)revision.Message ?? DBNull.Value);
        insert.Parameters.AddWithValue("$created", Timestamp(revision.CreatedAt));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        for (var index = 0; index < revision.Artifacts.Count; index++)
        {
            await using var artifact = connection.CreateCommand();
            artifact.Transaction = transaction;
            artifact.CommandText = """
                INSERT INTO revision_artifacts (revision_id, ordinal, artifact)
                VALUES ($revision, $ordinal, $artifact);
                """;
            artifact.Parameters.AddWithValue("$revision", revision.Id.ToString());
            artifact.Parameters.AddWithValue("$ordinal", index);
            artifact.Parameters.AddWithValue("$artifact", revision.Artifacts[index]);
            await artifact.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE workspaces SET revision = $revision, updated_at = $updated WHERE id = $id;";
        update.Parameters.AddWithValue("$revision", revision.Commit);
        update.Parameters.AddWithValue("$updated", Timestamp(now));
        update.Parameters.AddWithValue("$id", workspaceId.ToString());
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    public async Task<IReadOnlyList<WorkspaceRevision>> ListRevisionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (await FindWorkspaceAsync(connection, workspaceId, cancellationToken) is null)
            throw new CoordinatorNotFoundException($"Workspace '{workspaceId}' was not found.");
        return await ReadRevisionsAsync(connection, workspaceId, cancellationToken);
    }

    private async Task Initialize()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS workspaces (
              id TEXT PRIMARY KEY,
              workspace_key TEXT NOT NULL UNIQUE,
              name TEXT NOT NULL,
              repository TEXT NOT NULL,
              revision TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS leases (
              id TEXT PRIMARY KEY,
              workspace_id TEXT NOT NULL UNIQUE REFERENCES workspaces(id) ON DELETE CASCADE,
              owner TEXT NOT NULL,
              branch TEXT NOT NULL,
              acquired_at TEXT NOT NULL,
              expires_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS revisions (
              id TEXT PRIMARY KEY,
              workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
              branch TEXT NOT NULL,
              commit_sha TEXT NOT NULL,
              message TEXT,
              created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS revision_artifacts (
              revision_id TEXT NOT NULL REFERENCES revisions(id) ON DELETE CASCADE,
              ordinal INTEGER NOT NULL,
              artifact TEXT NOT NULL,
              PRIMARY KEY (revision_id, ordinal));
            CREATE INDEX IF NOT EXISTS ix_revisions_workspace ON revisions(workspace_id, created_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<WorkspaceSummary?> FindWorkspaceAsync(
        SqliteConnection connection,
        Guid id,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, workspace_key, name, repository, revision, created_at, updated_at
            FROM workspaces WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWorkspace(reader) : null;
    }

    private static async Task<WorkspaceLease?> FindLeaseAsync(
        SqliteConnection connection,
        Guid workspaceId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, workspace_id, owner, branch, acquired_at, expires_at
            FROM leases WHERE workspace_id = $workspace;
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new WorkspaceLease(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                ParseTimestamp(reader.GetString(5)))
            : null;
    }

    private async Task<IReadOnlyList<WorkspaceRevision>> ReadRevisionsAsync(
        SqliteConnection connection,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, workspace_id, branch, commit_sha, message, created_at
            FROM revisions WHERE workspace_id = $workspace
            ORDER BY created_at DESC;
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString());
        var rows = new List<(Guid Id, Guid WorkspaceId, string Branch, string Commit, string? Message, DateTimeOffset Created)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    ParseTimestamp(reader.GetString(5))));
            }
        }

        var result = new List<WorkspaceRevision>();
        foreach (var row in rows)
        {
            await using var artifacts = connection.CreateCommand();
            artifacts.CommandText = """
                SELECT artifact FROM revision_artifacts
                WHERE revision_id = $revision ORDER BY ordinal;
                """;
            artifacts.Parameters.AddWithValue("$revision", row.Id.ToString());
            var values = new List<string>();
            await using var reader = await artifacts.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                values.Add(reader.GetString(0));
            result.Add(new WorkspaceRevision(
                row.Id, row.WorkspaceId, row.Branch, row.Commit, row.Message, row.Created, values));
        }

        return result;
    }

    private async Task PurgeExpiredLeasesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM leases WHERE expires_at <= $now;";
        command.Parameters.AddWithValue("$now", Timestamp(_time.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WorkspaceSummary ReadWorkspace(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        ParseTimestamp(reader.GetString(5)),
        ParseTimestamp(reader.GetString(6)));

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Validate(CreateWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            throw new ArgumentException("Key is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Repository))
            throw new ArgumentException("Repository is required.", nameof(request));
    }
}
