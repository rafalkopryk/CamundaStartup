using Duende.IdentityServer;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Test;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Camunda.Startup.IdentityServer.Pages.Account;

public class LoginModel(
    TestUserStore users,
    IIdentityServerInteractionService interaction)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? Error { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var context = await interaction.GetAuthorizationContextAsync(ReturnUrl);

        if (!users.ValidateCredentials(Username, Password))
        {
            Error = "Invalid username or password.";
            return Page();
        }

        var user = users.FindByUsername(Username);

        var identityServerUser = new IdentityServerUser(user.SubjectId)
        {
            DisplayName = user.Username,
            AdditionalClaims = user.Claims.ToArray(),
        };

        await HttpContext.SignInAsync(
            IdentityServerConstants.DefaultCookieAuthenticationScheme,
            identityServerUser.CreatePrincipal());

        if (interaction.IsValidReturnUrl(ReturnUrl) || Url.IsLocalUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl!);
        }

        return Redirect("~/");
    }
}
