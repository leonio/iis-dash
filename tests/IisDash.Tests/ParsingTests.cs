using IisDash.Features.Uploads.Parsing;

namespace IisDash.Tests;

public sealed class ParsingTests
{
    [Theory]
    [InlineData("#Software: Microsoft Internet Information Services 10.0", LogFormat.W3C)]
    [InlineData("﻿#Fields: date time cs-uri-stem", LogFormat.W3C)]
    [InlineData("Date,Time,Client IP Address,User Name", LogFormat.Iis)]
    [InlineData("192.168.1.5, -, 10/24/2026, 7:55:20, W3SVC1, WEB01, 10.0.0.1, 120, 300, 4000, 200, 0, GET, /index.htm, -,", LogFormat.Iis)]
    [InlineData("# iis-dash", LogFormat.Unknown)]
    [InlineData("hello, world", LogFormat.Unknown)]
    [InlineData("", LogFormat.Unknown)]
    public void The_format_comes_from_the_first_line(string line, LogFormat expected) =>
        Assert.Equal(expected, LogFormatDetector.Detect(line));

    [Fact]
    public void W3C_lines_follow_the_fields_header_and_a_later_one()
    {
        var parser = new LogLineParser(LogFormat.W3C);
        Assert.True(parser.Parse("#Fields: date time cs-method cs-uri-stem cs-uri-query sc-status time-taken cs(User-Agent)").Skipped);
        var first = parser.Parse("2026-10-30 14:05:09 GET /api/orders/42 page=2 503 1234 Mozilla/5.0+(Windows)").Record!;
        Assert.Equal(new DateTime(2026, 10, 30, 14, 5, 9, DateTimeKind.Utc), DateTime.UnixEpoch.AddSeconds(first.Ts!.Value));
        Assert.Equal(("GET", "/api/orders/42", "page=2", 503, 1234L), (first.Method, first.UriStem, first.UriQuery, first.Status, first.TimeTakenMs));
        Assert.Equal("Mozilla/5.0 (Windows)", first.UserAgent);

        // IIS writes the header again after a restart, possibly with other fields.
        Assert.True(parser.Parse("#Fields: date time c-ip sc-status").Skipped);
        var second = parser.Parse("2026-10-30 15:00:00 10.0.0.9 200").Record!;
        Assert.Equal(("10.0.0.9", 200), (second.ClientIp, second.Status));

        var wrong = parser.Parse("2026-10-30 15:00:00 10.0.0.9");
        Assert.Null(wrong.Record);
        Assert.Contains("3 fields", wrong.Error);
    }

    [Fact]
    public void IIS_format_lines_in_iis_own_layout()
    {
        var parser = new LogLineParser(LogFormat.Iis);
        var record = parser.Parse("192.168.1.5, CONTOSO\\a.meyer, 10/24/2026, 7:55:20, W3SVC1, WEB01, 10.0.0.1, 120, 300, 4000, 404, 2, GET, /index.htm, a=1,").Record!;
        Assert.Equal(new DateTime(2026, 10, 24, 7, 55, 20, DateTimeKind.Utc), DateTime.UnixEpoch.AddSeconds(record.Ts!.Value));
        Assert.Equal(("192.168.1.5", "CONTOSO\\a.meyer", "GET", "/index.htm", "a=1"), (record.ClientIp, record.Username, record.Method, record.UriStem, record.UriQuery));
        Assert.Equal((404, 120L, 300L, 4000L), (record.Status, record.TimeTakenMs, record.BytesReceived, record.BytesSent));
    }

    [Fact]
    public void IIS_format_lines_under_a_header()
    {
        var parser = new LogLineParser(LogFormat.Iis);
        Assert.True(parser.Parse("Date,Time,Client IP Address,Method,URI Stem,Protocol Status,Time Taken,User Agent").Skipped);
        var record = parser.Parse("2026-10-24,08:00:00,10.1.1.1,POST,/api/x,201,15,\"Agent, with comma\"").Record!;
        Assert.Equal(("POST", 201, "Agent, with comma"), (record.Method, record.Status, record.UserAgent));
    }

    [Theory]
    [InlineData("/api/orders/42", "/api/orders/:id")]
    [InlineData("/api/customers/3f2504e0-4f89-11d3-9a0c-0305e82c3301/orders", "/api/customers/:id/orders")]
    [InlineData("/static/app.v2.css", "/static/app.v2.css")]
    [InlineData("/Orders/List.aspx", "/orders/list.aspx")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Ids_in_paths_fold_into_one_endpoint(string? stem, string endpoint) =>
        Assert.Equal(endpoint, EndpointNormalizer.Normalize(stem));

    [Theory]
    [InlineData("/Scripts/jquery-1.8.2.min.js", true)]
    [InlineData("/ScriptResource.axd", true)]
    [InlineData("/webresource.axd", true)]
    [InlineData("/content/site.CSS", true)]
    [InlineData("/img/logo.svg", true)]
    [InlineData("/fonts/x.woff2", true)]
    [InlineData("/Orders/List.aspx", false)]
    [InlineData("/api/orders/42", false)]
    [InlineData("/trace.axd", false)]
    [InlineData("/", false)]
    [InlineData(null, false)]
    public void Static_files_are_told_apart_from_pages_and_apis(string? stem, bool isStatic) =>
        Assert.Equal(isStatic, StaticFiles.IsStatic(stem));
}
