using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilitySite.Web.Controllers;

// Inicio y cierre de sesión. La autenticación la hace Keycloak (OIDC configurado en Program.cs):
// acá solo se redirige al login y se cierra la sesión local y la de Keycloak.
[AllowAnonymous]
public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Solo URLs locales: evita usar el login para redirigir a otro sitio.
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home")!;

        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(target);

        return Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost]
    public IActionResult Logout()
    {
        // Cierra la cookie del sitio y la sesión en Keycloak; al volver se muestra la lista de eventos.
        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
