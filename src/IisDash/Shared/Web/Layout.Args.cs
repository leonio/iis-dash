namespace IisDash.Shared.Web;

public sealed partial class Layout
{
    public sealed class Args
    {
        public string Title { get; init; } = "";

        /// <summary>The header's current section: dashboard, requests or uploads.</summary>
        public string Section { get; init; } = "";
    }
}
