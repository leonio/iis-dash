using IisDash.Shared.Data;

namespace IisDash.Features.Requests;

/// <summary>One request, every field the log had, and the file and line it came from.</summary>
public sealed class RequestDetailData
{
    public long Id { get; init; }
    public long FileId { get; init; }
    public string FileName { get; init; } = "";
    public int LineNo { get; init; }
    public long? Ts { get; init; }
    public string Endpoint { get; init; } = "";
    public List<(string Key, string? Value)> Fields { get; init; } = [];
    public string? Method => Field("method");
    public string? UriStem => Field("uri_stem");
    public string? UriQuery => Field("uri_query");
    public int? Status => int.TryParse(Field("status"), out int s) ? s : null;
    public string? ClientIp => Field("client_ip");
    public string? Field(string key) => Fields.FirstOrDefault(f => f.Key == key).Value;

    private static readonly string[] Columns =
    [
        "method", "uri_stem", "uri_query", "status", "sub_status", "win32_status", "time_taken_ms", "bytes_sent", "bytes_received",
        "client_ip", "username", "user_agent", "referer", "host", "server_ip", "server_port", "protocol_version", "site_name",
        "server_name", "service_name", "cookie",
    ];

    public static RequestDetailData? Load(Db db, long id) => db.One(
        $"""
        SELECT r.id, r.file_id, f.name, r.line_no, r.ts, r.endpoint, {string.Join(", ", Columns.Select(c => "r." + c))}
        FROM requests r JOIN log_files f ON f.id = r.file_id
        WHERE r.id = $id
        """,
        new Args { ["$id"] = id },
        r => new RequestDetailData
        {
            Id = r.GetInt64(0),
            FileId = r.GetInt64(1),
            FileName = r.GetString(2),
            LineNo = r.GetInt32(3),
            Ts = r.Long(4),
            Endpoint = r.GetString(5),
            Fields = [.. Columns.Select((c, i) => (c, r.IsDBNull(6 + i) ? null : Convert.ToString(r.GetValue(6 + i), System.Globalization.CultureInfo.InvariantCulture)))],
        });
}
