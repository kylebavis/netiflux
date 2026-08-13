using System.Text.Json;
using Netiflux.Core.Configuration;

namespace Netiflux.Core.State;

/// <summary>
/// Remembers which entries were pushed to the third-party bookmark service.
/// <para>
/// The Miniflux API has no "was this saved?" field — <c>POST /v1/entries/{id}/save</c> is
/// fire-and-forget and returns 202. Without a local record the UI could not show a
/// saved marker, and during triage you would have no way to tell an entry you already
/// pushed from one you skipped. So the app keeps its own log.
/// </para>
/// </summary>
public sealed class SavedEntryStore
{
    /// <summary>Entries remembered before the oldest records are trimmed.</summary>
    private const int MaxEntries = 20_000;

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<long, DateTimeOffset> _saved;
    private bool _dirty;

    private SavedEntryStore(string path, Dictionary<long, DateTimeOffset> saved)
    {
        _path = path;
        _saved = saved;
    }

    public static string DefaultPath => Path.Combine(ConfigStore.ConfigDirectory, "saved-entries.json");

    public static SavedEntryStore Load(string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
        {
            return new SavedEntryStore(path, []);
        }

        try
        {
            var json = File.ReadAllText(path);
            var records = JsonSerializer.Deserialize<Dictionary<long, DateTimeOffset>>(json);
            return new SavedEntryStore(path, records ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt cache is not worth failing startup over — start fresh.
            return new SavedEntryStore(path, []);
        }
    }

    public bool IsSaved(long entryId)
    {
        lock (_gate)
        {
            return _saved.ContainsKey(entryId);
        }
    }

    public void MarkSaved(long entryId)
    {
        lock (_gate)
        {
            _saved[entryId] = DateTimeOffset.UtcNow;
            _dirty = true;

            if (_saved.Count > MaxEntries)
            {
                Trim();
            }
        }
    }

    public void Forget(long entryId)
    {
        lock (_gate)
        {
            if (_saved.Remove(entryId))
            {
                _dirty = true;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _saved.Count;
            }
        }
    }

    /// <summary>Drops the oldest quarter of records once the cap is hit.</summary>
    private void Trim()
    {
        var keepFrom = _saved.Count / 4;
        var doomed = _saved
            .OrderBy(pair => pair.Value)
            .Take(keepFrom)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var id in doomed)
        {
            _saved.Remove(id);
        }
    }

    /// <summary>Persists if anything changed. Safe to call often; a no-op when clean.</summary>
    public void Flush()
    {
        Dictionary<long, DateTimeOffset> snapshot;

        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            snapshot = new Dictionary<long, DateTimeOffset>(_saved);
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the saved-marker cache degrades a visual cue, nothing more.
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }
}
