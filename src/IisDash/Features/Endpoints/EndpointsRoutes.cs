using System.Globalization;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;

namespace IisDash.Features.Endpoints;

public static class EndpointsRoutes
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/endpoints", (HttpContext http, Db db, FilterOptions options) =>
        {
            var query = http.Request.Query;
            int page = int.TryParse(query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? p : 1;
            var data = EndpointsQuery.Load(db, LogFilter.From(query), query["sort"], query["dir"], page);
            return Html.Page(EndpointsPage.Create(new EndpointsPage.Args { Data = data, Options = options.Current }));
        });
    }
}
