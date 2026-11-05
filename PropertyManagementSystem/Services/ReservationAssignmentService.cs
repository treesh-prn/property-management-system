using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class ReservationRoomTypes
    {
        public const int Guesthouse = 0;
        public const int FunctionRoom = 1;
        public const int DormOrHousing = 2;

        public static int? ForReservation(string? reservationType) => reservationType switch
        {
            "GH" => Guesthouse,
            "FN" => FunctionRoom,
            "DRM" => DormOrHousing,
            _ => null
        };
    }

    public enum ReservationCommitFailure
    {
        None,
        NotFound,
        Validation,
        Conflict
    }

    public sealed record ReservationCommitResult(
        bool Succeeded,
        ReservationCommitFailure Failure = ReservationCommitFailure.None,
        string? Message = null,
        GFMS_ReservationHeader? Reservation = null,
        GFMS_ReservationDetails? Detail = null)
    {
        public static ReservationCommitResult Ok(
            GFMS_ReservationHeader reservation,
            GFMS_ReservationDetails? detail = null) =>
            new(true, ReservationCommitFailure.None, null, reservation, detail);

        public static ReservationCommitResult Fail(
            ReservationCommitFailure failure,
            string message) => new(false, failure, message);
    }

    public sealed record ReservationSlotRequest(
        int ReservationId,
        int ReservationDetailId,
        int FacilityId,
        int RoomId,
        string ReservationType,
        DateTime StartDate,
        DateTime EndDate,
        int Occupants,
        int? ExpectedRoomType = null);

    public sealed record ReservationAssignmentCommand(
        ReservationSlotRequest Slot,
        string ExpectedStatus,
        byte[] ExpectedHeaderRowVersion,
        byte[] ExpectedDetailRowVersion,
        string? NextStatus = null,
        string? ApprovedBy = null,
        IReadOnlyCollection<int>? ManualEquipmentIds = null);

    public sealed record FunctionRoomSwapCommand(
        ReservationAssignmentCommand Assignment,
        int ConflictingReservationDetailId);

    public sealed record FunctionRoomSwapResult(
        ReservationCommitResult Commit,
        GFMS_ReservationHeader? ConflictingReservation = null,
        GFMS_ReservationDetails? ConflictingDetail = null);

    public sealed record GuestRoomSwapCommand(
        ReservationAssignmentCommand Assignment,
        int ConflictingReservationDetailId,
        byte[] ExpectedConflictingDetailRowVersion);

    public sealed record GuestRoomSwapResult(
        ReservationCommitResult Commit,
        GFMS_ReservationHeader? ConflictingReservation = null,
        GFMS_ReservationDetails? ConflictingDetail = null);

    public sealed record ReservationRoomAssignmentSelection(
        int ReservationDetailId,
        int RoomId);

    public interface IReservationAssignmentService
    {
        Task<ReservationCommitResult> ValidateAsync(
            ReservationSlotRequest request,
            CancellationToken cancellationToken = default);

        Task<ReservationCommitResult> ValidateFacilityAsync(
            int facilityId,
            string reservationType,
            CancellationToken cancellationToken = default);

        Task<List<RoomAssignmentOption>> GetAvailableRoomsAsync(
            int reservationDetailId,
            string reservationType,
            CancellationToken cancellationToken = default);

        Task<List<OccupiedRoomAssignmentOption>> GetOccupiedRoomsAsync(
            int reservationDetailId,
            string reservationType,
            CancellationToken cancellationToken = default);

        Task<ReservationCommitResult> AssignAsync(
            ReservationAssignmentCommand command,
            CancellationToken cancellationToken = default);

        Task<FunctionRoomSwapResult> SwapFunctionRoomAsync(
            FunctionRoomSwapCommand command,
            CancellationToken cancellationToken = default);

        Task<GuestRoomSwapResult> SwapGuestRoomAsync(
            GuestRoomSwapCommand command,
            CancellationToken cancellationToken = default);

        Task<ReservationCommitResult> ApproveAssignedReservationAsync(
            int reservationId,
            string reservationType,
            string expectedStatus,
            byte[] expectedRowVersion,
            string nextStatus,
            string? approvedBy,
            IReadOnlyCollection<ReservationRoomAssignmentSelection>? assignments = null,
            CancellationToken cancellationToken = default);

        Task<ReservationCommitResult> RescheduleAsync(
            int reservationId,
            string reservationType,
            string expectedStatus,
            byte[] expectedRowVersion,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default);
    }

    public sealed class ReservationAssignmentService : IReservationAssignmentService
    {
        private static readonly string[] ActiveStatuses =
        {
            ReservationStatuses.DepartmentHeadApproval,
            ReservationStatuses.AdminApproverApproval,
            ReservationStatuses.AdminManagerApproval,
            ReservationStatuses.Reserved,
            ReservationStatuses.Approved
        };

        private readonly DAL _context;
        private readonly IReservationEquipmentAllocationService _equipmentAllocations;

        // Retained for direct construction in integration tests and maintenance tools.
        // The application DI container uses the fuller constructor below.
        public ReservationAssignmentService(DAL context)
            : this(context, new ReservationEquipmentAllocationService(context))
        {
        }

        public ReservationAssignmentService(
            DAL context,
            IReservationEquipmentAllocationService equipmentAllocations)
        {
            _context = context;
            _equipmentAllocations = equipmentAllocations;
        }

        public static bool Overlaps(
            DateTime existingStart,
            DateTime existingEnd,
            DateTime requestedStart,
            DateTime requestedEnd) =>
            existingStart < requestedEnd && existingEnd > requestedStart;

        public static string? ValidateRoomRules(
            bool roomActive,
            bool facilityActive,
            int selectedFacilityId,
            int roomFacilityId,
            int expectedRoomType,
            int actualRoomType,
            int roomCapacity,
            int requestedOccupants,
            int overlappingOccupants,
            bool exclusiveUse)
        {
            if (!roomActive || !facilityActive)
                return "The selected room or facility is inactive.";
            if (selectedFacilityId != roomFacilityId)
                return "The selected room does not belong to the selected facility.";
            if (expectedRoomType != actualRoomType)
                return "The selected room is not valid for this reservation type.";
            if (requestedOccupants <= 0)
                return "The attendee or bed count must be greater than zero.";
            // Function Rooms are exclusive-use reservations. Their configured
            // capacity is advisory, so an approver may knowingly assign a room
            // to a larger group. Shared Guesthouse and Dorm rooms remain strict.
            if (!exclusiveUse && requestedOccupants > roomCapacity)
                return "The attendee or bed count exceeds the room capacity.";
            if ((exclusiveUse && overlappingOccupants > 0) ||
                (!exclusiveUse && overlappingOccupants + requestedOccupants > roomCapacity))
                return "Another active reservation occupies this room or time slot.";
            return null;
        }

        public static bool RowVersionMatches(byte[] current, byte[] expected) =>
            current != null && expected != null && current.SequenceEqual(expected);

        public async Task<ReservationCommitResult> ValidateAsync(
            ReservationSlotRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsRelational() &&
                _context.Database.CurrentTransaction == null)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var result = await ValidateCoreAsync(request, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }

            return await ValidateCoreAsync(request, cancellationToken);
        }

        public async Task<ReservationCommitResult> ValidateFacilityAsync(
            int facilityId,
            string reservationType,
            CancellationToken cancellationToken = default)
        {
            var roomType = ReservationRoomTypes.ForReservation(reservationType);
            if (!roomType.HasValue)
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Validation, "Unsupported reservation type.");

            var valid = await _context.Facilities.AsNoTracking().AnyAsync(f =>
                f.FacilityId == facilityId &&
                f.isActive == true &&
                f.Rooms.Any(r => r.isActive && r.RoomType == roomType.Value),
                cancellationToken);
            return valid
                ? ReservationCommitResult.Ok(new GFMS_ReservationHeader())
                : ReservationCommitResult.Fail(
                    ReservationCommitFailure.Validation,
                    "The facility is inactive or has no active room for this reservation type.");
        }

        public async Task<List<RoomAssignmentOption>> GetAvailableRoomsAsync(
            int reservationDetailId,
            string reservationType,
            CancellationToken cancellationToken = default)
        {
            var expectedRoomType = ReservationRoomTypes.ForReservation(reservationType);
            if (!expectedRoomType.HasValue) return new List<RoomAssignmentOption>();

            var detail = await _context.ReservationDetails.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ReservationDetailId == reservationDetailId,
                    cancellationToken);
            if (detail?.StartDate == null || detail.EndDate == null ||
                detail.StartDate.Value >= detail.EndDate.Value)
                return new List<RoomAssignmentOption>();

            var occupants = Math.Max(detail.NumberOfGuest, 1);
            var occupiedByRoom = await _context.ReservationDetails.AsNoTracking()
                .Where(item =>
                    item.ReservationId != detail.ReservationId &&
                    item.RoomId.HasValue &&
                    item.StartDate.HasValue &&
                    item.EndDate.HasValue &&
                    item.StartDate.Value < detail.EndDate.Value &&
                    item.EndDate.Value > detail.StartDate.Value)
                .Join(
                    _context.ReservationHeaders.AsNoTracking(),
                    item => item.ReservationId,
                    header => header.ReservationId,
                    (item, header) => new { item, header })
                .Where(joined => ActiveStatuses.Contains(joined.header.Status!))
                .GroupBy(joined => joined.item.RoomId!.Value)
                .Select(group => new
                {
                    RoomId = group.Key,
                    Occupants = group.Sum(joined =>
                        joined.item.NumberOfGuest > 0 ? joined.item.NumberOfGuest : 1)
                })
                .ToDictionaryAsync(item => item.RoomId, item => item.Occupants, cancellationToken);

            var rooms = await _context.Rooms.AsNoTracking()
                .Where(room =>
                    room.RoomType == expectedRoomType.Value &&
                    room.isActive &&
                    room.Facility.isActive == true)
                .OrderBy(room => room.Facility.FacilityName)
                .ThenBy(room => room.RoomName)
                .Select(room => new
                {
                    room.RoomId,
                    room.FacilityId,
                    FacilityName = room.Facility.FacilityName ?? "Guesthouse",
                    room.RoomName,
                    room.Capacity
                })
                .ToListAsync(cancellationToken);

            return rooms
                .Select(room =>
                {
                    var occupiedByOtherDetails = occupiedByRoom.TryGetValue(
                        room.RoomId, out var occupied) ? occupied : 0;
                    var capacityAvailableToThisReservation = room.Capacity - occupiedByOtherDetails;

                    return new
                    {
                        CapacityAvailableToThisDetail = capacityAvailableToThisReservation,
                        Option = new RoomAssignmentOption
                        {
                            RoomId = room.RoomId,
                            FacilityId = room.FacilityId,
                            FacilityName = room.FacilityName,
                            RoomName = room.RoomName,
                            Capacity = room.Capacity,
                            RemainingCapacity = capacityAvailableToThisReservation
                        }
                    };
                })
                .Where(room => room.CapacityAvailableToThisDetail >= occupants)
                .Select(room => room.Option)
                .ToList();
        }

        public async Task<List<OccupiedRoomAssignmentOption>> GetOccupiedRoomsAsync(
            int reservationDetailId,
            string reservationType,
            CancellationToken cancellationToken = default)
        {
            if (reservationType != "GH") return new List<OccupiedRoomAssignmentOption>();

            var target = await _context.ReservationDetails.AsNoTracking()
                .FirstOrDefaultAsync(detail => detail.ReservationDetailId == reservationDetailId, cancellationToken);
            if (target?.StartDate == null || target.EndDate == null) return new List<OccupiedRoomAssignmentOption>();

            var conflicts = await _context.ReservationDetails.AsNoTracking()
                .Where(detail =>
                    detail.ReservationId != target.ReservationId &&
                    detail.RoomId.HasValue &&
                    detail.RoomId != target.RoomId &&
                    detail.StartDate.HasValue && detail.EndDate.HasValue &&
                    detail.StartDate.Value < target.EndDate.Value &&
                    detail.EndDate.Value > target.StartDate.Value)
                .Join(_context.ReservationHeaders.AsNoTracking(),
                    detail => detail.ReservationId,
                    header => header.ReservationId,
                    (detail, header) => new { detail, header })
                .Join(_context.Rooms.AsNoTracking().Where(room =>
                        room.RoomType == ReservationRoomTypes.Guesthouse && room.isActive),
                    joined => joined.detail.RoomId,
                    room => room.RoomId,
                    (joined, room) => new
                    {
                        joined.detail,
                        joined.header,
                        room.RoomId,
                        room.FacilityId,
                        room.RoomName,
                        room.Capacity,
                        FacilityName = room.Facility.FacilityName ?? "Guesthouse"
                    })
                .Where(item => ActiveStatuses.Contains(item.header.Status!))
                .ToListAsync(cancellationToken);

            var requestedOccupants = Math.Max(target.NumberOfGuest, 1);
            var fullRoomIds = conflicts
                .GroupBy(item => item.RoomId)
                .Where(group => group.First().Capacity - group.Sum(item => Math.Max(item.detail.NumberOfGuest, 1)) < requestedOccupants)
                .Select(group => group.Key)
                .ToHashSet();

            return conflicts
                .Where(item => fullRoomIds.Contains(item.RoomId))
                .OrderBy(item => item.FacilityName)
                .ThenBy(item => item.RoomName)
                .ThenBy(item => item.header.ReservationCode)
                .Select(item => new OccupiedRoomAssignmentOption
                {
                    RoomId = item.RoomId,
                    FacilityId = item.FacilityId,
                    FacilityName = item.FacilityName,
                    RoomName = item.RoomName ?? "Room",
                    ConflictingReservationDetailId = item.detail.ReservationDetailId,
                    ConflictingReservationCode = item.header.ReservationCode ?? $"Reservation {item.header.ReservationId}",
                    ConflictingGuestName = item.detail.GuestName ?? "Guest",
                    ConflictingDetailRowVersion = Convert.ToBase64String(item.detail.RowVersion)
                })
                .ToList();
        }

        public async Task<ReservationCommitResult> AssignAsync(
            ReservationAssignmentCommand command,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var reservation = await _context.ReservationHeaders
                    .FirstOrDefaultAsync(h =>
                        h.ReservationId == command.Slot.ReservationId &&
                        h.ReservationType == command.Slot.ReservationType,
                        cancellationToken);
                if (reservation == null)
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.NotFound, "Reservation not found.");
                }

                var detail = await _context.ReservationDetails.FirstOrDefaultAsync(d =>
                    d.ReservationDetailId == command.Slot.ReservationDetailId &&
                    d.ReservationId == command.Slot.ReservationId,
                    cancellationToken);
                if (detail == null)
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.NotFound, "Reservation detail not found.");
                }

                await _context.Entry(reservation).ReloadAsync(cancellationToken);
                await _context.Entry(detail).ReloadAsync(cancellationToken);

                if (!string.Equals(reservation.Status, command.ExpectedStatus, StringComparison.Ordinal) ||
                    !RowVersionMatches(reservation.RowVersion, command.ExpectedHeaderRowVersion) ||
                    !RowVersionMatches(detail.RowVersion, command.ExpectedDetailRowVersion))
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The reservation changed while it was being processed. Refresh and try again.");
                }

                var validation = await ValidateCoreAsync(command.Slot, cancellationToken);
                if (!validation.Succeeded) return validation;

                detail.FacilityId = command.Slot.FacilityId;
                detail.RoomId = command.Slot.RoomId;
                detail.StartDate = command.Slot.StartDate;
                detail.EndDate = command.Slot.EndDate;
                detail.NumberOfGuest = command.Slot.Occupants;
                if (command.NextStatus != null) reservation.Status = command.NextStatus;
                if (command.ApprovedBy != null) reservation.ApprovedBy = command.ApprovedBy;

                var equipmentResult = await _equipmentAllocations.SyncAsync(
                    detail, command.ManualEquipmentIds, cancellationToken);
                if (!equipmentResult.Succeeded)
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        equipmentResult.Message ?? "The requested equipment is not available.");

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return ReservationCommitResult.Ok(reservation, detail);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "The reservation changed while it was being processed. Refresh and try again.");
            }
            catch (Exception exception) when (IsSqlConflict(exception))
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "Another request won this room or time slot. Choose another room or time.");
            }
        }

        public async Task<FunctionRoomSwapResult> SwapFunctionRoomAsync(
            FunctionRoomSwapCommand command,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var assignment = command.Assignment;
                var targetHeader = await _context.ReservationHeaders.FirstOrDefaultAsync(h =>
                    h.ReservationId == assignment.Slot.ReservationId &&
                    h.ReservationType == "FN", cancellationToken);
                var targetDetail = await _context.ReservationDetails.FirstOrDefaultAsync(d =>
                    d.ReservationDetailId == assignment.Slot.ReservationDetailId &&
                    d.ReservationId == assignment.Slot.ReservationId, cancellationToken);
                var conflictDetail = await _context.ReservationDetails.FirstOrDefaultAsync(d =>
                    d.ReservationDetailId == command.ConflictingReservationDetailId,
                    cancellationToken);
                var conflictHeader = conflictDetail == null ? null :
                    await _context.ReservationHeaders.FirstOrDefaultAsync(h =>
                        h.ReservationId == conflictDetail.ReservationId &&
                        h.ReservationType == "FN", cancellationToken);

                if (targetHeader == null || targetDetail == null || conflictHeader == null || conflictDetail == null)
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.NotFound, "One of the reservations could not be found."));

                await _context.Entry(targetHeader).ReloadAsync(cancellationToken);
                await _context.Entry(targetDetail).ReloadAsync(cancellationToken);
                await _context.Entry(conflictHeader).ReloadAsync(cancellationToken);
                await _context.Entry(conflictDetail).ReloadAsync(cancellationToken);

                if (!string.Equals(targetHeader.Status, assignment.ExpectedStatus, StringComparison.Ordinal) ||
                    !RowVersionMatches(targetHeader.RowVersion, assignment.ExpectedHeaderRowVersion) ||
                    !RowVersionMatches(targetDetail.RowVersion, assignment.ExpectedDetailRowVersion))
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The reservation changed while it was being processed. Refresh and try again."));

                if (!ActiveStatuses.Contains(conflictHeader.Status ?? string.Empty) ||
                    conflictDetail.RoomId != assignment.Slot.RoomId ||
                    conflictDetail.ReservationId == targetDetail.ReservationId ||
                    !targetDetail.RoomId.HasValue || !targetDetail.FacilityId.HasValue ||
                    !conflictDetail.StartDate.HasValue || !conflictDetail.EndDate.HasValue ||
                    !Overlaps(conflictDetail.StartDate.Value, conflictDetail.EndDate.Value,
                        assignment.Slot.StartDate, assignment.Slot.EndDate))
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The selected room is no longer eligible for a swap. Refresh and review the assignments."));

                var excludedReservations = new[] { targetDetail.ReservationId, conflictDetail.ReservationId };
                var selectedRoomValidation = await ValidateSwapDestinationAsync(
                    assignment.Slot.RoomId, assignment.Slot.FacilityId,
                    assignment.Slot.StartDate, assignment.Slot.EndDate,
                    assignment.Slot.Occupants, excludedReservations, cancellationToken);
                if (!selectedRoomValidation.Succeeded)
                    return new FunctionRoomSwapResult(selectedRoomValidation);

                var oldRoomValidation = await ValidateSwapDestinationAsync(
                    targetDetail.RoomId.Value, targetDetail.FacilityId.Value,
                    conflictDetail.StartDate.Value, conflictDetail.EndDate.Value,
                    Math.Max(conflictDetail.NumberOfGuest, 1), excludedReservations, cancellationToken);
                if (!oldRoomValidation.Succeeded)
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        oldRoomValidation.Failure,
                        "The other reservation cannot be moved into the current room: " + oldRoomValidation.Message));

                var oldRoomId = targetDetail.RoomId.Value;
                var oldFacilityId = targetDetail.FacilityId.Value;
                targetDetail.RoomId = assignment.Slot.RoomId;
                targetDetail.FacilityId = assignment.Slot.FacilityId;
                conflictDetail.RoomId = oldRoomId;
                conflictDetail.FacilityId = oldFacilityId;
                if (assignment.NextStatus != null) targetHeader.Status = assignment.NextStatus;
                if (assignment.ApprovedBy != null) targetHeader.ApprovedBy = assignment.ApprovedBy;

                var targetEquipment = await _equipmentAllocations.SyncAsync(
                    targetDetail, assignment.ManualEquipmentIds, cancellationToken);
                if (!targetEquipment.Succeeded)
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        targetEquipment.Message ?? "The requested equipment is not available."));
                var conflictEquipment = await _equipmentAllocations.SyncAsync(
                    conflictDetail, null, cancellationToken);
                if (!conflictEquipment.Succeeded)
                    return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        conflictEquipment.Message ?? "The other reservation's equipment cannot be moved."));

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return new FunctionRoomSwapResult(
                    ReservationCommitResult.Ok(targetHeader, targetDetail), conflictHeader, conflictDetail);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "A reservation changed during the swap. Refresh and try again."));
            }
            catch (Exception exception) when (IsSqlConflict(exception))
            {
                return new FunctionRoomSwapResult(ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "Another request changed one of these rooms. Refresh and try again."));
            }
        }

        public async Task<GuestRoomSwapResult> SwapGuestRoomAsync(
            GuestRoomSwapCommand command,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var assignment = command.Assignment;
                var targetHeader = await _context.ReservationHeaders.FirstOrDefaultAsync(header =>
                    header.ReservationId == assignment.Slot.ReservationId && header.ReservationType == "GH", cancellationToken);
                var targetDetail = await _context.ReservationDetails.FirstOrDefaultAsync(detail =>
                    detail.ReservationDetailId == assignment.Slot.ReservationDetailId &&
                    detail.ReservationId == assignment.Slot.ReservationId, cancellationToken);
                var conflictDetail = await _context.ReservationDetails.FirstOrDefaultAsync(detail =>
                    detail.ReservationDetailId == command.ConflictingReservationDetailId, cancellationToken);
                var conflictHeader = conflictDetail == null ? null :
                    await _context.ReservationHeaders.FirstOrDefaultAsync(header =>
                        header.ReservationId == conflictDetail.ReservationId && header.ReservationType == "GH", cancellationToken);

                if (targetHeader == null || targetDetail == null || conflictHeader == null || conflictDetail == null)
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.NotFound, "One of the guesthouse reservations could not be found."));

                await _context.Entry(targetHeader).ReloadAsync(cancellationToken);
                await _context.Entry(targetDetail).ReloadAsync(cancellationToken);
                await _context.Entry(conflictHeader).ReloadAsync(cancellationToken);
                await _context.Entry(conflictDetail).ReloadAsync(cancellationToken);

                if (!string.Equals(targetHeader.Status, assignment.ExpectedStatus, StringComparison.Ordinal) ||
                    !RowVersionMatches(targetHeader.RowVersion, assignment.ExpectedHeaderRowVersion) ||
                    !RowVersionMatches(targetDetail.RowVersion, assignment.ExpectedDetailRowVersion) ||
                    !RowVersionMatches(conflictDetail.RowVersion, command.ExpectedConflictingDetailRowVersion))
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict, "A room assignment changed. Refresh and try again."));

                if ((targetHeader.Status is not ReservationStatuses.AdminApproverApproval and not ReservationStatuses.AdminManagerApproval) ||
                    !ActiveStatuses.Contains(conflictHeader.Status ?? string.Empty) ||
                    conflictDetail.RoomId != assignment.Slot.RoomId ||
                    conflictDetail.ReservationId == targetDetail.ReservationId ||
                    !targetDetail.RoomId.HasValue || !targetDetail.FacilityId.HasValue ||
                    !targetDetail.StartDate.HasValue || !targetDetail.EndDate.HasValue ||
                    !conflictDetail.StartDate.HasValue || !conflictDetail.EndDate.HasValue ||
                    !Overlaps(conflictDetail.StartDate.Value, conflictDetail.EndDate.Value,
                        targetDetail.StartDate.Value, targetDetail.EndDate.Value))
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The selected occupied room is no longer eligible for a swap. Refresh and review the assignments."));

                var excludedDetailIds = new[] { targetDetail.ReservationDetailId, conflictDetail.ReservationDetailId };
                var newRoomValidation = await ValidateGuestSwapDestinationAsync(
                    assignment.Slot.RoomId, assignment.Slot.FacilityId,
                    targetDetail.StartDate.Value, targetDetail.EndDate.Value,
                    Math.Max(targetDetail.NumberOfGuest, 1), excludedDetailIds, cancellationToken);
                if (!newRoomValidation.Succeeded) return new GuestRoomSwapResult(newRoomValidation);

                var oldRoomValidation = await ValidateGuestSwapDestinationAsync(
                    targetDetail.RoomId.Value, targetDetail.FacilityId.Value,
                    conflictDetail.StartDate.Value, conflictDetail.EndDate.Value,
                    Math.Max(conflictDetail.NumberOfGuest, 1), excludedDetailIds, cancellationToken);
                if (!oldRoomValidation.Succeeded)
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        oldRoomValidation.Failure,
                        "The other guest cannot be moved into the current room: " + oldRoomValidation.Message));

                var oldRoomId = targetDetail.RoomId.Value;
                var oldFacilityId = targetDetail.FacilityId.Value;
                targetDetail.RoomId = assignment.Slot.RoomId;
                targetDetail.FacilityId = assignment.Slot.FacilityId;
                conflictDetail.RoomId = oldRoomId;
                conflictDetail.FacilityId = oldFacilityId;

                var targetEquipment = await _equipmentAllocations.SyncAsync(
                    targetDetail, assignment.ManualEquipmentIds, cancellationToken);
                if (!targetEquipment.Succeeded)
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        targetEquipment.Message ?? "The requested equipment is not available."));
                var conflictEquipment = await _equipmentAllocations.SyncAsync(
                    conflictDetail, null, cancellationToken);
                if (!conflictEquipment.Succeeded)
                    return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        conflictEquipment.Message ?? "The other reservation's equipment cannot be moved."));

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return new GuestRoomSwapResult(
                    ReservationCommitResult.Ok(targetHeader, targetDetail), conflictHeader, conflictDetail);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict, "A room assignment changed during the swap. Refresh and try again."));
            }
            catch (Exception exception) when (IsSqlConflict(exception))
            {
                return new GuestRoomSwapResult(ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict, "Another request changed one of these rooms. Refresh and try again."));
            }
        }

        private async Task<ReservationCommitResult> ValidateGuestSwapDestinationAsync(
            int roomId,
            int facilityId,
            DateTime startDate,
            DateTime endDate,
            int occupants,
            IReadOnlyCollection<int> excludedDetailIds,
            CancellationToken cancellationToken)
        {
            var room = await _context.Rooms.Include(item => item.Facility).AsNoTracking()
                .FirstOrDefaultAsync(item => item.RoomId == roomId, cancellationToken);
            if (room == null)
                return ReservationCommitResult.Fail(ReservationCommitFailure.NotFound, "Room not found.");

            var overlappingOccupants = await _context.ReservationDetails.AsNoTracking()
                .Where(detail => !excludedDetailIds.Contains(detail.ReservationDetailId) &&
                    detail.RoomId == roomId && detail.StartDate.HasValue && detail.EndDate.HasValue &&
                    detail.StartDate.Value < endDate && detail.EndDate.Value > startDate)
                .Join(_context.ReservationHeaders.AsNoTracking(), detail => detail.ReservationId,
                    header => header.ReservationId, (detail, header) => new { detail, header })
                .Where(item => ActiveStatuses.Contains(item.header.Status!))
                .SumAsync(item => item.detail.NumberOfGuest > 0 ? item.detail.NumberOfGuest : 1, cancellationToken);

            var failure = ValidateRoomRules(
                room.isActive, room.Facility?.isActive != false,
                facilityId, room.FacilityId,
                ReservationRoomTypes.Guesthouse, room.RoomType,
                room.Capacity, occupants, overlappingOccupants, false);
            return failure == null
                ? ReservationCommitResult.Ok(new GFMS_ReservationHeader())
                : ReservationCommitResult.Fail(
                    failure.StartsWith("Another active", StringComparison.Ordinal)
                        ? ReservationCommitFailure.Conflict
                        : ReservationCommitFailure.Validation,
                    failure);
        }

        private async Task<ReservationCommitResult> ValidateSwapDestinationAsync(
            int roomId,
            int facilityId,
            DateTime startDate,
            DateTime endDate,
            int occupants,
            IReadOnlyCollection<int> excludedReservationIds,
            CancellationToken cancellationToken)
        {
            var room = await _context.Rooms.Include(r => r.Facility).AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
            if (room == null)
                return ReservationCommitResult.Fail(ReservationCommitFailure.NotFound, "Room not found.");

            var hasThirdPartyConflict = await _context.ReservationDetails.AsNoTracking()
                .Where(d => !excludedReservationIds.Contains(d.ReservationId) &&
                    d.RoomId == roomId && d.StartDate.HasValue && d.EndDate.HasValue &&
                    d.StartDate.Value < endDate && d.EndDate.Value > startDate)
                .Join(_context.ReservationHeaders.AsNoTracking(), d => d.ReservationId,
                    h => h.ReservationId, (d, h) => h)
                .AnyAsync(h => ActiveStatuses.Contains(h.Status!), cancellationToken);

            var failure = ValidateRoomRules(room.isActive, room.Facility?.isActive != false,
                facilityId, room.FacilityId, ReservationRoomTypes.FunctionRoom, room.RoomType,
                room.Capacity, occupants, hasThirdPartyConflict ? 1 : 0, true);
            return failure == null
                ? ReservationCommitResult.Ok(new GFMS_ReservationHeader())
                : ReservationCommitResult.Fail(
                    failure.StartsWith("Another active", StringComparison.Ordinal)
                        ? ReservationCommitFailure.Conflict : ReservationCommitFailure.Validation,
                    failure);
        }

        public async Task<ReservationCommitResult> ApproveAssignedReservationAsync(
            int reservationId,
            string reservationType,
            string expectedStatus,
            byte[] expectedRowVersion,
            string nextStatus,
            string? approvedBy,
            IReadOnlyCollection<ReservationRoomAssignmentSelection>? assignments = null,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var reservation = await _context.ReservationHeaders.FirstOrDefaultAsync(h =>
                    h.ReservationId == reservationId && h.ReservationType == reservationType,
                    cancellationToken);
                if (reservation == null)
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.NotFound, "Reservation not found.");
                }

                await _context.Entry(reservation).ReloadAsync(cancellationToken);

                if (!string.Equals(reservation.Status, expectedStatus, StringComparison.Ordinal) ||
                    !RowVersionMatches(reservation.RowVersion, expectedRowVersion))
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The reservation changed while it was being processed. Refresh and try again.");
                }

                var details = await _context.ReservationDetails
                    .Where(d => d.ReservationId == reservationId)
                    .ToListAsync(cancellationToken);

                if (assignments != null)
                {
                    if (assignments.Count != details.Count ||
                        assignments.GroupBy(item => item.ReservationDetailId).Any(group => group.Count() != 1))
                    {
                        return ReservationCommitResult.Fail(
                            ReservationCommitFailure.Validation,
                            "Choose one room for every guest before approval.");
                    }

                    var selections = assignments.ToDictionary(
                        item => item.ReservationDetailId,
                        item => item.RoomId);
                    if (details.Any(detail => !selections.ContainsKey(detail.ReservationDetailId)))
                    {
                        return ReservationCommitResult.Fail(
                            ReservationCommitFailure.Validation,
                            "The submitted room assignments do not match this reservation.");
                    }

                    var selectedRoomIds = selections.Values.Distinct().ToList();
                    var selectedRooms = await _context.Rooms
                        .AsNoTracking()
                        .Where(room => selectedRoomIds.Contains(room.RoomId))
                        .ToDictionaryAsync(room => room.RoomId, cancellationToken);
                    if (selectedRooms.Count != selectedRoomIds.Count)
                    {
                        return ReservationCommitResult.Fail(
                            ReservationCommitFailure.NotFound,
                            "One of the selected rooms no longer exists.");
                    }

                    foreach (var detail in details)
                    {
                        var room = selectedRooms[selections[detail.ReservationDetailId]];
                        detail.RoomId = room.RoomId;
                        detail.FacilityId = room.FacilityId;
                    }
                }

                var assigned = details.Where(d => d.RoomId.HasValue && d.FacilityId.HasValue).ToList();
                if (assigned.Count == 0 || assigned.Count != details.Count)
                {
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.Validation,
                        "Every guest must have a room assignment before approval.");
                }

                foreach (var detail in assigned)
                {
                    if (!detail.StartDate.HasValue || !detail.EndDate.HasValue)
                    {
                        return ReservationCommitResult.Fail(
                            ReservationCommitFailure.Validation,
                            "An assigned reservation must have a valid start and end date.");
                    }

                    // Refresh equipment only when this approval is also changing room assignments.
                    // Later approval stages retain the original allocation and cost snapshot.
                    if (assignments != null)
                    {
                        var equipmentResult = await _equipmentAllocations.SyncAsync(
                            detail, null, cancellationToken);
                        if (!equipmentResult.Succeeded)
                            return ReservationCommitResult.Fail(
                                ReservationCommitFailure.Conflict,
                                equipmentResult.Message ?? "Equipment is not available for one of the room assignments.");
                    }

                }

                foreach (var group in assigned.GroupBy(detail => new
                {
                    detail.RoomId,
                    detail.FacilityId,
                    detail.StartDate,
                    detail.EndDate
                }))
                {
                    var firstDetail = group.First();
                    var slot = new ReservationSlotRequest(
                        reservationId,
                        firstDetail.ReservationDetailId,
                        group.Key.FacilityId!.Value,
                        group.Key.RoomId!.Value,
                        reservationType,
                        group.Key.StartDate!.Value,
                        group.Key.EndDate!.Value,
                        group.Sum(detail => OccupantsFor(reservationType, detail)));
                    var validation = await ValidateCoreAsync(
                        slot,
                        cancellationToken,
                        reservationId);
                    if (!validation.Succeeded) return validation;
                }

                reservation.Status = nextStatus;
                reservation.ApprovedBy = approvedBy;
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return ReservationCommitResult.Ok(reservation);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "The reservation changed while it was being processed. Refresh and try again.");
            }
            catch (Exception exception) when (IsSqlConflict(exception))
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "Another request won this room or time slot. Choose another room or time.");
            }
        }

        public async Task<ReservationCommitResult> RescheduleAsync(
            int reservationId,
            string reservationType,
            string expectedStatus,
            byte[] expectedRowVersion,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var reservation = await _context.ReservationHeaders.FirstOrDefaultAsync(h =>
                    h.ReservationId == reservationId && h.ReservationType == reservationType,
                    cancellationToken);
                if (reservation == null)
                    return ReservationCommitResult.Fail(ReservationCommitFailure.NotFound, "Reservation not found.");

                await _context.Entry(reservation).ReloadAsync(cancellationToken);
                if (!string.Equals(reservation.Status, expectedStatus, StringComparison.Ordinal) ||
                    !RowVersionMatches(reservation.RowVersion, expectedRowVersion))
                    return ReservationCommitResult.Fail(
                        ReservationCommitFailure.Conflict,
                        "The reservation changed while it was being processed. Refresh and try again.");

                var details = await _context.ReservationDetails
                    .Where(d => d.ReservationId == reservationId)
                    .ToListAsync(cancellationToken);

                foreach (var group in details
                    .Where(d => d.RoomId.HasValue && d.FacilityId.HasValue)
                    .GroupBy(d => new { d.RoomId, d.FacilityId }))
                {
                    var occupants = group.Sum(d => Math.Max(d.NumberOfGuest, 1));
                    var slot = new ReservationSlotRequest(
                        reservationId,
                        0,
                        group.Key.FacilityId!.Value,
                        group.Key.RoomId!.Value,
                        reservationType,
                        startDate,
                        endDate,
                        occupants);
                    var validation = await ValidateCoreAsync(
                        slot, cancellationToken, reservationId);
                    if (!validation.Succeeded) return validation;
                }

                foreach (var detail in details)
                {
                    detail.StartDate = startDate;
                    detail.EndDate = endDate;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return ReservationCommitResult.Ok(reservation);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "The reservation changed while it was being processed. Refresh and try again.");
            }
            catch (Exception exception) when (IsSqlConflict(exception))
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Conflict,
                    "Another request won this room or time slot. Choose another room or time.");
            }
        }

        private async Task<ReservationCommitResult> ValidateCoreAsync(
            ReservationSlotRequest request,
            CancellationToken cancellationToken,
            int? excludeReservationId = null)
        {
            if (request.StartDate < new DateTime(1753, 1, 1) ||
                request.EndDate > new DateTime(9999, 12, 31) ||
                request.StartDate >= request.EndDate)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Validation,
                    "The reservation start date must be earlier than its end date.");
            }

            if (request.Occupants <= 0)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Validation,
                    "The attendee or bed count must be greater than zero.");
            }

            var expectedRoomType = request.ExpectedRoomType ??
                ReservationRoomTypes.ForReservation(request.ReservationType);
            if (!expectedRoomType.HasValue)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.Validation, "Unsupported reservation type.");
            }

            var room = await _context.Rooms
                .Include(r => r.Facility)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == request.RoomId, cancellationToken);
            if (room == null)
            {
                return ReservationCommitResult.Fail(
                    ReservationCommitFailure.NotFound, "Room not found.");
            }

            var overlappingOccupants = await _context.ReservationDetails
                .AsNoTracking()
                .Where(d =>
                    d.ReservationDetailId != request.ReservationDetailId &&
                    (!excludeReservationId.HasValue || d.ReservationId != excludeReservationId.Value) &&
                    d.RoomId == request.RoomId &&
                    d.StartDate.HasValue &&
                    d.EndDate.HasValue &&
                    d.StartDate.Value < request.EndDate &&
                    d.EndDate.Value > request.StartDate)
                .Join(
                    _context.ReservationHeaders.AsNoTracking(),
                    detail => detail.ReservationId,
                    header => header.ReservationId,
                    (detail, header) => new { detail, header })
                .Where(x => ActiveStatuses.Contains(x.header.Status!))
                .SumAsync(x => request.ReservationType == "FN"
                    ? 1
                    : x.detail.NumberOfGuest > 0 ? x.detail.NumberOfGuest : 1,
                    cancellationToken);

            var ruleFailure = ValidateRoomRules(
                room.isActive,
                room.Facility?.isActive != false,
                request.FacilityId,
                room.FacilityId,
                expectedRoomType.Value,
                room.RoomType,
                room.Capacity,
                request.Occupants,
                overlappingOccupants,
                request.ReservationType == "FN");

            return ruleFailure != null
                ? ReservationCommitResult.Fail(
                    ruleFailure.StartsWith("Another active", StringComparison.Ordinal)
                        ? ReservationCommitFailure.Conflict
                        : ReservationCommitFailure.Validation,
                    ruleFailure)
                : ReservationCommitResult.Ok(new GFMS_ReservationHeader());
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?>
            BeginSerializableTransactionAsync(CancellationToken cancellationToken)
        {
            return _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken)
                : null;
        }

        private static int OccupantsFor(string reservationType, GFMS_ReservationDetails detail) =>
            reservationType == "FN"
                ? Math.Max(detail.NumberOfGuest, 1)
                : Math.Max(detail.NumberOfGuest, 1);

        private static bool IsSqlConflict(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is SqlException sqlException &&
                    sqlException.Number is 1205 or 2601 or 2627)
                    return true;
            }

            return false;
        }

    }
}
