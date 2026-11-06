using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class ReservationAssignmentSqlServerTests
{
    [Fact]
    public async Task GuesthouseAutoApprovedReservation_SavesRoomsAndRemainsApproved()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsGuesthouseAutoApprove_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";
        try
        {
            await SeedGuesthouseAsync(connectionString, guestCount: 2, roomCapacity: 3);
            await using var context = CreateContext(connectionString);
            var header = await context.ReservationHeaders.SingleAsync();
            header.Status = ReservationStatuses.Approved;
            header.ApprovedBy = "admin.approver";
            await context.SaveChangesAsync();
            var details = await context.ReservationDetails.ToListAsync();
            var room = await context.Rooms.SingleAsync();

            var result = await new ReservationAssignmentService(context).ApproveAssignedReservationAsync(
                header.ReservationId, "GH", ReservationStatuses.Approved, header.RowVersion.ToArray(),
                ReservationStatuses.Approved, header.ApprovedBy,
                details.Select(detail => new ReservationRoomAssignmentSelection(detail.ReservationDetailId, room.RoomId)).ToList());

            Assert.True(result.Succeeded, result.Message);
            context.ChangeTracker.Clear();
            var saved = await context.ReservationHeaders.SingleAsync();
            Assert.Equal(ReservationStatuses.Approved, saved.Status);
            Assert.Equal("admin.approver", saved.ApprovedBy);
            Assert.All(await context.ReservationDetails.ToListAsync(), detail => Assert.Equal(room.RoomId, detail.RoomId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GuesthouseSoftAssignments_SaveOnlyWhenApprovalCommits()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsGuesthouseSoftAssign_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedGuesthouseAsync(connectionString, guestCount: 2, roomCapacity: 3);

            await using var context = CreateContext(connectionString);
            var header = await context.ReservationHeaders.SingleAsync();
            var details = await context.ReservationDetails.OrderBy(detail => detail.ReservationDetailId).ToListAsync();
            var room = await context.Rooms.SingleAsync();

            Assert.All(details, detail => Assert.Null(detail.RoomId));

            var result = await new ReservationAssignmentService(context)
                .ApproveAssignedReservationAsync(
                    header.ReservationId,
                    "GH",
                    ReservationStatuses.AdminApproverApproval,
                    header.RowVersion.ToArray(),
                    ReservationStatuses.AdminManagerApproval,
                    "Admin Approver",
                    details.Select(detail => new ReservationRoomAssignmentSelection(
                        detail.ReservationDetailId,
                        room.RoomId)).ToList());

            Assert.True(result.Succeeded, result.Message);
            context.ChangeTracker.Clear();

            var savedHeader = await context.ReservationHeaders.SingleAsync();
            var savedDetails = await context.ReservationDetails.ToListAsync();
            Assert.Equal(ReservationStatuses.AdminManagerApproval, savedHeader.Status);
            Assert.All(savedDetails, detail => Assert.Equal(room.RoomId, detail.RoomId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GuesthouseSoftAssignments_RollBackWhenCapacityIsExceeded()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsGuesthouseSoftConflict_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedGuesthouseAsync(connectionString, guestCount: 4, roomCapacity: 3);

            await using var context = CreateContext(connectionString);
            var header = await context.ReservationHeaders.SingleAsync();
            var details = await context.ReservationDetails.OrderBy(detail => detail.ReservationDetailId).ToListAsync();
            var room = await context.Rooms.SingleAsync();

            var result = await new ReservationAssignmentService(context)
                .ApproveAssignedReservationAsync(
                    header.ReservationId,
                    "GH",
                    ReservationStatuses.AdminApproverApproval,
                    header.RowVersion.ToArray(),
                    ReservationStatuses.AdminManagerApproval,
                    "Admin Approver",
                    details.Select(detail => new ReservationRoomAssignmentSelection(
                        detail.ReservationDetailId,
                        room.RoomId)).ToList());

            Assert.False(result.Succeeded);
            context.ChangeTracker.Clear();

            var unchangedHeader = await context.ReservationHeaders.SingleAsync();
            var unchangedDetails = await context.ReservationDetails.ToListAsync();
            Assert.Equal(ReservationStatuses.AdminApproverApproval, unchangedHeader.Status);
            Assert.All(unchangedDetails, detail => Assert.Null(detail.RoomId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AdminManagerCanReassignGuesthouseRoomsDuringFinalApproval()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsGuesthouseManagerReassign_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedGuesthouseAsync(connectionString, guestCount: 2, roomCapacity: 2);

            await using var context = CreateContext(connectionString);
            var firstRoom = await context.Rooms.SingleAsync();
            var facility = await context.Facilities.SingleAsync();
            var secondRoom = new GFMS_Rooms
            {
                RoomName = "Apartment B",
                FacilityId = facility.FacilityId,
                RoomType = ReservationRoomTypes.Guesthouse,
                Capacity = 2,
                isActive = true
            };
            context.Rooms.Add(secondRoom);
            await context.SaveChangesAsync();

            var header = await context.ReservationHeaders.SingleAsync();
            var details = await context.ReservationDetails.OrderBy(detail => detail.ReservationDetailId).ToListAsync();
            var service = new ReservationAssignmentService(context);
            var approverResult = await service.ApproveAssignedReservationAsync(
                header.ReservationId,
                "GH",
                ReservationStatuses.AdminApproverApproval,
                header.RowVersion.ToArray(),
                ReservationStatuses.AdminManagerApproval,
                "Admin Approver",
                details.Select(detail => new ReservationRoomAssignmentSelection(
                    detail.ReservationDetailId,
                    firstRoom.RoomId)).ToList());
            Assert.True(approverResult.Succeeded, approverResult.Message);

            context.ChangeTracker.Clear();
            header = await context.ReservationHeaders.SingleAsync();
            details = await context.ReservationDetails.OrderBy(detail => detail.ReservationDetailId).ToListAsync();
            Assert.All(details, detail => Assert.Equal(firstRoom.RoomId, detail.RoomId));

            var managerResult = await new ReservationAssignmentService(context)
                .ApproveAssignedReservationAsync(
                    header.ReservationId,
                    "GH",
                    ReservationStatuses.AdminManagerApproval,
                    header.RowVersion.ToArray(),
                    ReservationStatuses.Approved,
                    "Admin Manager",
                    details.Select(detail => new ReservationRoomAssignmentSelection(
                        detail.ReservationDetailId,
                        secondRoom.RoomId)).ToList());

            Assert.True(managerResult.Succeeded, managerResult.Message);
            context.ChangeTracker.Clear();

            var approvedHeader = await context.ReservationHeaders.SingleAsync();
            var reassignedDetails = await context.ReservationDetails.ToListAsync();
            Assert.Equal(ReservationStatuses.Approved, approvedHeader.Status);
            Assert.All(reassignedDetails, detail => Assert.Equal(secondRoom.RoomId, detail.RoomId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task TwoParallelAssignments_ExactlyOneCommits()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsAssignment_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedAsync(connectionString);

            await using var firstContext = CreateContext(connectionString);
            await using var secondContext = CreateContext(connectionString);
            var firstHeader = await firstContext.ReservationHeaders.SingleAsync(h => h.ReservationId == 1);
            var firstDetail = await firstContext.ReservationDetails.SingleAsync(d => d.ReservationDetailId == 1);
            var secondHeader = await secondContext.ReservationHeaders.SingleAsync(h => h.ReservationId == 2);
            var secondDetail = await secondContext.ReservationDetails.SingleAsync(d => d.ReservationDetailId == 2);

            var start = new DateTime(2026, 8, 2, 9, 0, 0);
            var end = start.AddHours(2);
            var first = new ReservationAssignmentService(firstContext).AssignAsync(
                Command(firstHeader, firstDetail, start, end));
            var second = new ReservationAssignmentService(secondContext).AssignAsync(
                Command(secondHeader, secondDetail, start, end));

            var results = await Task.WhenAll(first, second);

            Assert.Single(results.Where(result => result.Succeeded));
            Assert.Single(results.Where(result =>
                !result.Succeeded && result.Failure == ReservationCommitFailure.Conflict));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AdjacentAssignments_BothCommit()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsAdjacent_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedAsync(connectionString);
            var start = new DateTime(2026, 8, 2, 9, 0, 0);

            await using (var firstContext = CreateContext(connectionString))
            {
                var header = await firstContext.ReservationHeaders.SingleAsync(h => h.ReservationId == 1);
                var detail = await firstContext.ReservationDetails.SingleAsync(d => d.ReservationDetailId == 1);
                var result = await new ReservationAssignmentService(firstContext)
                    .AssignAsync(Command(header, detail, start, start.AddHours(2)));
                Assert.True(result.Succeeded, result.Message);
            }

            await using (var secondContext = CreateContext(connectionString))
            {
                var header = await secondContext.ReservationHeaders.SingleAsync(h => h.ReservationId == 2);
                var detail = await secondContext.ReservationDetails.SingleAsync(d => d.ReservationDetailId == 2);
                var result = await new ReservationAssignmentService(secondContext)
                    .AssignAsync(Command(header, detail, start.AddHours(2), start.AddHours(4)));
                Assert.True(result.Succeeded, result.Message);
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AdminManagerSwap_AtomicallyExchangesFunctionRooms()
    {
        if (!OperatingSystem.IsWindows()) return;

        var databaseName = $"PmsFunctionRoomSwap_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

        try
        {
            await SeedFunctionRoomSwapAsync(connectionString);
            await using var context = CreateContext(connectionString);
            var headers = await context.ReservationHeaders.OrderBy(h => h.ReservationId).ToListAsync();
            var details = await context.ReservationDetails.OrderBy(d => d.ReservationId).ToListAsync();
            var rooms = await context.Rooms.OrderBy(r => r.RoomId).ToListAsync();
            var target = details[0];
            var conflict = details[1];

            var result = await new ReservationAssignmentService(context).SwapFunctionRoomAsync(
                new FunctionRoomSwapCommand(
                    new ReservationAssignmentCommand(
                        new ReservationSlotRequest(
                            headers[0].ReservationId,
                            target.ReservationDetailId,
                            rooms[1].FacilityId,
                            rooms[1].RoomId,
                            "FN",
                            target.StartDate!.Value,
                            target.EndDate!.Value,
                            target.NumberOfGuest,
                            ReservationRoomTypes.FunctionRoom),
                        ReservationStatuses.AdminManagerApproval,
                        headers[0].RowVersion.ToArray(),
                        target.RowVersion.ToArray(),
                        ReservationStatuses.Approved,
                        "Admin Manager"),
                    conflict.ReservationDetailId));

            Assert.True(result.Commit.Succeeded, result.Commit.Message);
            context.ChangeTracker.Clear();
            var saved = await context.ReservationDetails.OrderBy(d => d.ReservationId).ToListAsync();
            Assert.Equal(rooms[1].RoomId, saved[0].RoomId);
            Assert.Equal(rooms[0].RoomId, saved[1].RoomId);
            Assert.Equal(ReservationStatuses.Approved,
                (await context.ReservationHeaders.FindAsync(headers[0].ReservationId))!.Status);
            Assert.Equal(ReservationStatuses.Approved,
                (await context.ReservationHeaders.FindAsync(headers[1].ReservationId))!.Status);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static ReservationAssignmentCommand Command(
        GFMS_ReservationHeader header,
        GFMS_ReservationDetails detail,
        DateTime start,
        DateTime end) =>
        new(
            new ReservationSlotRequest(
                header.ReservationId,
                detail.ReservationDetailId,
                1,
                1,
                "FN",
                start,
                end,
                5,
                ReservationRoomTypes.FunctionRoom),
            header.Status!,
            header.RowVersion.ToArray(),
            detail.RowVersion.ToArray(),
            ReservationStatuses.AdminManagerApproval);

    private static async Task SeedAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.EnsureCreatedAsync();

        var facility = new GFMS_Facility
        {
            FacilityName = "Test Facility",
            isActive = true,
            Rooms = new List<GFMS_Rooms>()
        };
        var room = new GFMS_Rooms
        {
            RoomName = "Function Room A",
            Facility = facility,
            RoomType = ReservationRoomTypes.FunctionRoom,
            Capacity = 10,
            isActive = true
        };
        facility.Rooms.Add(room);
        context.Facilities.Add(facility);

        for (var id = 1; id <= 2; id++)
        {
            var header = new GFMS_ReservationHeader
            {
                GUID = Guid.NewGuid().ToString("N"),
                ReservationType = "FN",
                ReservationCode = $"TEST-FN-2026-{id:D4}",
                Status = ReservationStatuses.AdminApproverApproval,
                RequestorEmail = $"requestor{id}@example.test"
            };
            context.ReservationHeaders.Add(header);
            context.ReservationDetails.Add(new GFMS_ReservationDetails
            {
                ReservationHeader = header,
                NumberOfGuest = 5,
                StartDate = new DateTime(2026, 8, 2, 9, 0, 0),
                EndDate = new DateTime(2026, 8, 2, 11, 0, 0)
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedFunctionRoomSwapAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.EnsureCreatedAsync();
        var facility = new GFMS_Facility
        {
            FacilityName = "Swap Facility",
            isActive = true,
            Rooms = new List<GFMS_Rooms>()
        };
        facility.Rooms.Add(new GFMS_Rooms
        {
            RoomName = "Function Room A", Facility = facility,
            RoomType = ReservationRoomTypes.FunctionRoom, Capacity = 10, isActive = true
        });
        facility.Rooms.Add(new GFMS_Rooms
        {
            RoomName = "Function Room B", Facility = facility,
            RoomType = ReservationRoomTypes.FunctionRoom, Capacity = 10, isActive = true
        });
        context.Facilities.Add(facility);
        await context.SaveChangesAsync();

        for (var index = 0; index < 2; index++)
        {
            var header = new GFMS_ReservationHeader
            {
                GUID = Guid.NewGuid().ToString("N"), ReservationType = "FN",
                ReservationCode = $"SWAP-FN-{index + 1}",
                Status = index == 0
                    ? ReservationStatuses.AdminManagerApproval
                    : ReservationStatuses.Approved,
                RequestorEmail = $"swap{index + 1}@example.test"
            };
            context.ReservationHeaders.Add(header);
            context.ReservationDetails.Add(new GFMS_ReservationDetails
            {
                ReservationHeader = header,
                FacilityId = facility.FacilityId,
                RoomId = facility.Rooms.ElementAt(index).RoomId,
                NumberOfGuest = 5,
                StartDate = new DateTime(2026, 8, 12, 9, 0, 0),
                EndDate = new DateTime(2026, 8, 12, 11, 0, 0)
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedGuesthouseAsync(
        string connectionString,
        int guestCount,
        int roomCapacity)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.EnsureCreatedAsync();

        var facility = new GFMS_Facility
        {
            FacilityName = "Guesthouse",
            isActive = true,
            Rooms = new List<GFMS_Rooms>()
        };
        var room = new GFMS_Rooms
        {
            RoomName = "Apartment A",
            Facility = facility,
            RoomType = ReservationRoomTypes.Guesthouse,
            Capacity = roomCapacity,
            isActive = true
        };
        facility.Rooms.Add(room);
        context.Facilities.Add(facility);

        var header = new GFMS_ReservationHeader
        {
            GUID = Guid.NewGuid().ToString("N"),
            ReservationType = "GH",
            ReservationCode = "TEST-GH-2026-0001",
            Status = ReservationStatuses.AdminApproverApproval,
            RequestorEmail = "requestor@example.test"
        };
        context.ReservationHeaders.Add(header);

        for (var guest = 1; guest <= guestCount; guest++)
        {
            context.ReservationDetails.Add(new GFMS_ReservationDetails
            {
                ReservationHeader = header,
                GuestName = $"Guest {guest}",
                NumberOfGuest = 1,
                StartDate = new DateTime(2026, 8, 5, 14, 0, 0),
                EndDate = new DateTime(2026, 8, 6, 10, 0, 0)
            });
        }

        await context.SaveChangesAsync();
    }

    private static DAL CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DAL>()
            .UseSqlServer(connectionString)
            .Options;
        return new DAL(options);
    }
}
