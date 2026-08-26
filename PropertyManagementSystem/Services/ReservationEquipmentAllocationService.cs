using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public sealed record EquipmentAllocationResult(bool Succeeded, string? Message = null)
    {
        public static EquipmentAllocationResult Ok() => new(true);
        public static EquipmentAllocationResult Fail(string message) => new(false, message);
    }

    public interface IReservationEquipmentAllocationService
    {
        Task<EquipmentAllocationResult> SyncAsync(
            GFMS_ReservationDetails detail,
            IEnumerable<int>? manualEquipmentIds = null,
            CancellationToken cancellationToken = default);
        Task ReleaseReservationAsync(int reservationId, CancellationToken cancellationToken = default);
        List<ReservationEquipmentViewModel> GetForReservation(int reservationId);
    }

    public sealed class ReservationEquipmentAllocationService : IReservationEquipmentAllocationService
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
        public ReservationEquipmentAllocationService(DAL context) => _context = context;

        public async Task<EquipmentAllocationResult> SyncAsync(
            GFMS_ReservationDetails detail,
            IEnumerable<int>? manualEquipmentIds = null,
            CancellationToken cancellationToken = default)
        {
            if (detail.ReservationDetailId <= 0)
                return EquipmentAllocationResult.Fail("Save the reservation details before assigning equipment.");

            var existing = await _context.ReservationEquipments
                .Where(item => item.ReservationDetailId == detail.ReservationDetailId)
                .ToListAsync(cancellationToken);
            var manualIds = manualEquipmentIds == null
                ? existing.Where(item => !item.IsAutomatic).Select(item => item.EquipmentId).ToHashSet()
                : manualEquipmentIds.Where(id => id > 0).ToHashSet();

            var includedLinks = detail.FacilityId.HasValue && detail.RoomId.HasValue
                ? await _context.FacilityEquipmentLinks.AsNoTracking()
                    .Where(link => link.IsActive && link.LinkType == EquipmentLinkTypes.Included &&
                        link.FacilityId == detail.FacilityId.Value &&
                        (!link.RoomId.HasValue || link.RoomId == detail.RoomId.Value))
                    .OrderBy(link => link.RoomId.HasValue ? 0 : 1)
                    .ToListAsync(cancellationToken)
                : new List<GFMS_FacilityEquipmentLink>();

            var automatic = includedLinks
                .GroupBy(link => link.EquipmentId)
                .ToDictionary(group => group.Key, group => group.First());
            manualIds.ExceptWith(automatic.Keys);
            var desiredIds = automatic.Keys.Concat(manualIds).Distinct().ToList();
            var equipment = await _context.Equipments.AsNoTracking()
                .Where(item => desiredIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (equipment.Count != desiredIds.Count)
                return EquipmentAllocationResult.Fail("One or more selected equipment records no longer exist.");

            foreach (var id in desiredIds)
            {
                var item = equipment[id];
                if (item.isActive == false)
                    return EquipmentAllocationResult.Fail($"{item.EquipmentName} is inactive and cannot be assigned.");
                var quantity = automatic.TryGetValue(id, out var link) ? link.DefaultQuantity : 1;
                if (quantity < 1 || quantity > Math.Max(item.EquipmentQty ?? 0, 0))
                    return EquipmentAllocationResult.Fail($"There is not enough {item.EquipmentName} inventory for this reservation.");

                var isRoomFixture = link?.IsFixed == true && link.RoomId.HasValue;
                if (!isRoomFixture && detail.StartDate.HasValue && detail.EndDate.HasValue)
                {
                    var overlappingAllocations = await _context.ReservationEquipments.AsNoTracking()
                        .Where(allocation => allocation.EquipmentId == id &&
                            allocation.ReservationDetailId != detail.ReservationDetailId &&
                            allocation.AllocationStatus == EquipmentAllocationStatuses.Reserved)
                        .Join(_context.ReservationDetails.AsNoTracking(), allocation => allocation.ReservationDetailId,
                            reservationDetail => reservationDetail.ReservationDetailId,
                            (allocation, reservationDetail) => new { allocation, reservationDetail })
                        .Join(_context.ReservationHeaders.AsNoTracking(), joined => joined.reservationDetail.ReservationId,
                            header => header.ReservationId, (joined, header) => new { joined, header })
                        .Where(joined => joined.header.ReservationId != detail.ReservationId &&
                            ActiveStatuses.Contains(joined.header.Status!) &&
                            joined.joined.reservationDetail.StartDate < detail.EndDate &&
                            joined.joined.reservationDetail.EndDate > detail.StartDate)
                        .Select(joined => new
                        {
                            joined.header.ReservationId,
                            joined.joined.allocation.Quantity
                        })
                        .ToListAsync(cancellationToken);
                    // A guesthouse reservation can contain several guest detail rows that share
                    // one facility-level allocation. Count that equipment once per reservation.
                    var allocated = overlappingAllocations
                        .GroupBy(item => item.ReservationId)
                        .Sum(group => group.Max(item => item.Quantity));
                    if (allocated + quantity > Math.Max(item.EquipmentQty ?? 0, 0))
                        return EquipmentAllocationResult.Fail($"{item.EquipmentName} is fully allocated for the selected schedule.");
                }
            }

            _context.ReservationEquipments.RemoveRange(existing.Where(item => !desiredIds.Contains(item.EquipmentId)));
            foreach (var id in desiredIds)
            {
                var item = equipment[id];
                var allocation = existing.FirstOrDefault(current => current.EquipmentId == id)
                    ?? new GFMS_ReservationEquipment
                    {
                        ReservationDetailId = detail.ReservationDetailId,
                        EquipmentId = id,
                        DateAssigned = DateTime.UtcNow
                    };
                if (allocation.ReservationEquipmentId == 0)
                    _context.ReservationEquipments.Add(allocation);

                var isAutomatic = automatic.TryGetValue(id, out var link);
                allocation.Quantity = isAutomatic ? link!.DefaultQuantity : 1;
                allocation.SourceType = isAutomatic
                    ? link!.RoomId.HasValue ? EquipmentAllocationSources.Room : EquipmentAllocationSources.Facility
                    : EquipmentAllocationSources.Manual;
                allocation.SourceFacilityId = isAutomatic ? link!.FacilityId : null;
                allocation.SourceRoomId = isAutomatic ? link!.RoomId : null;
                allocation.IsAutomatic = isAutomatic;
                allocation.AllocationStatus = EquipmentAllocationStatuses.Reserved;
                allocation.EquipmentNameSnapshot = item.EquipmentName ?? $"Equipment {id}";
                allocation.AssignedCost = GetAssignedCost(item, detail);
            }
            return EquipmentAllocationResult.Ok();
        }

        public async Task ReleaseReservationAsync(int reservationId, CancellationToken cancellationToken = default)
        {
            var detailIds = await _context.ReservationDetails.AsNoTracking()
                .Where(detail => detail.ReservationId == reservationId)
                .Select(detail => detail.ReservationDetailId)
                .ToListAsync(cancellationToken);
            var allocations = await _context.ReservationEquipments
                .Where(item => detailIds.Contains(item.ReservationDetailId))
                .ToListAsync(cancellationToken);
            foreach (var allocation in allocations)
                allocation.AllocationStatus = EquipmentAllocationStatuses.Released;
        }

        public List<ReservationEquipmentViewModel> GetForReservation(int reservationId) =>
            _context.ReservationEquipments.AsNoTracking()
                .Where(allocation => _context.ReservationDetails.Any(detail =>
                    detail.ReservationId == reservationId &&
                    detail.ReservationDetailId == allocation.ReservationDetailId))
                .GroupBy(allocation => new
                {
                    allocation.EquipmentId,
                    allocation.EquipmentNameSnapshot,
                    allocation.SourceType,
                    allocation.IsAutomatic
                })
                .Select(group => new ReservationEquipmentViewModel
                {
                    EquipmentId = group.Key.EquipmentId,
                    EquipmentName = group.Key.EquipmentNameSnapshot,
                    Quantity = group.Max(item => item.Quantity),
                    SourceType = group.Key.SourceType,
                    IsAutomatic = group.Key.IsAutomatic
                })
                .OrderBy(item => item.IsAutomatic ? 0 : 1)
                .ThenBy(item => item.EquipmentName)
                .ToList();

        private static decimal GetAssignedCost(GFMS_Equipments equipment, GFMS_ReservationDetails detail)
        {
            var hours = detail.StartDate.HasValue && detail.EndDate.HasValue
                ? (detail.EndDate.Value - detail.StartDate.Value).TotalHours
                : 0;
            return Convert.ToDecimal(hours > 4 ? equipment.WholeDayCost ?? 0 : equipment.HalfDayCost ?? 0);
        }
    }
}
