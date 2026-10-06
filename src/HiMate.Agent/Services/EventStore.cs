using HiMate.Agent.Models;
using Microsoft.Data.Sqlite;

namespace HiMate.Agent.Services;

public enum SaveEventResult
{
    Inserted,
    Duplicate,
    Conflict
}

public sealed class EventStore
{
    private readonly string _connectionString;

    public EventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS events (
                local_id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_event_id INTEGER NOT NULL,
                uid TEXT NOT NULL,
                type TEXT NOT NULL,
                amount INTEGER NOT NULL,
                total INTEGER NOT NULL,
                remaining INTEGER NOT NULL,
                tx INTEGER NOT NULL,
                gen INTEGER NOT NULL,
                seq INTEGER NOT NULL,
                cid INTEGER NULL,
                oid INTEGER NULL,
                received_at_utc TEXT NOT NULL,
                device_acked INTEGER NOT NULL DEFAULT 0,
                sync_status TEXT NOT NULL DEFAULT 'PENDING',
                server_id INTEGER NULL,
                last_error TEXT NOT NULL DEFAULT '',
                retry_count INTEGER NOT NULL DEFAULT 0,
                UNIQUE(uid, gen, tx, seq)
            );
            CREATE INDEX IF NOT EXISTS idx_events_sync ON events(sync_status, local_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<SaveEventResult> SaveFromDeviceAsync(CardEvent e)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        var find = db.CreateCommand();
        find.Transaction = (SqliteTransaction)tx;
        find.CommandText = "SELECT * FROM events WHERE uid=$uid AND gen=$gen AND tx=$tx AND seq=$seq LIMIT 1";
        find.Parameters.AddWithValue("$uid", e.Uid);
        find.Parameters.AddWithValue("$gen", e.Gen);
        find.Parameters.AddWithValue("$tx", e.Tx);
        find.Parameters.AddWithValue("$seq", e.Seq);

        await using (var reader = await find.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                // device_event_id is only a local transport/queue ID and may be reset/reused.
                // Logical duplicate comparison intentionally ignores it.
                var same =
                    reader.GetString(reader.GetOrdinal("type")) == e.Type &&
                    reader.GetInt32(reader.GetOrdinal("amount")) == e.Amount &&
                    reader.GetInt32(reader.GetOrdinal("total")) == e.Total &&
                    reader.GetInt32(reader.GetOrdinal("remaining")) == e.Remaining;

                await tx.CommitAsync();
                return same ? SaveEventResult.Duplicate : SaveEventResult.Conflict;
            }
        }

        var insert = db.CreateCommand();
        insert.Transaction = (SqliteTransaction)tx;
        insert.CommandText = """
            INSERT INTO events(device_event_id,uid,type,amount,total,remaining,tx,gen,seq,cid,oid,received_at_utc)
            VALUES($eid,$uid,$type,$amount,$total,$remaining,$tx,$gen,$seq,$cid,$oid,$received)
            """;
        insert.Parameters.AddWithValue("$eid", e.DeviceEventId);
        insert.Parameters.AddWithValue("$uid", e.Uid);
        insert.Parameters.AddWithValue("$type", e.Type);
        insert.Parameters.AddWithValue("$amount", e.Amount);
        insert.Parameters.AddWithValue("$total", e.Total);
        insert.Parameters.AddWithValue("$remaining", e.Remaining);
        insert.Parameters.AddWithValue("$tx", e.Tx);
        insert.Parameters.AddWithValue("$gen", e.Gen);
        insert.Parameters.AddWithValue("$seq", e.Seq);
        insert.Parameters.AddWithValue("$cid", (object?)e.Cid ?? DBNull.Value);
        insert.Parameters.AddWithValue("$oid", (object?)e.Oid ?? DBNull.Value);
        insert.Parameters.AddWithValue("$received", e.ReceivedAtUtc);
        await insert.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return SaveEventResult.Inserted;
    }

    public async Task MarkDeviceAckedAsync(CardEvent e)
    {
        await ExecuteAsync("UPDATE events SET device_acked=1 WHERE uid=$uid AND gen=$gen AND tx=$tx AND seq=$seq",
            ("$uid", e.Uid), ("$gen", e.Gen), ("$tx", e.Tx), ("$seq", e.Seq));
    }

    public async Task<List<CardEvent>> GetPendingAsync(int limit = 100)
    {
        var list = new List<CardEvent>();
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM events
            WHERE sync_status IN ('PENDING','RETRY')
            ORDER BY local_id ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r));
        return list;
    }

    public async Task<List<CardEvent>> GetRecentAsync(int limit = 500)
    {
        var list = new List<CardEvent>();
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT * FROM events ORDER BY local_id DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r));
        return list;
    }

    public async Task<int> CountPendingAsync()
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE sync_status IN ('PENDING','RETRY')";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> CountProblemsAsync()
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE sync_status IN ('CONFLICT','INVALID')";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task MarkSyncedAsync(long localId, long? serverId, string status)
    {
        await ExecuteAsync("UPDATE events SET sync_status='SYNCED',server_id=$sid,last_error='' WHERE local_id=$id",
            ("$sid", (object?)serverId ?? DBNull.Value), ("$id", localId));
    }

    public async Task MarkPermanentFailureAsync(long localId, string status, string message)
    {
        await ExecuteAsync("UPDATE events SET sync_status=$status,last_error=$err,retry_count=retry_count+1 WHERE local_id=$id",
            ("$status", status), ("$err", message), ("$id", localId));
    }

    public async Task MarkRetryAsync(long localId, string message)
    {
        await ExecuteAsync("UPDATE events SET sync_status='RETRY',last_error=$err,retry_count=retry_count+1 WHERE local_id=$id",
            ("$err", message), ("$id", localId));
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static CardEvent Read(SqliteDataReader r)
    {
        long? NullableLong(string name) => r.IsDBNull(r.GetOrdinal(name)) ? null : r.GetInt64(r.GetOrdinal(name));
        return new CardEvent
        {
            LocalId = r.GetInt64(r.GetOrdinal("local_id")),
            DeviceEventId = r.GetInt64(r.GetOrdinal("device_event_id")),
            Uid = r.GetString(r.GetOrdinal("uid")),
            Type = r.GetString(r.GetOrdinal("type")),
            Amount = r.GetInt32(r.GetOrdinal("amount")),
            Total = r.GetInt32(r.GetOrdinal("total")),
            Remaining = r.GetInt32(r.GetOrdinal("remaining")),
            Tx = r.GetInt32(r.GetOrdinal("tx")),
            Gen = r.GetInt32(r.GetOrdinal("gen")),
            Seq = r.GetInt32(r.GetOrdinal("seq")),
            Cid = NullableLong("cid"),
            Oid = NullableLong("oid"),
            ReceivedAtUtc = r.GetString(r.GetOrdinal("received_at_utc")),
            DeviceAcked = r.GetInt32(r.GetOrdinal("device_acked")) == 1,
            SyncStatus = r.GetString(r.GetOrdinal("sync_status")),
            ServerId = NullableLong("server_id"),
            LastError = r.GetString(r.GetOrdinal("last_error")),
            RetryCount = r.GetInt32(r.GetOrdinal("retry_count"))
        };
    }
}
