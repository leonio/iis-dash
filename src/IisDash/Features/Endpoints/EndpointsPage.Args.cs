using IisDash.Shared.Filters;

namespace IisDash.Features.Endpoints;

public sealed partial class EndpointsPage
{
    public sealed class Args
    {
        public required EndpointsPageData Data { get; init; }
        public required FilterOptions.Snapshot Options { get; init; }
    }
}
