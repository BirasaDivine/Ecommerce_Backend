using Ecommerce_Backend.Extensions;

var builder = WebApplication.CreateBuilder(args);

var roles = builder.Services.AddAppRoles(builder.Configuration);

builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddServiceBusMessaging(builder.Configuration, roles);
builder.Services.AddAppHealthChecks(builder.Configuration);

if (roles.ApiEnabled)
{
    builder.Services.AddApiServices(builder.Configuration);
}

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

if (roles.ApiEnabled)
{
    await app.UseApiPipelineAsync();
}

app.MapAppHealthChecks();

app.Run();

public partial class Program { }
