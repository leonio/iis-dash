namespace Server.Data.Entities;

public sealed class LogFile
{
    public long Id { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Format { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public int TotalLines { get; set; }
    public int ParsedLines { get; set; }
    public int FailedLines { get; set; }
    public int SkippedLines { get; set; }

    public ICollection<LogRawLine> RawLines { get; set; } = new List<LogRawLine>();
    public ICollection<LogEntry> Entries { get; set; } = new List<LogEntry>();
}
