using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public interface ISetupReservationGuard
    {
        int CountBlockingReservationsForFacility(int facilityId, DateTime asOf);
        int CountBlockingReservationsForRoom(int roomId, DateTime asOf);
        int CountBlockingReservationsForEquipment(int equipmentId, DateTime asOf);
    }

    public sealed class SetupReservationGuard : ISetupReservationGuard
    {
        private static readonly string[] BlockingStatuses =
        {
            ReservationStatuses.DepartmentHeadApproval,
            ReservationStatuses.AdminManagerApproval,
            ReservationStatuses.AdminApproverApproval,
            ReservationStatuses.Reserved,
            ReservationStatuses.Approved
        };

        private readonly DAL _context;

        public SetupReservationGuard(DAL context)
        {
            _context = context;
        }

        public int CountBlockingReservationsForFacility(int facilityId, DateTime asOf)
        {
            return (from detail in _context.ReservationDetails.AsNoTracking()
                    join header in _context.ReservationHeaders.AsNoTracking()
                        on detail.ReservationId equals header.ReservationId
                    where header.Status != null
                          && BlockingStatuses.Contains(header.Status)
                          && (!detail.EndDate.HasValue || detail.EndDate.Value > asOf)
                          && (detail.FacilityId == facilityId
                              || _context.Rooms.AsNoTracking().Any(room =>
                                  room.RoomId == detail.RoomId
                                  && room.FacilityId == facilityId))
                    select header.ReservationId)
                .Distinct()
                .Count();
        }

        public int CountBlockingReservationsForRoom(int roomId, DateTime asOf)
        {
            return (from detail in _context.ReservationDetails.AsNoTracking()
                    join header in _context.ReservationHeaders.AsNoTracking()
                        on detail.ReservationId equals header.ReservationId
                    where detail.RoomId == roomId
                          && header.Status != null
                          && BlockingStatuses.Contains(header.Status)
                          && (!detail.EndDate.HasValue || detail.EndDate.Value > asOf)
                    select header.ReservationId)
                .Distinct()
                .Count();
        }

        public int CountBlockingReservationsForEquipment(int equipmentId, DateTime asOf)
        {
            return (from allocation in _context.ReservationEquipments.AsNoTracking()
                    join detail in _context.ReservationDetails.AsNoTracking()
                        on allocation.ReservationDetailId equals detail.ReservationDetailId
                    join header in _context.ReservationHeaders.AsNoTracking()
                        on detail.ReservationId equals header.ReservationId
                    where allocation.EquipmentId == equipmentId
                          && allocation.AllocationStatus == EquipmentAllocationStatuses.Reserved
                          && header.Status != null
                          && BlockingStatuses.Contains(header.Status)
                          && (!detail.EndDate.HasValue || detail.EndDate.Value > asOf)
                    select header.ReservationId)
                .Distinct()
                .Count();
        }
    }
}
