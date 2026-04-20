using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var publicOrigin = builder.Configuration["Authentication:PublicOrigin"];

builder.Services.AddRazorPages();

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient("RdmApi", (sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["RdmApi:BaseUrl"] ?? "http://localhost:8095";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = "https://thurs.uia.no:4444";
        options.ClientId = "e18dcfcf-975e-44df-8d8d-3dea346cb4d3";
        options.ClientSecret = "930vEpz58JQgg5uzvUwR5zRpEo";

        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.CallbackPath = "/auth/callback";

        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("offline_access");

        options.RequireHttpsMetadata = false;

        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SameSite = SameSiteMode.None;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

        // Let the handler build redirect_uri from the current request (and X-Forwarded-* via
        // UseForwardedHeaders). A hardcoded https://…/auth/callback was sending users back to
        // port 443 while nginx routed that host to a different app (400 "Host is not trusted").
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            if (Uri.TryCreate(publicOrigin, UriKind.Absolute, out var originUri))
            {
                context.ProtocolMessage.RedirectUri = $"{originUri.Scheme}://{originUri.Authority}{options.CallbackPath}";
                return Task.CompletedTask;
            }

            var request = context.Request;
            context.ProtocolMessage.RedirectUri = $"{request.Scheme}://{request.Host}{options.CallbackPath}";
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost,
    ForwardLimit = 1,
    RequireHeaderSymmetry = false
};

forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

if (Uri.TryCreate(publicOrigin, UriKind.Absolute, out var configuredOrigin))
{
    app.Use(async (context, next) =>
    {
        var request = context.Request;
        var isSameHost = string.Equals(request.Host.Host, configuredOrigin.Host, StringComparison.OrdinalIgnoreCase);
        var isSameScheme = string.Equals(request.Scheme, configuredOrigin.Scheme, StringComparison.OrdinalIgnoreCase);
        var isSafeMethod = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

        if ((!isSameHost || !isSameScheme) && isSafeMethod)
        {
            var target = $"{configuredOrigin.Scheme}://{configuredOrigin.Authority}{request.PathBase}{request.Path}{request.QueryString}";
            context.Response.Redirect(target, permanent: false);
            return;
        }

        await next();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Map("/login", async context =>
{
    var props = new AuthenticationProperties
    {
        RedirectUri = "/"
    };

    props.Items["prompt"] = "login";

    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, props);
});

app.Map("/logout", async context =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/");
});

app.MapRazorPages();

app.Run();