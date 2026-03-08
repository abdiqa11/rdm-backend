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

    // X-Role
    c.AddSecurityDefinition("XRoleHeader", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Role",
        Description = "Prototype role: Admin | Researcher | Viewer"
    });

    // X-Actor
    c.AddSecurityDefinition("XActorHeader", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Actor",
        Description = "Prototype actor identity (e.g., teacher, abdi, researcher1)"
    });

    // Require both headers (Swagger will send them on every request after Authorize)
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "XRoleHeader"
                }
            },
            Array.Empty<string>()
        },
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

// Header-based identity middleware (your prototype RBAC)
app.Use(async (ctx, next) =>
{
    var actor = ctx.Request.Headers["X-Actor"].FirstOrDefault() ?? "anonymous";
    var role = ctx.Request.Headers["X-Role"].FirstOrDefault() ?? "Viewer";

    role = role.Trim();
    if (!RdmApi.Security.Roles.IsValid(role))
        role = RdmApi.Security.Roles.Viewer;

    ctx.Items["actor"] = actor;
    ctx.Items["role"] = role;

    await next();
});

app.MapControllers();

app.Run();