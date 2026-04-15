using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace RdmFrontend.Pages;

[Authorize]
public class ClaimsModel : PageModel
{
    public List<Claim> UserClaims { get; private set; } = new();

    public string? AccessToken { get; private set; }

    public async Task OnGet()
    {
        UserClaims = User.Claims.ToList();
        AccessToken = await HttpContext.GetTokenAsync("access_token");
    }
}