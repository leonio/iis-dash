namespace Server.Features.Version;

public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        GetVersion.Map(app);
        return app;
    }
}
