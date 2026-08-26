using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class ReservationAssignmentRulesTests
{
    private static readonly DateTime Day = new(2026, 8, 1, 8, 0, 0);

    [Fact]
    public void AdjacentReservationsDoNotOverlap()
    {
        Assert.False(ReservationAssignmentService.Overlaps(
            Day, Day.AddHours(2), Day.AddHours(2), Day.AddHours(4)));
    }

    [Fact]
    public void IntersectingReservationsOverlap()
    {
        Assert.True(ReservationAssignmentService.Overlaps(
            Day, Day.AddHours(2), Day.AddHours(1), Day.AddHours(3)));
    }

    [Fact]
    public void InactiveRoomCannotBeAssigned()
    {
        var error = Rules(roomActive: false);
        Assert.Contains("inactive", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongTypeRoomCannotBeAssigned()
    {
        var error = Rules(expectedRoomType: 1, actualRoomType: 0);
        Assert.Contains("reservation type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunctionRoomAttendeeCapacityIsAdvisory()
    {
        var error = Rules(roomCapacity: 10, requestedOccupants: 11);
        Assert.Null(error);
    }

    [Fact]
    public void SharedRoomStillRejectsRequestedOccupantsBeyondCapacity()
    {
        var error = Rules(
            expectedRoomType: ReservationRoomTypes.Guesthouse,
            actualRoomType: ReservationRoomTypes.Guesthouse,
            roomCapacity: 2,
            requestedOccupants: 3,
            exclusiveUse: false);

        Assert.Contains("capacity", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DormBedCapacityIncludesOverlappingOccupants()
    {
        var error = Rules(
            roomCapacity: 4,
            requestedOccupants: 2,
            overlappingOccupants: 3,
            exclusiveUse: false);
        Assert.Contains("occupies", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunctionRoomRejectsAnyOverlappingReservation()
    {
        var error = Rules(overlappingOccupants: 1, exclusiveUse: true);
        Assert.Contains("occupies", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuesthouseRoomAllowsOccupancyWithinCapacity()
    {
        var error = Rules(
            expectedRoomType: ReservationRoomTypes.Guesthouse,
            actualRoomType: ReservationRoomTypes.Guesthouse,
            roomCapacity: 3,
            requestedOccupants: 1,
            overlappingOccupants: 1,
            exclusiveUse: false);

        Assert.Null(error);
    }

    [Fact]
    public void GuesthouseRoomRejectsOccupancyBeyondCapacity()
    {
        var error = Rules(
            expectedRoomType: ReservationRoomTypes.Guesthouse,
            actualRoomType: ReservationRoomTypes.Guesthouse,
            roomCapacity: 3,
            requestedOccupants: 1,
            overlappingOccupants: 3,
            exclusiveUse: false);

        Assert.Contains("occupies", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleRowVersionDoesNotMatch()
    {
        Assert.False(ReservationAssignmentService.RowVersionMatches(
            new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }));
    }

    private static string Rules(
        bool roomActive = true,
        bool facilityActive = true,
        int selectedFacilityId = 10,
        int roomFacilityId = 10,
        int expectedRoomType = 1,
        int actualRoomType = 1,
        int roomCapacity = 10,
        int requestedOccupants = 1,
        int overlappingOccupants = 0,
        bool exclusiveUse = true) =>
        ReservationAssignmentService.ValidateRoomRules(
            roomActive,
            facilityActive,
            selectedFacilityId,
            roomFacilityId,
            expectedRoomType,
            actualRoomType,
            roomCapacity,
            requestedOccupants,
            overlappingOccupants,
            exclusiveUse)!;
}
