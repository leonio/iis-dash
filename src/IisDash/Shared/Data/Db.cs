using System.Reflection;
using Microsoft.Data.Sqlite;

namespace IisDash.Shared.Data;

/// <summary>
/// SQLite access: one file, WAL mode, a short-lived connection per unit of work, plain SQL.
/// </summary>
/// <remarks>
/// <para>Plain SQL rather than an ORM: the app is three tables and a handful of aggregate queries, the aggregates are
/// what SQLite is fast at, and it keeps the native build free of reflection. Rows are read with a mapping function at
/// each call site, so what a query returns is next to the query.</para>
/// <para>Which SQLite: on Windows the one that ships with Windows (winsqlite3); everywhere else e_sqlite3, which the
/// native Linux build links into the executable itself (see IisDash.csproj).</para>
/// </remarks>
public sealed class Db
{
    public string Path { get; }

    static Db()
    {
        if (OperatingSystem.IsWindows())
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
        }
        else
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        }
    }

    public Db(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        using var conn = Open();
        Execute(conn, "PRAGMA journal_mode = WAL");
        using var schema = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sql")!);
        Execute(conn, schema.ReadToEnd());
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString());
        conn.Open();
        // Foreign keys for the cascade when an upload is deleted or replaced; a large page cache and memory-mapped reads
        // for the aggregates; NORMAL sync is safe with WAL and much faster for the bulk inserts.
        Execute(conn, "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY; " +
                      "PRAGMA cache_size = -65536; PRAGMA mmap_size = 268435456");
        return conn;
    }

    public List<T> Query<T>(string sql, Args args, Func<SqliteDataReader, T> map)
    {
        using var conn = Open();
        return Query(conn, sql, args, map);
    }

    public static List<T> Query<T>(SqliteConnection conn, string sql, Args args, Func<SqliteDataReader, T> map)
    {
        using var command = Command(conn, sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public T? One<T>(string sql, Args args, Func<SqliteDataReader, T> map) => Query(sql, args, map).FirstOrDefault();

    public long Scalar(string sql, Args? args = null)
    {
        using var conn = Open();
        using var command = Command(conn, sql, args ?? new Args());
        return command.ExecuteScalar() is long value ? value : 0;
    }

    public int Execute(string sql, Args? args = null)
    {
        using var conn = Open();
        using var command = Command(conn, sql, args ?? new Args());
        return command.ExecuteNonQuery();
    }

    public static int Execute(SqliteConnection conn, string sql, Args? args = null)
    {
        using var command = Command(conn, sql, args ?? new Args());
        return command.ExecuteNonQuery();
    }

    public static SqliteCommand Command(SqliteConnection conn, string sql, Args args)
    {
        var command = conn.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

/// <summary>Named parameters for a statement: <c>new Args { ["$file"] = id }</c>.</summary>
public sealed class Args : Dictionary<string, object?>;

public static class ReaderExtensions
{
    public static string? Text(this SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
    public static long? Long(this SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);
    public static int? Int(this SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
    public static double? Double(this SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
}
