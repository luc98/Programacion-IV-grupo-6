using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Infrastructure.Mock;

namespace AvailabilitySite.Web.Security;

// Usuario FIJO de ejemplo: todavía no hay login.
// TODO(backend): leer el usuario de la sesión (claim "sub" del servidor de autorización) y
// devolver IsAuthenticated = false cuando no hay sesión.
public sealed class ExampleCurrentUserContext : ICurrentUserContext
{
    public bool IsAuthenticated => true;

    public string? UserId => MockData.ExampleUserId;
}
