using AvailabilitySite.Application.Common;
using AvailabilitySite.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilitySite.Web.Controllers;

// Traducción única de ApplicationError a respuestas HTTP y pantallas de error.
public abstract class AppController : Controller
{
    protected IActionResult ErrorPage(ApplicationError error, string? retryUrl = null, params PageLink[] links)
    {
        // Sin sesión: se redirige al login de Keycloak y después se vuelve a esta misma página.
        if (error.Type == ApplicationErrorType.Unauthenticated)
            return Challenge();

        var (status, title) = error.Type switch
        {
            ApplicationErrorType.NotFound => (StatusCodes.Status404NotFound, "No encontrado"),
            ApplicationErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Acceso no autorizado"),
            ApplicationErrorType.Validation => (StatusCodes.Status400BadRequest, "Datos inválidos"),
            _ => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible por el momento"),
        };

        return StatusPage(status, title, error.Message, retryUrl, links);
    }

    protected IActionResult StatusPage(int status, string title, string message, string? retryUrl = null, params PageLink[] links)
    {
        Response.StatusCode = status;
        return View("StatusPage", new StatusPageViewModel
        {
            StatusCode = status,
            Title = title,
            Message = message,
            RetryUrl = retryUrl,
            Links = links.Length > 0 ? links : [new PageLink("Ir a eventos", Url.Action("Index", "Home")!)],
        });
    }

}
