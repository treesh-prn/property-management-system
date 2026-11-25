using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyManagementSystem.Areas.Identity.Pages.Account;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class LogoutTests
{
    [Theory]
    [InlineData(true, "/Home/Index")]
    [InlineData(false, "/Home/Index")]
    [InlineData(false, null)]
    public async Task Logout_clears_sessions_and_redirects_to_login_without_dashboard_callback(bool hasAuthServer, string? returnUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=NotUsed;Integrated Security=true"));
        services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        var authentication = new RecordingAuthentication();
        services.AddSingleton<IAuthenticationService>(authentication);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:AuthServer"] = hasAuthServer ? "https://auth.example.invalid" : null,
            ["Jwt:AppUrl"] = "https://gfms.example.invalid"
        }).Build();
        var page = new LogoutModel(signIn, NullLogger<LogoutModel>.Instance, configuration);

        var result = await page.OnPost(returnUrl);

        Assert.Contains(IdentityConstants.ApplicationScheme, authentication.SignedOutSchemes);
        Assert.Contains(IdentityConstants.ExternalScheme, authentication.SignedOutSchemes);
        Assert.Contains(IdentityConstants.TwoFactorUserIdScheme, authentication.SignedOutSchemes);
        if (hasAuthServer)
            Assert.Equal("https://auth.example.invalid/Identity/Account/Login",
                Assert.IsType<RedirectResult>(result).Url);
        else
            Assert.Equal("/Account/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
    }

    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public List<string?> SignedOutSchemes { get; } = new();
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutSchemes.Add(scheme);
            return Task.CompletedTask;
        }
    }
}
