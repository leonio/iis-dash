namespace IisDash.Features.Uploads;

public sealed partial class UploadsPage
{
    public sealed class Args
    {
        public List<UploadRow> Files { get; init; } = [];

        /// <summary>What the upload just made did, file by file; null when the page is only being looked at.</summary>
        public List<LogIngestion.FileOutcome>? Results { get; init; }
    }
}
