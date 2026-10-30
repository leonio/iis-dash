using IisDash.Shared.Data;

namespace IisDash.Shared.Filters;

/// <summary>
/// What the filter bar offers: the uploaded files, the methods in the logs, and the time they cover. Read once and kept
/// until an upload or a delete changes them (<see cref="Changed"/>), as finding the methods means reading every row.
/// </summary>
public sealed class FilterOptions(Db db)
{
    private Snapshot? _current;

    public sealed record File(long Id, string Name);

    public sealed record Snapshot(List<File> Files, List<string> Methods, long? FirstTs, long? LastTs)
    {
        public bool HasData => Files.Count > 0 && FirstTs is not null;
    }

    public Snapshot Current => _current ??= Load();

    public void Changed() => _current = null;

    private Snapshot Load()
    {
        var files = db.Query("SELECT id, name FROM log_files ORDER BY name", new Args(), r => new File(r.GetInt64(0), r.GetString(1)));
        var methods = db.Query("SELECT method FROM requests WHERE method IS NOT NULL GROUP BY method ORDER BY count(*) DESC LIMIT 20",
            new Args(), r => r.GetString(0));
        var (first, last) = db.Query("SELECT min(first_ts), max(last_ts) FROM log_files", new Args(), r => (r.Long(0), r.Long(1)))[0];
        return new Snapshot(files, methods, first, last);
    }
}
