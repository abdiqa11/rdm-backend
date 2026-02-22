using Microsoft.EntityFrameworkCore;
using RdmApi.Data;
using RdmApi.Services;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddDbContext<RdmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<S3ObjectStore>();



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    // Mock identity from headers
    var actor = ctx.Request.Headers["X-Actor"].FirstOrDefault() ?? "anonymous";
    var role = ctx.Request.Headers["X-Role"].FirstOrDefault() ?? "Viewer";

    // Normalize role
    role = role.Trim();
    if (!RdmApi.Security.Roles.IsValid(role))
        role = RdmApi.Security.Roles.Viewer;

    ctx.Items["actor"] = actor;
    ctx.Items["role"] = role;

    await next();
});


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");
app.MapControllers();


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
