using System.Security.Claims;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Reflection;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RdmApi.Data;
using RdmApi.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Controllers + API explorer
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RdmApi",
        Version = "v1"
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }

    c.MapType<IFormFile>(() => new OpenApiSchema
    {
        Type = "string",
        Format = "binary"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste a bearer token: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Opaque token"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
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
builder.Services.AddSingleton<RdmApi.Security.DatasetOwnershipAuthorizer>();
builder.Services.AddHttpClient();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Introspection";
        options.DefaultChallengeScheme = "Introspection";
        options.DefaultForbidScheme = "Introspection";
    })
    .AddScheme<AuthenticationSchemeOptions, IntrospectionAuthenticationHandler>(
        "Introspection",
        _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

// Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Opaque token introspection middleware
app.UseAuthentication();

app.Use(async (ctx, next) =>
{
    var resolver = ctx.RequestServices.GetRequiredService<RdmApi.Security.UserRoleResolver>();
    var httpClientFactory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();

    string actor = "anonymous";
    string role;

    var authHeader = ctx.Request.Headers.Authorization.ToString();

    if (!string.IsNullOrWhiteSpace(authHeader) &&
        authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = authHeader["Bearer ".Length..].Trim();

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var client = httpClientFactory.CreateClient();

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://thurs.uia.no:4445/admin/oauth2/introspect");

                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["client_id"] = "e18dcfcf-975e-44df-8d8d-3dea346cb4d3",
                    ["client_secret"] = "930vEpz58JQgg5uzvUwR5zRpEo"
                });

                var response = await client.SendAsync(request, ctx.RequestAborted);
                var body = await response.Content.ReadAsStringAsync(ctx.RequestAborted);

                Console.WriteLine($"INTROSPECTION HTTP: {(int)response.StatusCode}");
                Console.WriteLine($"INTROSPECTION BODY: {body}");

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    var active =
                        root.TryGetProperty("active", out var activeProp) &&
                        activeProp.ValueKind == JsonValueKind.True;

                    if (active)
                    {
                        var sub = TryGetString(root, "sub");
                        var email = TryGetString(root, "email");
                        var preferredUsername = TryGetString(root, "preferred_username");

                        actor =
                            sub ??
                            email ??
                            preferredUsername ??
                            "authenticated-user";

                        actor = actor.Trim();

                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, actor),
                            new Claim("actor", actor)
                        };

                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            claims.Add(new Claim(ClaimTypes.Email, email));
                            claims.Add(new Claim("email", email));
                        }

                        if (!string.IsNullOrWhiteSpace(sub))
                        {
                            claims.Add(new Claim("sub", sub));
                            claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
                        }

                        var identity = new ClaimsIdentity(claims, "Introspection");
                        ctx.User = new ClaimsPrincipal(identity);

                        Console.WriteLine($"AUTH DEBUG: active token, actor={actor}");
                    }
                    else
                    {
                        Console.WriteLine("AUTH DEBUG: token inactive");
                    }
                }
                else
                {
                    Console.WriteLine("AUTH DEBUG: introspection request failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AUTH DEBUG: introspection exception = " + ex.Message);
            }
        }
    }
    else
    {
        Console.WriteLine("AUTH DEBUG: no bearer token found");
    }

    role = resolver.ResolveRole(
        ctx.User.FindFirst("sub")?.Value,
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        ctx.User.FindFirst(ClaimTypes.Email)?.Value,
        ctx.User.FindFirst("email")?.Value,
        actor);

    var resolvedEmail =
        ctx.User.FindFirst(ClaimTypes.Email)?.Value
        ?? ctx.User.FindFirst("email")?.Value
        ?? "unknown-email";
    Console.WriteLine($"User {resolvedEmail} resolved as role {role}");
    Console.WriteLine($"ROLE DEBUG: actor={actor}, role={role}");

    ctx.Items["actor"] = actor;
    ctx.Items["role"] = role;

    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();

static string? TryGetString(JsonElement root, string propertyName)
{
    if (root.TryGetProperty(propertyName, out var prop) &&
        prop.ValueKind == JsonValueKind.String)
    {
        return prop.GetString();
    }

    return null;
}

sealed class IntrospectionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public IntrospectionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(Context.User, Scheme.Name)));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}