using System.Globalization;

var format = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "w3c";
var endpoints = new[]
{
    "/",
    "/api/hello",
    "/api/logs/upload",
    "/api/logs/precheck",
    "/api/logs/uploads",
    "/admin",
    "/health",
    "/assets/app.css",
    "/assets/app.js",
    "/signin",
};

var random = new Random();
var now = DateTimeOffset.Now;
var rows = 200;

if (format == "w3c")
{
    var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var outputFile = $"iis-w3c-{stamp}.log";
    using var writer = new StreamWriter(outputFile);

    writer.WriteLine("#Software: IIS Log Generator");
    writer.WriteLine("#Version: 1.0");
    writer.WriteLine($"#Date: {now:yyyy-MM-dd HH:mm:ss}");
    writer.WriteLine("#Fields: date time c-ip cs-username cs-method cs-uri-stem cs-uri-query sc-status sc-substatus sc-win32-status sc-bytes cs-bytes time-taken cs(User-Agent) cs(Referer) cs-host s-ip s-port cs-version s-sitename s-computername");

    for (var i = 0; i < rows; i++)
    {
        var timestamp = now.AddSeconds(-random.Next(0, 86400));
        var endpoint = endpoints[random.Next(endpoints.Length)];
        var uriStem = endpoint.Split('?', StringSplitOptions.RemoveEmptyEntries)[0];
        var uriQuery = endpoint.Contains('?') ? endpoint.Split('?', StringSplitOptions.RemoveEmptyEntries).Last() : "-";
        var status = Pick(new[] { 200, 200, 200, 304, 400, 401, 403, 404, 500 }, random);
        var subStatus = status == 500 ? 0 : random.Next(0, 10);
        var win32 = status == 500 ? 123 : 0;
        var bytesSent = random.Next(512, 150000);
        var bytesRecv = random.Next(0, 4096);
        var timeTaken = random.Next(1, 4000);
        var userAgent = Pick(new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
            "curl/8.5.0",
            "PostmanRuntime/7.37.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        }, random);
        var referer = Pick(new[] { "-", "http://localhost:5173/", "http://intranet/dashboard" }, random);

        writer.WriteLine(string.Join(' ', new[]
        {
            timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            RandomIp(random),
            Pick(new[] { "-", "DOMAIN\\user1", "DOMAIN\\user2" }, random),
            Pick(new[] { "GET", "POST", "PUT" }, random),
            uriStem,
            uriQuery,
            status.ToString(CultureInfo.InvariantCulture),
            subStatus.ToString(CultureInfo.InvariantCulture),
            win32.ToString(CultureInfo.InvariantCulture),
            bytesSent.ToString(CultureInfo.InvariantCulture),
            bytesRecv.ToString(CultureInfo.InvariantCulture),
            timeTaken.ToString(CultureInfo.InvariantCulture),
            QuoteIfNeeded(userAgent),
            QuoteIfNeeded(referer),
            "localhost",
            "10.0.0.1",
            "443",
            "HTTP/1.1",
            "Default Web Site",
            "IIS-SERVER",
        }));
    }

    Console.WriteLine($"Generated W3C log file: {outputFile}");
    return;
}

if (format == "iis")
{
    var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    var outputFile = $"iis-csv-{stamp}.log";
    using var writer = new StreamWriter(outputFile);

    writer.WriteLine(string.Join(',', new[]
    {
        "Date",
        "Time",
        "Client IP Address",
        "User Name",
        "Service Name",
        "Server Name",
        "Server IP Address",
        "Server Port",
        "Method",
        "URI Stem",
        "URI Query",
        "Protocol Status",
        "Win32 Status",
        "Bytes Sent",
        "Bytes Received",
        "Time Taken",
        "Protocol Version",
        "Host",
        "User Agent",
        "Cookie",
        "Referer",
    }));

    for (var i = 0; i < rows; i++)
    {
        var timestamp = now.AddSeconds(-random.Next(0, 86400));
        var endpoint = endpoints[random.Next(endpoints.Length)];
        var uriStem = endpoint.Split('?', StringSplitOptions.RemoveEmptyEntries)[0];
        var uriQuery = endpoint.Contains('?') ? endpoint.Split('?', StringSplitOptions.RemoveEmptyEntries).Last() : string.Empty;
        var status = Pick(new[] { 200, 200, 200, 304, 400, 401, 403, 404, 500 }, random);
        var win32 = status == 500 ? 123 : 0;
        var bytesSent = random.Next(512, 150000);
        var bytesRecv = random.Next(0, 4096);
        var timeTaken = random.Next(1, 4000);
        var userAgent = Pick(new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
            "curl/8.5.0",
            "PostmanRuntime/7.37.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        }, random);
        var referer = Pick(new[] { "", "http://localhost:5173/", "http://intranet/dashboard" }, random);

        writer.WriteLine(string.Join(',', new[]
        {
            timestamp.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            RandomIp(random),
            Pick(new[] { "", "DOMAIN\\user1", "DOMAIN\\user2" }, random),
            "W3SVC1",
            "IIS-SERVER",
            "10.0.0.1",
            "443",
            Pick(new[] { "GET", "POST", "PUT" }, random),
            uriStem,
            uriQuery,
            status.ToString(CultureInfo.InvariantCulture),
            win32.ToString(CultureInfo.InvariantCulture),
            bytesSent.ToString(CultureInfo.InvariantCulture),
            bytesRecv.ToString(CultureInfo.InvariantCulture),
            timeTaken.ToString(CultureInfo.InvariantCulture),
            "HTTP/1.1",
            "localhost",
            QuoteCsv(userAgent),
            "",
            QuoteCsv(referer),
        }));
    }

    Console.WriteLine($"Generated IIS CSV log file: {outputFile}");
    return;
}

Console.WriteLine("Usage: dotnet run --project gen-logdata.cs [w3c|iis]");
Console.WriteLine("Defaults to w3c if not provided.");

static string RandomIp(Random random)
{
    return $"192.168.{random.Next(0, 255)}.{random.Next(1, 254)}";
}

static T Pick<T>(IReadOnlyList<T> values, Random random)
{
    return values[random.Next(values.Count)];
}

static string QuoteIfNeeded(string value)
{
    return value.Contains(' ') ? $"\"{value}\"" : value;
}

static string QuoteCsv(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return value;
    }

    if (value.Contains(',') || value.Contains('"'))
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    return value;
}
