using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Api.Features.Auth;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Access;

public sealed class SignInTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SignedInClients.Owner, UserRole.Owner, null)]
    [InlineData(SignedInClients.KitchenerManager, UserRole.BranchManager, "KIT")]
    [InlineData(SignedInClients.GuelphDispatcher, UserRole.Dispatcher, "GUE")]
    [InlineData(SignedInClients.GuelphCsr, UserRole.Csr, "GUE")]
    [InlineData(SignedInClients.GuelphTechnician, UserRole.Technician, "GUE")]
    public async Task Every_demo_role_can_sign_in_and_see_who_they_are(string email, UserRole role, string? branch)
    {
        using var client = await factory.SignedInAsAsync(email);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions.Api, Ct);

        Assert.NotNull(me);
        Assert.Equal(role, me.Role);
        Assert.Equal(branch, me.Branch?.Code);
    }

    [Fact]
    public async Task Capabilities_follow_the_role()
    {
        using var technician = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        using var dispatcher = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var tech = (await technician.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions.Api, Ct))!.Can;
        var disp = (await dispatcher.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions.Api, Ct))!.Can;

        Assert.False(tech.CanDispatch);
        Assert.False(tech.CanReviewCalls);
        Assert.True(disp.CanDispatch);
        Assert.False(disp.CanSeeFinancials);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_answer()
    {
        // A user of its own: a failed attempt changes the user row (the lockout counter), and
        // other tests signing in as the shared demo users at the same moment would then hit
        // Identity's optimistic concurrency check.
        const string email = "wrong-password@kestrel.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                await users.CreateAsync(new AppUser { UserName = email, Email = email, DisplayName = "Wrong Password", Role = UserRole.Owner }, "correct-horse-battery");
            }
        }

        using var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "not-the-password" }, Ct);
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/sign-in", new { email = "nobody@kestrel.test", password = "not-the-password" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_the_api_answers_401_instead_of_redirecting()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var client = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        (await client.PostAsync("/api/auth/sign-out", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", Ct)).StatusCode);
    }
}
