using AvailabilitySite.Web.Composition;
using AvailabilitySite.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilitySite.Web.Controllers;

// Inicio y cierre de sesión. La autenticación la hace Keycloak (OIDC configurado en Program.cs):
// acá se muestra la pantalla de login, se redirige a Keycloak y se cierra la sesión local y la de Keycloak.
[AllowAnonymous]
public class AccountController : Controller
{
    // Alias del proveedor de Google configurado en el realm de Keycloak.
    private const string GoogleIdentityProvider = "google";

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        var target = LocalTarget(returnUrl);

        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(target);

        return View(new LoginViewModel { ReturnUrl = target });
    }

    [HttpPost]
    [ActionName("Login")]
    public IActionResult LoginWithGoogle(string? returnUrl = null)
    {
        // kc_idp_hint hace que Keycloak pase directo a Google sin mostrar su propia pantalla.
        var properties = new AuthenticationProperties { RedirectUri = LocalTarget(returnUrl) };
        properties.Items[AvailabilityServiceRegistration.IdentityProviderHintKey] = GoogleIdentityProvider;
        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
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

    // Solo URLs locales: evita usar el login para redirigir a otro sitio.
    private string LocalTarget(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home")!;
}
