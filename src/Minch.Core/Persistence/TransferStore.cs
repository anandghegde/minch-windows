using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Minch.Core.Models;

namespace Minch.Core.Persistence;

/// <summary>
/// Local cache of the TorBox library plus local-only state (tags, favorites,
/// hidden, first-seen time, downloaded files, playback position).
///
/// Remote snapshots are stored as JSON blobs keyed by transfer id — the shape
/// is owned by TorBox and changes often, so a relational schema buys nothing.
/// Local state lives in separate tables so a sync can never clobber it.
///
/// All public members are thread-safe. <see cref="Changed"/> fires on the
/// calling thread; UI consumers must marshal to the dispatcher.
/// </summary>
public sealed class TransferStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();
    private Dictionary<string, Transfer> _remote = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TransferLocal> _local = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileLocal> _files = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    /// <param name="path">Database file path, or ":memory:" for tests.</param>
    public TransferStore(string path)
    {
        if (path != ":memory:")
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS transfers (
                id   TEXT PRIMARY KEY,
                json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS transfer_local (
                id          TEXT PRIMARY KEY,
                added_at    TEXT,
                tags        TEXT,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                is_hidden   INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS file_local (
                id            TEXT PRIMARY KEY,
                is_downloaded INTEGER NOT NULL DEFAULT 0,
                local_path    TEXT,
                played_at     TEXT,
                position_sec  REAL
            );
            """);
        Load();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minch", "minch.db");

    // ------------------------------------------------------------------ reads

    /// <summary>Remote snapshot merged with local state, newest first.</summary>
    public IReadOnlyList<Transfer> All()
    {
        lock (_gate)
        {
            return _remote.Values.Select(Merge).OrderByDescending(t => t.AddedAt).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public Transfer? Get(string id)
    {
        lock (_gate)
        {
            return _remote.TryGetValue(id, out var t) ? Merge(t) : null;
        }
    }

    public int Count
    {
        get { lock (_gate) return _remote.Count; }
    }

    // ----------------------------------------------------------------- writes

    /// <summary>
    /// Upserts the remote snapshot and drops rows no longer present. Local
    /// state is untouched, except that first-seen time is recorded for new ids.
    /// Returns true when anything visible changed.
    /// </summary>
    public bool ReplaceRemote(IReadOnlyList<Transfer> incoming)
    {
        bool changed;
        lock (_gate)
        {
            var next = new Dictionary<string, Transfer>(StringComparer.Ordinal);
            foreach (var t in incoming) next[t.Id] = t;

            changed = next.Count != _remote.Count;
            using var tx = _db.BeginTransaction();

            foreach (var (id, t) in next)
            {
                if (_remote.TryGetValue(id, out var old) && SameRemote(old, t)) continue;
                changed = true;
                Exec("INSERT INTO transfers(id, json) VALUES($id, $json) ON CONFLICT(id) DO UPDATE SET json = excluded.json;",
                    tx, ("$id", id), ("$json", JsonSerializer.Serialize(Strip(t), JsonOpts)));
                if (!_local.ContainsKey(id))
                {
                    var local = new TransferLocal(t.AddedAt, [], false, false);
                    _local[id] = local;
                    SaveLocal(id, local, tx);
                }
            }

            foreach (var id in _remote.Keys.Where(k => !next.ContainsKey(k)).ToList())
            {
                changed = true;
                Exec("DELETE FROM transfers WHERE id = $id;", tx, ("$id", id));
                Exec("DELETE FROM transfer_local WHERE id = $id;", tx, ("$id", id));
                _local.Remove(id);
                // file_local rows are kept: a file downloaded to disk stays on disk.
            }

            tx.Commit();
            _remote = next;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    /// <summary>Optimistic local removal (e.g. right after a delete call succeeds).</summary>
    public void Remove(string id)
    {
        lock (_gate)
        {
            if (!_remote.Remove(id)) return;
            Exec("DELETE FROM transfers WHERE id = $id;", null, ("$id", id));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Optimistic in-place edit of the remote snapshot (e.g. after a rename).</summary>
    public void UpdateRemote(string id, Func<Transfer, Transfer> mutate)
    {
        lock (_gate)
        {
            if (!_remote.TryGetValue(id, out var t)) return;
            var next = Strip(mutate(t));
            _remote[id] = next;
            Exec("UPDATE transfers SET json = $json WHERE id = $id;", null, ("$id", id), ("$json", JsonSerializer.Serialize(next, JsonOpts)));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFavorite(string id, bool value) => UpdateLocal(id, l => l with { IsFavorite = value });

    public void SetHidden(string id, bool value) => UpdateLocal(id, l => l with { IsHidden = value });

    public void SetTags(string id, IReadOnlyList<string> tags) =>
        UpdateLocal(id, l => l with { Tags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() });

    public void MarkDownloaded(string fileId, string localPath) =>
        UpdateFile(fileId, f => f with { IsDownloaded = true, LocalPath = localPath });

    public void ClearDownloaded(string fileId) =>
        UpdateFile(fileId, f => f with { IsDownloaded = false, LocalPath = null });

    public void MarkPlayed(string fileId, double? positionSec = null) =>
        UpdateFile(fileId, f => f with { PlayedAt = DateTimeOffset.UtcNow, PositionSec = positionSec ?? f.PositionSec });

    /// <summary>Wipes everything — used on sign-out / key replacement.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Exec("DELETE FROM transfers; DELETE FROM transfer_local; DELETE FROM file_local;");
            _remote.Clear();
            _local.Clear();
            _files.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _db.Dispose();

    // --------------------------------------------------------------- internals

    private sealed record TransferLocal(DateTimeOffset AddedAt, IReadOnlyList<string> Tags, bool IsFavorite, bool IsHidden);

    private sealed record FileLocal(bool IsDownloaded, string? LocalPath, DateTimeOffset? PlayedAt, double? PositionSec)
    {
        public static readonly FileLocal Empty = new(false, null, null, null);
    }

    private Transfer Merge(Transfer t)
    {
        var local = _local.GetValueOrDefault(t.Id);
        var files = t.Files.Select(f =>
        {
            if (!_files.TryGetValue(f.Id, out var fl)) return f;
            var downloaded = fl.IsDownloaded && fl.LocalPath is not null && File.Exists(fl.LocalPath);
            return f with
            {
                IsDownloaded = downloaded,
                LocalPath = downloaded ? fl.LocalPath : null,
                PlayedAt = fl.PlayedAt,
                PlaybackPositionSec = fl.PositionSec,
            };
        }).ToList();
        return t with
        {
            AddedAt = local?.AddedAt ?? t.AddedAt,
            TagNames = local?.Tags ?? [],
            IsFavorite = local?.IsFavorite ?? false,
            IsHidden = local?.IsHidden ?? false,
            Files = files,
        };
    }

    /// <summary>Removes local fields so the stored JSON is purely remote.</summary>
    private static Transfer Strip(Transfer t) => t with
    {
        TagNames = [],
        IsFavorite = false,
        IsHidden = false,
        Files = t.Files.Select(f => f with { IsDownloaded = false, LocalPath = null, PlayedAt = null, PlaybackPositionSec = null }).ToList(),
    };

    private static bool SameRemote(Transfer a, Transfer b)
    {
        if (a.InfoHash != b.InfoHash || a.Name != b.Name || a.Kind != b.Kind || a.SizeBytes != b.SizeBytes ||
            a.Status != b.Status || a.Progress != b.Progress || a.DownloadSpeed != b.DownloadSpeed ||
            a.UploadSpeed != b.UploadSpeed || a.EtaSeconds != b.EtaSeconds || a.Availability != b.Availability ||
            a.Error != b.Error || a.Seeds != b.Seeds || a.Peers != b.Peers || a.Files.Count != b.Files.Count)
            return false;
        for (var i = 0; i < a.Files.Count; i++)
        {
            var x = a.Files[i];
            var y = b.Files[i];
            if (x.Id != y.Id || x.Name != y.Name || x.PathInTransfer != y.PathInTransfer || x.SizeBytes != y.SizeBytes || x.Mime != y.Mime)
                return false;
        }
        return true;
    }

    private void UpdateLocal(string id, Func<TransferLocal, TransferLocal> mutate)
    {
        lock (_gate)
        {
            var current = _local.GetValueOrDefault(id) ?? new TransferLocal(DateTimeOffset.UtcNow, [], false, false);
            var next = mutate(current);
            _local[id] = next;
            SaveLocal(id, next, null);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFile(string fileId, Func<FileLocal, FileLocal> mutate)
    {
        lock (_gate)
        {
            var next = mutate(_files.GetValueOrDefault(fileId) ?? FileLocal.Empty);
            _files[fileId] = next;
            Exec("""
                INSERT INTO file_local(id, is_downloaded, local_path, played_at, position_sec)
                VALUES($id, $d, $p, $pa, $pos)
                ON CONFLICT(id) DO UPDATE SET is_downloaded = excluded.is_downloaded, local_path = excluded.local_path,
                    played_at = excluded.played_at, position_sec = excluded.position_sec;
                """, null,
                ("$id", fileId), ("$d", next.IsDownloaded ? 1 : 0), ("$p", next.LocalPath),
                ("$pa", next.PlayedAt?.ToString("O")), ("$pos", next.PositionSec));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveLocal(string id, TransferLocal l, SqliteTransaction? tx) =>
        Exec("""
            INSERT INTO transfer_local(id, added_at, tags, is_favorite, is_hidden) VALUES($id, $a, $t, $f, $h)
            ON CONFLICT(id) DO UPDATE SET added_at = excluded.added_at, tags = excluded.tags,
                is_favorite = excluded.is_favorite, is_hidden = excluded.is_hidden;
            """, tx,
            ("$id", id), ("$a", l.AddedAt.ToString("O")), ("$t", JsonSerializer.Serialize(l.Tags)),
            ("$f", l.IsFavorite ? 1 : 0), ("$h", l.IsHidden ? 1 : 0));

    private void Load()
    {
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, json FROM transfers;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                try
                {
                    var t = JsonSerializer.Deserialize<Transfer>(r.GetString(1), JsonOpts);
                    if (t is not null) _remote[r.GetString(0)] = t;
                }
                catch (JsonException)
                {
                    // Schema drift between versions: drop the row, next sync refills it.
                }
            }
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, added_at, tags, is_favorite, is_hidden FROM transfer_local;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var added = !r.IsDBNull(1) && DateTimeOffset.TryParse(r.GetString(1), out var a) ? a : DateTimeOffset.UtcNow;
                IReadOnlyList<string> tags = [];
                if (!r.IsDBNull(2))
                {
                    try { tags = JsonSerializer.Deserialize<List<string>>(r.GetString(2)) ?? []; }
                    catch (JsonException) { }
                }
                _local[r.GetString(0)] = new TransferLocal(added, tags, r.GetInt64(3) != 0, r.GetInt64(4) != 0);
            }
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, is_downloaded, local_path, played_at, position_sec FROM file_local;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                DateTimeOffset? played = !r.IsDBNull(3) && DateTimeOffset.TryParse(r.GetString(3), out var p) ? p : null;
                _files[r.GetString(0)] = new FileLocal(
                    r.GetInt64(1) != 0,
                    r.IsDBNull(2) ? null : r.GetString(2),
                    played,
                    r.IsDBNull(4) ? null : r.GetDouble(4));
            }
        }
    }

    private void Exec(string sql) => Exec(sql, null);

    private void Exec(string sql, SqliteTransaction? tx, params (string Name, object? Value)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
