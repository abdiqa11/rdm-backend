using Microsoft.EntityFrameworkCore;
using RdmApi.Data;
using RdmApi.Services;


var builder = WebApplication.CreateBuilder(args);

// Controllers + API explorer (needed for Swagger)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "RdmApi",
        Version = "v1"
    });
    c.MapType<IFormFile>(() => new Microsoft.OpenApi.Models.OpenApiSchema
    {
        Type = "string",
        Format = "binary"
    });
    
    // X-Actor
    c.AddSecurityDefinition("XActorHeader", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Actor",
        Description = "Authenticated user identity (for example FEIDE email)"
    });

    // Require  Actor header (Swagger will send it on every request after Authorize)
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "XActorHeader"
                }
            },
            Array.Empty<string>()
        }
    });
});

// DB + services
builder.Services.AddDbContext<RdmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<S3ObjectStore>();
builder.Services.AddSingleton<RdmApi.Security.UserRoleResolver>();

var app = builder.Build();

// Swagger UI (best for demo)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Avoid HTTPS redirect confusion in local demo
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    var resolver = ctx.RequestServices.GetRequiredService<RdmApi.Security.UserRoleResolver>();

    // 1. Get actor (from frontend → FEIDE identity)
    var actor = ctx.Request.Headers["X-Actor"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(actor))
        actor = "anonymous";

    actor = actor.Trim();

    // 2. Resolve role from backend config (NOT from headers anymore)
    var role = resolver.ResolveRole(actor);

    // 3. Store in HttpContext (used by RequireRoleAttribute)
    ctx.Items["actor"] = actor;
    ctx.Items["role"] = role;

    await next();
});

app.MapControllers();

app.Run();