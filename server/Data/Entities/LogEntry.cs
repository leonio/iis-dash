namespace Server.Data.Entities;

public sealed class LogEntry
{
    public long Id { get; set; }
    public long LogFileId { get; set; }
    public LogFile LogFile { get; set; } = null!;
    public int LineNumber { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string? DateText { get; set; }
    public string? TimeText { get; set; }
    public string? ClientIp { get; set; }
    public string? Username { get; set; }
    public string? Method { get; set; }
    public string? UriStem { get; set; }
    public string? UriQuery { get; set; }
    public int? ProtocolStatus { get; set; }
    public int? SubStatus { get; set; }
    public int? Win32Status { get; set; }
    public long? BytesSent { get; set; }
    public long? BytesReceived { get; set; }
    public long? TimeTakenMs { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public string? Host { get; set; }
    public string? ServerIp { get; set; }
    public int? ServerPort { get; set; }
    public string? ProtocolVersion { get; set; }
    public string? SiteName { get; set; }
    public string? ServerName { get; set; }
    public string? ServiceName { get; set; }
    public string? Cookie { get; set; }
}
