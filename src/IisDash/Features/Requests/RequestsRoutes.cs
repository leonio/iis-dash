using System.Globalization;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;

namespace IisDash.Features.Requests;

public static class RequestsRoutes
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/requests", (HttpContext http, Db db, FilterOptions options) =>
        {
            var query = http.Request.Query;
            int page = int.TryParse(query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? p : 1;
            var data = RequestsQuery.Load(db, LogFilter.From(query), query["sort"], query["dir"], page);
            return Html.Page(RequestsPage.Create(new RequestsPage.Args { Data = data, Options = options.Current }));
        });

        app.MapGet("/requests/{id:long}", (long id, HttpContext http, Db db) =>
            RequestDetailData.Load(db, id) is { } request
                ? Html.Page(RequestDetail.Create(request))
                : Html.Page(Error.Create(I18n.T(Ui.Language(http), "error.no_request")), 404));
    }
}
