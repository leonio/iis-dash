namespace Server.Data.Entities;

public sealed class LogRawLine
{
    public long Id { get; set; }
    public long LogFileId { get; set; }
    public LogFile LogFile { get; set; } = null!;
    public int LineNumber { get; set; }
    public string LineText { get; set; } = string.Empty;
    public string ParseStatus { get; set; } = string.Empty;
    public string? ParseError { get; set; }
    public bool IsDataLine { get; set; }
}
