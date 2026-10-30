using System.IO.Compression;
using IisDash.Features.Export;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;
using Microsoft.AspNetCore.Http;

namespace IisDash.Tests;

public sealed class WebTests
{
    [Fact]
    public void Every_language_has_every_text()
    {
        var english = I18n.Catalogue("en").Keys.ToHashSet();
        Assert.Contains("de", I18n.Languages);
        foreach (string language in I18n.Languages)
        {
            var keys = I18n.Catalogue(language).Keys.ToHashSet();
            Assert.Empty(english.Except(keys));
            Assert.Empty(keys.Except(english));
        }
    }

    [Fact]
    public void Numbers_and_times_the_way_each_language_writes_them()
    {
        var en = new Formats("en");
        var de = new Formats("de");
        long ts = (long)(new DateTime(2026, 10, 30, 14, 5, 9, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;
        Assert.Equal(("12,345", "12.345"), (en.N(12345), de.N(12345)));
        Assert.Equal(("4.2%", "4,2 %"), (en.Pct(0.042), de.Pct(0.042)));
        Assert.Equal(("1.24 s", "1,24 s"), (en.Ms(1240), de.Ms(1240)));
        Assert.Equal(("2026-10-30 14:05:09", "30.10.2026 14:05:09"), (en.When(ts), de.When(ts)));
        Assert.Equal(("Oct 30", "30. Okt."), (en.Day(ts), de.Day(ts)));
    }

    [Fact]
    public void The_filter_reads_the_url_drops_what_does_not_parse_and_becomes_sql()
    {
        var filter = LogFilter.From(new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["from"] = "2026-10-30T14:00",
            ["status"] = "5xx",
            ["path"] = "50%_off",
            ["hour"] = "25",
            ["method"] = "get; drop table",
            ["sort"] = "time",
        }));

        Assert.Equal("from=2026-10-30T14%3A00&status=5xx&path=50%25_off", filter.Query());
        var (where, args) = filter.Sql();
        Assert.Equal("WHERE ts >= $from AND status >= 500 AND status < 600 AND uri_stem LIKE $path ESCAPE '\\'", where);
        Assert.Equal("%50\\%\\_off%", args["$path"]);
        Assert.Equal("/?status=5xx", filter.Without("from").Without("path").Url("/"));
    }

    [Fact]
    public void A_workbook_excel_can_open()
    {
        var stream = new MemoryStream();
        using (var x = new XlsxWriter(stream))
        {
            x.Sheet("Figures & more", ("Name", 20), ("Count", 10), ("When", 20), ("Share", 10));
            x.Row("a <b>", 12L, new DateTime(2026, 10, 30, 14, 0, 0), new Percent(0.25));
            x.Row("bell\u0007", null, null, 1.5);
            x.Finish();
        }

        stream.Position = 0;
        using var zip = new ZipArchive(stream);
        Assert.Equal(["xl/worksheets/sheet1.xml", "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml"],
            zip.Entries.Select(e => e.FullName));
        string sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("<t xml:space=\"preserve\">a &lt;b&gt;</t>", sheet);
        Assert.Contains("<c r=\"B2\" s=\"3\"><v>12</v></c>", sheet);
        Assert.Contains("<c r=\"D2\" s=\"4\"><v>0.25</v></c>", sheet);
        Assert.Contains(">bell<", sheet);
        Assert.Contains("<autoFilter ref=\"A1:D3\" />", sheet);
        string workbook = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd();
        Assert.Contains("name=\"Figures &amp; more\"", workbook);
    }
}
