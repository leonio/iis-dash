namespace IisDash.Features.Uploads;

public sealed partial class UploadFailures
{
    public sealed record Line(int LineNo, string Text, string Error);

    public sealed class Args
    {
        public required UploadRow File { get; init; }
        public List<Line> Lines { get; init; } = [];
    }
}
