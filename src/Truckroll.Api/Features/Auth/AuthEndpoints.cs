using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Auth;

/// <summary>
/// Sign in, sign out, and "who am I". The web app calls these through its same-origin rewrite,
/// so the session cookie is first-party.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/sign-in", SignInAsync).AllowAnonymous()
            .WithSummary("Check an email and password and set the session cookie.");

        auth.MapPost("/sign-out", SignOutAsync).RequireAuthorization()
            .WithSummary("Clear the session cookie.");

        auth.MapGet("/me", GetMeAsync).RequireAuthorization()
            .WithSummary("The signed-in person, their role and what they can do.");

        return routes;
    }

    internal static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult, ValidationProblem>> SignInAsync(
        SignInRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, TruckrollDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Enter your email and password."],
            });
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Same answer as a wrong password, so the endpoint can't be used to discover accounts.
            return TypedResults.Unauthorized();
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await DescribeAsync(user, db));
    }

    internal static async Task<NoContent> SignOutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    internal static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMeAsync(
        AccessScope scope, UserManager<AppUser> users, SignInManager<AppUser> signIn, TruckrollDbContext db)
    {
        var user = scope.UserId is null ? null : await users.FindByIdAsync(scope.UserId);
        if (user is null)
        {
            // A cookie for an account that no longer exists (the demo data was reset, say). Drop
            // it, so the browser stops presenting it and the web app's proxy sends them to sign in.
            await signIn.SignOutAsync();
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await DescribeAsync(user, db));
    }

    private static async Task<MeResponse> DescribeAsync(AppUser user, TruckrollDbContext db)
    {
        var scope = AccessScope.ForUser(user);
        var branch = await scope.Branches(db)
            .Where(b => b.Id == user.BranchId)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name))
            .SingleOrDefaultAsync();

        return new MeResponse(
            user.Id,
            user.DisplayName,
            user.Email ?? "",
            user.Role,
            branch,
            user.TechnicianId,
            new Capabilities(scope.CanDispatch, scope.CanReviewCalls, scope.CanBookJobs, scope.CanSeeFinancials));
    }
}

public sealed record SignInRequest(string Email, string Password);

/// <summary>Who's signed in, as the web app needs it to draw navigation and hide what they can't do.</summary>
public sealed record MeResponse(
    string Id,
    string DisplayName,
    string Email,
    UserRole Role,
    BranchSummary? Branch,
    int? TechnicianId,
    Capabilities Can);

public sealed record BranchSummary(int Id, string Code, string Name);

/// <summary>
/// What the person can do, for the UI to show or hide controls. The API enforces the same rules
/// independently; hiding a button is a courtesy, not security.
/// </summary>
public sealed record Capabilities(bool CanDispatch, bool CanReviewCalls, bool CanBookJobs, bool CanSeeFinancials);
