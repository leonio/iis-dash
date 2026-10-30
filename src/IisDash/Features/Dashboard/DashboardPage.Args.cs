using IisDash.Shared.Filters;

namespace IisDash.Features.Dashboard;

public sealed partial class DashboardPage
{
    public sealed class Args
    {
        public required DashboardData Data { get; init; }
        public required FilterOptions.Snapshot Options { get; init; }
    }
}
