using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Controllers;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class GfmsUserAccessPolicyTests
{
    [Theory]
    [InlineData("OtherApp.Admin")]
    [InlineData("GFMS.Unknown")]
    [InlineData("gfms.System Admin")]
    public void Rejects_roles_outside_the_supported_catalog(string role)
    {
        Assert.NotNull(GfmsUserAccessPolicy.ValidateSelection(new[] { role }, false));
    }

    [Fact]
    public void Removing_access_preserves_roles_owned_by_other_modules()
    {
        var remove = GfmsUserAccessPolicy.RolesToRemove(
            new[] { "OtherApp.Admin", "GFMS.FutureRole", ReservationRoles.Requestor, ReservationRoles.AdminApprover },
            new[] { ReservationRoles.Requestor });
        Assert.Equal(new[] { ReservationRoles.AdminApprover }, remove);
    }

    [Fact]
    public void Administrator_can_revoke_all_gfms_roles_from_another_user()
    {
        Assert.Null(GfmsUserAccessPolicy.ValidateSelection(Array.Empty<string>(), false));
        Assert.Equal(GfmsUserAccessPolicy.Roles, GfmsUserAccessPolicy.RolesToRemove(
            GfmsUserAccessPolicy.Roles.Append("OtherApp.User"), Array.Empty<string>()));
    }

    [Fact]
    public void Administrator_must_retain_own_system_admin_access()
    {
        Assert.NotNull(GfmsUserAccessPolicy.ValidateSelection(new[] { ReservationRoles.Requestor }, true));
        Assert.Null(GfmsUserAccessPolicy.ValidateSelection(new[] { ReservationRoles.SystemAdmin }, true));
    }

    [Fact]
    public void Controller_requires_active_system_admin_and_antiforgery_on_save()
    {
        var attributes = typeof(UserAccessController).GetCustomAttributes(true);
        Assert.Contains(attributes, attribute => attribute.GetType() == typeof(AuthorizeAttribute));
        var role = Assert.Single(attributes.OfType<ActiveRoleAuthorizeAttribute>());
        Assert.Equal("ActiveRolePolicy:GFMS.System Admin", role.Policy);
        var save = typeof(UserAccessController).GetMethods().Single(method =>
            method.Name == "Edit" && method.GetCustomAttributes(typeof(HttpPostAttribute), true).Any());
        Assert.Single(save.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }
}
