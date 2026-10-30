namespace IisDash.Shared.Filters;

public sealed partial class FilterBar
{
    public sealed class Args
    {
        /// <summary>The page the filter applies to: "/" or "/requests".</summary>
        public required string Action { get; init; }

        public required FilterOptions.Snapshot Options { get; init; }

        /// <summary>Other query values the page keeps when the filter changes (the requests list's sort).</summary>
        public IReadOnlyList<(string Key, string Value)> Keep { get; init; } = [];
    }
}
