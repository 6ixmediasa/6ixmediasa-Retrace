using Microsoft.Data.Sqlite;
using Retrace.Core;

namespace Retrace.Data;

public sealed class SqliteEventRepository : IEventRepository
{
    private readonly string _connectionString;

    public SqliteEventRepository(string? databasePath = null)
    {
        var path = databasePath ?? RetracePaths.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL,
                event_type INTEGER NOT NULL,
                original_path TEXT NULL,
                current_path TEXT NULL,
                file_name TEXT NOT NULL,
                file_extension TEXT NOT NULL,
                file_size INTEGER NULL,
                is_directory INTEGER NOT NULL,
                recovery_available INTEGER NOT NULL,
                recovery_data_path TEXT NULL,
                status TEXT NOT NULL,
                notes TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);
            CREATE INDEX IF NOT EXISTS idx_events_status ON events(status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> AddAsync(RetraceEvent item, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events(timestamp_utc,event_type,original_path,current_path,file_name,file_extension,file_size,is_directory,recovery_available,recovery_data_path,status,notes)
            VALUES($t,$e,$o,$c,$n,$x,$s,$d,$r,$p,$st,$notes);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$t", item.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("$e", (int)item.EventType);
        command.Parameters.AddWithValue("$o", (object?)item.OriginalPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$c", (object?)item.CurrentPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$n", item.FileName);
        command.Parameters.AddWithValue("$x", item.FileExtension);
        command.Parameters.AddWithValue("$s", (object?)item.FileSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$d", item.IsDirectory ? 1 : 0);
        command.Parameters.AddWithValue("$r", item.RecoveryAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$p", (object?)item.RecoveryDataPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$st", item.Status);
        command.Parameters.AddWithValue("$notes", (object?)item.Notes ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task<IReadOnlyList<RetraceEvent>> GetRecentAsync(int limit = 500, CancellationToken cancellationToken = default) =>
        QueryAsync("SELECT * FROM events ORDER BY timestamp_utc DESC LIMIT $limit", c => c.Parameters.AddWithValue("$limit", limit), cancellationToken);

    public Task<IReadOnlyList<RetraceEvent>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        QueryAsync("SELECT * FROM events WHERE timestamp_utc >= $since AND status='Active' ORDER BY timestamp_utc DESC", c => c.Parameters.AddWithValue("$since", sinceUtc.ToString("O")), cancellationToken);

    public Task<IReadOnlyList<RetraceEvent>> SearchAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        var q = $"%{query.Trim()}%";
        return QueryAsync("SELECT * FROM events WHERE file_name LIKE $q OR original_path LIKE $q OR current_path LIKE $q OR notes LIKE $q ORDER BY timestamp_utc DESC LIMIT $limit", c => { c.Parameters.AddWithValue("$q", q); c.Parameters.AddWithValue("$limit", limit); }, cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE events SET status=$status WHERE id=$id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events WHERE timestamp_utc < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RetraceEvent>> QueryAsync(string sql, Action<SqliteCommand>? configure, CancellationToken cancellationToken)
    {
        var list = new List<RetraceEvent>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) list.Add(Map(reader));
        return list;
    }

    private static RetraceEvent Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        TimestampUtc = DateTime.Parse(r.GetString(r.GetOrdinal("timestamp_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        EventType = (RetraceEventType)r.GetInt32(r.GetOrdinal("event_type")),
        OriginalPath = r.IsDBNull(r.GetOrdinal("original_path")) ? null : r.GetString(r.GetOrdinal("original_path")),
        CurrentPath = r.IsDBNull(r.GetOrdinal("current_path")) ? null : r.GetString(r.GetOrdinal("current_path")),
        FileName = r.GetString(r.GetOrdinal("file_name")),
        FileExtension = r.GetString(r.GetOrdinal("file_extension")),
        FileSize = r.IsDBNull(r.GetOrdinal("file_size")) ? null : r.GetInt64(r.GetOrdinal("file_size")),
        IsDirectory = r.GetInt32(r.GetOrdinal("is_directory")) == 1,
        RecoveryAvailable = r.GetInt32(r.GetOrdinal("recovery_available")) == 1,
        RecoveryDataPath = r.IsDBNull(r.GetOrdinal("recovery_data_path")) ? null : r.GetString(r.GetOrdinal("recovery_data_path")),
        Status = r.GetString(r.GetOrdinal("status")),
        Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes"))
    };
}
