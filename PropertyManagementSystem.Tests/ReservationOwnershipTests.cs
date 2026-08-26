using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationOwnershipTests
{
    [Fact]
    public void IsOwner_MatchesRequestorEmailCaseInsensitively()
    {
        var reservation = new GFMS_ReservationHeader
        {
            RequestorEmail = "trisha@example.com"
        };

        Assert.True(ReservationOwnership.IsOwner(
            reservation,
            "TRISHA@EXAMPLE.COM"));
    }

    [Fact]
    public void IsOwner_DoesNotUseApproverOrDisplayName()
    {
        var reservation = new GFMS_ReservationHeader
        {
            Requestor = "Trisha Paran",
            RequestorEmail = "trisha@example.com",
            ApproverEmail = "jerwin@example.com"
        };

        Assert.False(ReservationOwnership.IsOwner(
            reservation,
            "jerwin@example.com"));
    }
}
