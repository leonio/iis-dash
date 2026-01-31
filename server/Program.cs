using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Features.Logs.Precheck;
using Server.Features.Logs.Shared;
using Server.Features.Logs.Upload;
using Server.Features.Logs.Uploads;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<IisLogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("IisLogDb")));

builder.Services.AddScoped<LogIngestionService>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientCors", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IisLogDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseCors("ClientCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/hello", (HttpContext context) =>
{
    var name = context.User.Identity?.Name ?? "";
    return Results.Text(name);
}).RequireAuthorization();

app.MapLogPrecheckEndpoints();
app.MapLogUploadEndpoints();
app.MapLogUploadsEndpoints();

app.Run();
