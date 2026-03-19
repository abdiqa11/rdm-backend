using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace RdmFrontend.Pages;

[Authorize]
public class ClaimsModel : PageModel
{
    public List<Claim> UserClaims { get; private set; } = new();

    public void OnGet()
    {
        UserClaims = User.Claims.ToList();
    }
}
