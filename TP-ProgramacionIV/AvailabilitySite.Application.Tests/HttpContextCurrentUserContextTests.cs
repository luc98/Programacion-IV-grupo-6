using System.Security.Claims;
using AvailabilitySite.Web.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class HttpContextCurrentUserContextTests
{
    [Fact]
    public void AuthenticatedUser_UsesSubjectClaim()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "keycloak-user-1")], "oidc"))
        };
        var accessor = new HttpContextAccessor { HttpContext = context };

        var currentUser = new HttpContextCurrentUserContext(accessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal("keycloak-user-1", currentUser.UserId);
    }

    [Fact]
    public void AnonymousUser_IsNotAuthenticated()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var currentUser = new HttpContextCurrentUserContext(accessor);

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void AuthenticatedUserWithoutSubject_IsNotAuthenticated()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "user")], "oidc"))
        };
        var accessor = new HttpContextAccessor { HttpContext = context };

        var currentUser = new HttpContextCurrentUserContext(accessor);

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }
}
