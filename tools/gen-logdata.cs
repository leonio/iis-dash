// Sample IIS logs that look like a real site's: a working-day rhythm, quieter weekends, a few endpoints carrying most
// of the traffic, static files answered 304, an older Web Forms part of the site that loads its scripts through
// ScriptResource.axd on every page, a scanner poking for wp-login.php, a slow report export, and one incident (the
// orders API failing for half an hour on the fifth day). One file per day, named as IIS names them.
//
//   dotnet run tools/gen-logdata.cs                      7 days of W3C logs into ./sample-logs
//   dotnet run tools/gen-logdata.cs -- iis 3 2000 out    3 days in IIS's own format, ~2,000 requests a day, into ./out
//
// Arguments, all optional, in order: format (w3c or iis), days, requests per weekday, output folder.
// The same arguments always give the same files.

using System.Globalization;
using System.Text;

var format = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "w3c";
var days = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 7;
var perDay = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 24_000;
var outDir = args.Length > 3 ? args[3] : "sample-logs";
Directory.CreateDirectory(outDir);

var random = new Random(20261030);
var firstDay = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);

// path, weight, methods, median ms, error rate (5xx), not-found rate (4xx)
Endpoint[] endpoints =
[
    new("/", 9, ["GET"], 45, 0.001, 0),
    new("/api/orders", 14, ["GET", "GET", "GET", "POST"], 120, 0.003, 0.004),
    new("/api/orders/{id}", 16, ["GET", "GET", "GET", "PUT", "DELETE"], 85, 0.002, 0.02),
    new("/api/customers/{guid}", 8, ["GET", "GET", "PUT"], 95, 0.002, 0.03),
    new("/api/products", 10, ["GET"], 60, 0.001, 0),
    new("/api/products/{id}/stock", 6, ["GET"], 140, 0.004, 0.01),
    new("/api/search", 7, ["GET"], 320, 0.006, 0),
    new("/api/reports/export", 1.2, ["POST"], 2400, 0.02, 0),
    new("/account/login", 3, ["GET", "POST"], 180, 0.002, 0),
    new("/health", 4, ["GET"], 4, 0, 0),
    new("/static/app.css", 6, ["GET"], 6, 0, 0),
    new("/static/app.js", 6, ["GET"], 8, 0, 0),
    new("/static/img/logo.svg", 4, ["GET"], 5, 0, 0),
    // The older Web Forms part: its pages in whatever case links happen to use, and its scripts, uncached.
    new("/Orders/List.aspx", 5, ["GET", "GET", "POST"], 260, 0.004, 0),
    new("/Scripts/jquery-1.8.2.min.js", 9, ["GET"], 9, 0, 0),
    new("/Scripts/WebForms/MSAjax/MicrosoftAjax.js", 6, ["GET"], 11, 0, 0),
    new("/ScriptResource.axd", 14, ["GET"], 14, 0.001, 0),
    new("/WebResource.axd", 8, ["GET"], 10, 0, 0),
    new("/Content/Site.css", 5, ["GET"], 6, 0, 0),
];
var totalWeight = endpoints.Sum(e => e.Weight);

string[] agents =
[
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:144.0) Gecko/20100101 Firefox/144.0",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15",
    "Mozilla/5.0 (iPhone; CPU iPhone OS 26_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148",
    "OrdersSync/3.2 (+https://intranet/orders-sync)",
    "curl/8.15.0",
];
string[] users = ["-", "-", "-", "-", "CONTOSO\\a.meyer", "CONTOSO\\j.schmidt", "CONTOSO\\l.weber", "CONTOSO\\svc-orders"];
var clients = Enumerable.Range(0, 400).Select(_ => random.Next(4) == 0
    ? $"10.20.{random.Next(1, 40)}.{random.Next(2, 250)}"
    : $"{random.Next(80, 220)}.{random.Next(0, 255)}.{random.Next(0, 255)}.{random.Next(1, 254)}").ToArray();
const string Scanner = "185.220.101.47";

