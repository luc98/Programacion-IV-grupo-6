using System.Security.Claims;
using AvailabilitySite.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace AvailabilitySite.Web.Authentication;

public sealed class HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserContext
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(UserId);

    public string? UserId => User.FindFirstValue("sub");
}
