using IisDash.Shared.Filters;

namespace IisDash.Features.Requests;

public sealed partial class RequestsPage
{
    public sealed class Args
    {
        public required RequestsPageData Data { get; init; }
        public required FilterOptions.Snapshot Options { get; init; }
    }
}
