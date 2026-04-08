using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace RdmFrontend.Pages;

[Authorize]
public class ClaimsModel : PageModel
{
    public List<Claim> UserClaims { get; private set; } = new();

    public string? AccessToken { get; private set; }
    public string? IdToken { get; private set; }

    public async Task OnGet()
    {
        UserClaims = User.Claims.ToList();
        AccessToken = await HttpContext.GetTokenAsync("access_token");
        IdToken = await HttpContext.GetTokenAsync("id_token");
    }
}