for (var day = 0; day < days; day++)
{
    var date = firstDay.AddDays(day);
    bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    var lines = new List<(DateTime Time, string Line)>();

    // Requests spread over the day by a working-day curve (UTC; the site's users are in central Europe).
    int count = (int)(perDay * (weekend ? 0.38 : 1.0) * (0.92 + random.NextDouble() * 0.16));
    double[] hourly = [.. Enumerable.Range(0, 24).Select(h => Rhythm(h, weekend))];
    double hourlyTotal = hourly.Sum();
    for (var i = 0; i < count; i++)
    {
        int hour = Pick(hourly, hourlyTotal);
        var time = date.AddHours(hour).AddSeconds(random.Next(3600));
        var endpoint = PickEndpoint();
        bool incident = day == 4 && time.Hour == 14 && time.Minute is >= 10 and < 42 && endpoint.Path.StartsWith("/api/orders", StringComparison.Ordinal);
        lines.Add((time, Line(time, endpoint, incident)));
    }

    // The scanner: a burst of 404s on the third evening.
    if (day == 2)
    {
        string[] probes = ["/wp-login.php", "/.env", "/xmlrpc.php", "/admin/config.php", "/phpmyadmin/index.php", "/.git/config"];
        for (var i = 0; i < 900; i++)
        {
            var time = date.AddHours(21).AddSeconds(random.Next(25 * 60));
            lines.Add((time, Raw(time, Scanner, "-", "GET", probes[random.Next(probes.Length)], "-", 404, 0, 2, random.Next(1100, 1400), "Mozilla/5.0 zgrab/0.x", "-")));
        }
    }

    lines.Sort((a, b) => a.Time.CompareTo(b.Time));
    string name = Path.Combine(outDir, $"u_ex{date:yyMMdd}.log");
    using var writer = new StreamWriter(name, false, new UTF8Encoding(false));
    if (format == "w3c")
    {
        writer.WriteLine("#Software: Microsoft Internet Information Services 10.0");
        writer.WriteLine("#Version: 1.0");
        writer.WriteLine($"#Date: {date:yyyy-MM-dd} 00:00:00");
        writer.WriteLine("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status sc-bytes cs-bytes time-taken");
    }

    foreach (var (_, line) in lines) writer.WriteLine(line);
    Console.WriteLine($"{name}: {lines.Count:N0} requests");
}

double Rhythm(int hour, bool weekend)
{
    // Quiet at night, a morning rise from 06:00 UTC, a lunch dip, a slow evening fade.
    double working = Math.Exp(-Math.Pow((hour - 9.5) / 2.6, 2)) + 0.85 * Math.Exp(-Math.Pow((hour - 14) / 2.4, 2));
    double evening = 0.35 * Math.Exp(-Math.Pow((hour - 19.5) / 2.2, 2));
    return 0.05 + (weekend ? 0.45 * working + 0.9 * evening : working + evening);
}

int Pick(double[] weights, double total)
{
    double r = random.NextDouble() * total;
    for (int i = 0; i < weights.Length; i++)
    {
        r -= weights[i];
        if (r <= 0) return i;
    }

    return weights.Length - 1;
}

Endpoint PickEndpoint()
{
    double r = random.NextDouble() * totalWeight;
    foreach (var e in endpoints)
    {
        r -= e.Weight;
        if (r <= 0) return e;
    }

    return endpoints[^1];
}

