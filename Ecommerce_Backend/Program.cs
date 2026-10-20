using Ecommerce_Backend.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddAppAuthentication(builder.Configuration);
builder.Services.AddServiceBusMessaging(builder.Configuration);
builder.Services.AddAppServices();
builder.Services.AddDevelopmentSeedOptions(builder.Configuration);
builder.Services.AddAppHealthChecks(builder.Configuration);

var app = builder.Build();

await app.SeedDevelopmentDataAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapAppHealthChecks();

app.Run();

public partial class Program { }
