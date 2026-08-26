using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class DormHousingEligibilityPolicyTests
{
    [Fact]
    public void Dorm_and_housing_application_filing_is_disabled()
    {
        Assert.False(DormHousingApplicationFeature.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(DormHousingApplicationFeature.UnavailableMessage));
    }

    [Theory]
    [InlineData("SINGLE")]
    [InlineData("MARRIED")]
    [InlineData("WIDOWED")]
    [InlineData("")]
    [InlineData(null)]
    public void Dorm_is_available_regardless_of_civil_status(string? civilStatus)
    {
        Assert.True(DormHousingEligibilityPolicy.CanApplyForUnit(
            civilStatus,
            DormHousingEligibilityPolicy.DormUnit));
    }

    [Theory]
    [InlineData("MARRIED")]
    [InlineData("Married")]
    [InlineData(" married ")]
    public void Married_employee_can_apply_for_housing(string civilStatus)
    {
        Assert.True(DormHousingEligibilityPolicy.CanApplyForUnit(
            civilStatus,
            DormHousingEligibilityPolicy.HousingUnit));
    }

    [Theory]
    [InlineData("SINGLE")]
    [InlineData("WIDOWED")]
    [InlineData("")]
    [InlineData(null)]
    public void Employee_who_is_not_married_cannot_apply_for_housing(string? civilStatus)
    {
        Assert.False(DormHousingEligibilityPolicy.CanApplyForUnit(
            civilStatus,
            DormHousingEligibilityPolicy.HousingUnit));
    }

    [Theory]
    [InlineData("SINGLE", "Single")]
    [InlineData(" married ", "Married")]
    [InlineData(null, "Not specified")]
    public void Civil_status_has_a_friendly_display_label(string? civilStatus, string expected)
    {
        Assert.Equal(expected, DormHousingEligibilityPolicy.GetCivilStatusLabel(civilStatus));
    }
}
