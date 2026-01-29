using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization();

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

app.UseCors("ClientCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/hello", (HttpContext context) =>
{
    var name = context.User.Identity?.Name ?? "";
    return Results.Text(name);
}).RequireAuthorization();

app.Run();
