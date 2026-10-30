using IisDash.Features.Uploads.Parsing;

namespace IisDash.Features.Uploads;

public sealed partial class Precheck
{
    public sealed record Item(string Name, long Size, LogFormat Format, bool TooLarge);
}
