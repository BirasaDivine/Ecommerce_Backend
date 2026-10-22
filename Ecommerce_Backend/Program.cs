using Ecommerce_Backend.Extensions;

var builder = WebApplication.CreateBuilder(args);

var roles = builder.Services.AddAppRoles(builder.Configuration);

builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddServiceBusMessaging(builder.Configuration, roles);
builder.Services.AddAppHealthChecks(builder.Configuration);
builder.Services.AddAppVersion(builder.Configuration);
builder.Services.AddAppTelemetry(builder.Configuration);

if (roles.ApiEnabled)
{
    builder.Services.AddApiServices(builder.Configuration);
}

var app = builder.Build();

if (roles.ApiEnabled)
{
    await app.UseApiPipelineAsync();
}

app.MapAppHealthChecks();
app.MapAppVersion();

app.Run();

public partial class Program { }