string Line(DateTime time, Endpoint e, bool incident)
{
    string method = e.Methods[random.Next(e.Methods.Length)];
    string path = e.Path.Replace("{id}", random.Next(1000, 99999).ToString(CultureInfo.InvariantCulture))
                        .Replace("{guid}", Guid.NewGuid().ToString());
    if (e.Path == "/Orders/List.aspx") path = new[] { "/Orders/List.aspx", "/orders/list.aspx", "/ORDERS/List.aspx" }[random.Next(3)];
    string query = e.Path switch
    {
        "/api/search" => "q=" + new[] { "chair", "desk", "lamp", "monitor+arm", "cable" }[random.Next(5)] + "&page=" + random.Next(1, 4),
        "/api/orders" when method == "GET" => "status=" + new[] { "open", "shipped", "all" }[random.Next(3)],
        "/ScriptResource.axd" or "/WebResource.axd" => $"d={Convert.ToBase64String(BitConverter.GetBytes(random.NextInt64())).TrimEnd('=')}&t={random.Next(10000000, 99999999)}",
        "/Orders/List.aspx" => "page=" + random.Next(1, 30),
        _ => "-",
    };

    bool isStatic = e.Path.StartsWith("/static", StringComparison.Ordinal) || e.Path.StartsWith("/Content", StringComparison.Ordinal);
    bool uncached = e.Path.StartsWith("/Scripts", StringComparison.Ordinal) || e.Path.EndsWith(".axd", StringComparison.Ordinal);
    int status = method == "POST" ? 201 : method == "DELETE" ? 204 : 200;
    if (isStatic && random.NextDouble() < 0.6) status = 304;
    if (random.NextDouble() < e.NotFound) status = 404;
    if (e.Path == "/account/login" && method == "POST" && random.NextDouble() < 0.08) status = 401;
    if (method is "POST" or "PUT" && random.NextDouble() < 0.01) status = 400;
    if (method == "GET" && !isStatic && random.NextDouble() < 0.02) status = 302;
    if (random.NextDouble() < e.Errors) status = 500;
    if (incident && random.NextDouble() < 0.45) status = random.NextDouble() < 0.7 ? 503 : 500;

    double median = e.MedianMs * (incident ? 4.5 : 1) * (time.Hour is >= 8 and <= 16 ? 1.25 : 1);
    long ms = Math.Max(1, (long)(median * Math.Exp(Gaussian() * 0.75)));
    if (status == 304) ms = Math.Max(1, ms / 3);
    if (status == 503) ms = random.Next(30_000, 30_150);
    long sent = status switch
    {
        304 or 204 => random.Next(180, 260),
        404 or 401 or 400 => random.Next(1100, 1500),
        >= 500 => random.Next(1200, 3800),
        _ => isStatic || uncached ? random.Next(8_000, 140_000) : e.Path == "/api/reports/export" ? random.Next(400_000, 2_400_000) : random.Next(600, 24_000),
    };
    string client = e.Path == "/health" ? "10.20.0.5" : clients[(int)Math.Min(clients.Length - 1, Math.Floor(Math.Pow(random.NextDouble(), 2.2) * clients.Length))];
    string agent = e.Path == "/health" ? "kube-probe/1.33" : client.StartsWith("10.20.", StringComparison.Ordinal) && random.Next(3) == 0 ? agents[4] : agents[random.Next(agents.Length - 2)];
    string user = e.Path.StartsWith("/api", StringComparison.Ordinal) ? users[random.Next(users.Length)] : "-";
    string referer = isStatic || e.Path == "/" ? "https://shop.contoso.example/" : "-";
    return Raw(time, client, user, method, path, query, status, status == 503 ? 2 : 0, ms, sent, agent, referer);
}

string Raw(DateTime time, string client, string user, string method, string path, string query, int status, int sub, long ms, long sent, string agent, string referer)
{
    long received = method is "POST" or "PUT" ? random.Next(400, 6000) : random.Next(300, 900);
    int win32 = status == 503 ? 64 : 0;
    if (format == "w3c")
    {
        return string.Join(' ', time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "10.20.1.10", method, path, query, "443", user, client, agent.Replace(' ', '+'), referer,
            status.ToString(CultureInfo.InvariantCulture), sub.ToString(CultureInfo.InvariantCulture), win32.ToString(CultureInfo.InvariantCulture),
            sent.ToString(CultureInfo.InvariantCulture), received.ToString(CultureInfo.InvariantCulture), ms.ToString(CultureInfo.InvariantCulture));
    }

    // IIS's own format: client, user, date, time, service, server, server IP, time taken, bytes received, bytes sent,
    // status, Win32 status, method, target, parameters, and a trailing comma.
    return string.Join(", ", client, user, time.ToString("M/d/yyyy", CultureInfo.InvariantCulture), time.ToString("H:mm:ss", CultureInfo.InvariantCulture),
        "W3SVC1", "WEB01", "10.20.1.10", ms.ToString(CultureInfo.InvariantCulture), received.ToString(CultureInfo.InvariantCulture),
        sent.ToString(CultureInfo.InvariantCulture), status.ToString(CultureInfo.InvariantCulture), win32.ToString(CultureInfo.InvariantCulture),
        method, path, query) + ",";
}

double Gaussian() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

record Endpoint(string Path, double Weight, string[] Methods, double MedianMs, double Errors, double NotFound);
