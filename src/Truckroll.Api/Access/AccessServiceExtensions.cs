using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Access;

/// <summary>Sign-in (Identity with a cookie) and the per-request <see cref="AccessScope"/>.</summary>
public static class AccessServiceExtensions
{
    /// <summary>The sign-in cookie's name. The web app's proxy checks for it to redirect early.</summary>
    public const string CookieName = "truckroll.auth";

    public static IServiceCollection AddTruckrollAccess(this IServiceCollection services)
    {
        // The browser signs in with a cookie; AI clients use a personal token (MCP only).
        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddScheme<AuthenticationSchemeOptions, PersonalTokenHandler>(PersonalTokenHandler.SchemeName, null);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PersonalTokens>();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<TruckrollDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<TruckrollClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = CookieName;
            cookie.Cookie.HttpOnly = true;
            // Lax: sent on same-site requests and top-level navigation, never on a cross-site POST.
            // With JSON-only endpoints that's the CSRF defence (docs/adr/006-same-origin-api.md).
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(12);
            cookie.SlidingExpiration = true;

            // An API answers with status codes; the web app decides where to send people.
            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // The cookie carries the role and branch, so it would keep working after the account
        // behind it changed or was deleted. Re-check it against the user record every minute
        // (Identity's default is every 30); a cookie for a missing or changed user is dropped.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddAuthorization(options =>
            // /mcp takes tokens and only tokens: a browser cookie can't drive it, and a token
            // can't open the web app (whose endpoints use the default, cookie, scheme).
            options.AddPolicy(PersonalTokenHandler.SchemeName, policy => policy
                .AddAuthenticationSchemes(PersonalTokenHandler.SchemeName)
                .RequireAuthenticatedUser()));
        services.AddHttpContextAccessor();
        services.AddScoped(provider =>
        {
            var user = provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
            return user is null ? AccessScope.Anonymous : AccessScope.FromPrincipal(user);
        });

        return services;
    }
}
