using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;

namespace IisDash.Features.Dashboard;

public static class DashboardRoutes
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/", (HttpContext http, Db db, FilterOptions options) =>
        {
            var filter = LogFilter.From(http.Request.Query);
            return Html.Page(DashboardPage.Create(new DashboardPage.Args { Data = DashboardQuery.Load(db, filter), Options = options.Current }));
        });
    }
}
