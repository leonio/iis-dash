using System.Reflection;

namespace Server.Features.Version;

public static class GetVersion
{
    public record Response(string Version);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/version", () =>
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly?.GetName().Version?.ToString()
                          ?? "Unknown";
            return Results.Ok(new Response(version));
        });
    }
}
