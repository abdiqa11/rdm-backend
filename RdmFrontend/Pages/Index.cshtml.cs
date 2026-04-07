using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RdmFrontend.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public string? ActorEmail { get; private set; }
    public string? BackendResponse { get; private set; }
    public int? BackendStatusCode { get; private set; }
    public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;

    public IndexModel(ILogger<IndexModel> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public void OnGet()
    {
        LoadActorEmail();
    }

    public async Task OnPostTestBackendAsync()
    {
        LoadActorEmail();

        if (!IsAuthenticated)
        {
            BackendStatusCode = 401;
            BackendResponse = "User is not signed in.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ActorEmail))
        {
            BackendStatusCode = 0;
            BackendResponse = "No email claim found for the logged-in user.";
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("RdmApi");

            client.DefaultRequestHeaders.Remove("X-Actor");
            client.DefaultRequestHeaders.Add("X-Actor", ActorEmail);

            var payload = new
            {
                title = "Frontend RBAC test dataset",
                creator = "Frontend FEIDE user",
                description = "Created from Razor frontend test"
            };

            var response = await client.PostAsJsonAsync("/datasets", payload);

            BackendStatusCode = (int)response.StatusCode;
            BackendResponse = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while calling backend API.");
            BackendStatusCode = -1;
            BackendResponse = $"Backend call failed: {ex.Message}";
        }
    }

    private void LoadActorEmail()
    {
        if (!IsAuthenticated)
        {
            ActorEmail = null;
            return;
        }

        ActorEmail =
            User.FindFirst("email")?.Value ??
            User.FindFirst(ClaimTypes.Email)?.Value ??
            User.FindFirst("preferred_username")?.Value;
    }
}