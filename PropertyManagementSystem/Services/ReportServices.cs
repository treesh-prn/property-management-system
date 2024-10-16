using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public class ReportServices
    {
        private readonly DAL _context;

        public ReportServices(DAL context)
        {
            _context = context;
        }

        public ReservationReport CalculateReservationCosts(int reservationId)
        {
            var reservationHeader = _context.ReservationHeaders
                .FirstOrDefault(x => x.ReservationId == reservationId);

            if (reservationHeader == null)
            {
                return null;
            }

            var companyName = _context.Companies
                .Where(c => c.Id == reservationHeader.ChargeToCompany)
                .Select(c => c.Company) 
                .FirstOrDefault();

            var reservationDetails = _context.ReservationDetails
                .Where(x => x.ReservationId == reservationId)
                .ToList();

            if (!reservationDetails.Any())
            {
                return null;
            }

            var facility = _context.Facilities
                .FirstOrDefault(x => x.FacilityId == reservationDetails.First().FacilityId);
            var room = _context.Rooms
                .FirstOrDefault(x => x.RoomId == reservationDetails.First().RoomId);

            double equipmentTotalCost = 0;
            double roomTotalCost = 0;
            double subTotalCost = 0;
            double totalCost = 0;
            double? hours = null;

            foreach (var detail in reservationDetails)
            {
                if (detail.StartDate.HasValue && detail.EndDate.HasValue)
                {
                    hours = (detail.EndDate.Value - detail.StartDate.Value).TotalHours;
                }

                var equipmentIds = detail.EquipmentNeeded?.Split(',')
                    .Select(e => int.Parse(e.Trim()))
                    .ToList();

                if (equipmentIds != null)
                {
                    var equipmentDetails = _context.Equipments
                        .Where(e => equipmentIds.Contains(e.Id))
                        .ToList();

                    foreach (var equipment in equipmentDetails)
                    {
                        if (hours <= 4)
                        {
                            equipmentTotalCost += equipment.HalfDayCost ?? 0;
                        }
                        else
                        {
                            equipmentTotalCost += equipment.WholeDayCost ?? 0;
                        }
                    }
                }

                if (hours <= 4)
                {
                    roomTotalCost += (room?.CostPerDay ?? 0) / 2;
                }
                else
                {
                    roomTotalCost += room?.CostPerDay ?? 0;
                }
            }

            subTotalCost = roomTotalCost + equipmentTotalCost;
            totalCost = subTotalCost;

            return new ReservationReport
            {
                CompanyName = companyName,
                EquipmentTotalCost = equipmentTotalCost,
                RoomTotalCost = roomTotalCost,
                SubTotalCost = subTotalCost,
                TotalCost = totalCost,
                Hours = hours
            };
        }

        public IEnumerable<ReservationReport> GenerateCompanyChargesReport()
        {
            // Fetch reservation headers and details
            var reservationHeaders = _context.ReservationHeaders.Where(rh => rh.Status == "APPROVED").ToList();
            var reservationDetails = _context.ReservationDetails
            .Include(rd => rd.Room)
            .Include(rd => rd.Facility)
            .Where(rd => rd.Facility.FacilityClassification == "FUNCTION ROOM" && rd.ReservationHeader.ChargeToCompany != null) // Filter by facility classification and status
            .ToList();
            var equipmentDetails = _context.Equipments.ToList();

            // Group reservation details by company
            var reportData = reservationHeaders
                .GroupBy(rh => rh.ChargeToCompany)
                .Select(group =>
                {
                    var companyId = group.Key;
                    var company = _context.Companies.FirstOrDefault(c => c.Id == companyId);
                    var companyName = company?.Company ?? "Unknown Company";

                    var reservationIds = group.SelectMany(rh => reservationDetails
                        .Where(rd => rd.ReservationId == rh.ReservationId)
                        .Select(rd => rd.ReservationId)).Distinct();

                    double equipmentTotalCost = 0;
                    double roomTotalCost = 0;

                    foreach (var reservationId in reservationIds)
                    {
                        var details = reservationDetails
                            .Where(rd => rd.ReservationId == reservationId)
                            .ToList();

                        foreach (var detail in details)
                        {
                            if (!detail.StartDate.HasValue || !detail.EndDate.HasValue)
                                continue;

                            var hours = (detail.EndDate.Value - detail.StartDate.Value).TotalHours;

                            var equipmentIds = detail.EquipmentNeeded?
                                .Split(',')
                                .Select(id => int.TryParse(id.Trim(), out var parsedId) ? parsedId : (int?)null)
                                .Where(id => id.HasValue)
                                .Select(id => id.Value)
                                .ToList();

                            if (equipmentIds != null)
                            {
                                var equipmentCosts = equipmentDetails
                                    .Where(e => equipmentIds.Contains(e.Id))
                                    .Sum(e => hours <= 4 ? e.HalfDayCost ?? 0 : e.WholeDayCost ?? 0);

                                equipmentTotalCost += equipmentCosts;
                            }

                            var roomCost = detail.Room?.CostPerDay ?? 0;
                            roomTotalCost += hours <= 4 ? roomCost / 2 : roomCost;
                        }
                    }

                    var subTotalCost = roomTotalCost + equipmentTotalCost;

                    return new ReservationReport
                    {
                        CompanyName = companyName,
                        EquipmentTotalCost = equipmentTotalCost,
                        RoomTotalCost = roomTotalCost,
                        SubTotalCost = subTotalCost,
                        TotalCost = subTotalCost // Assuming TotalCost equals SubTotalCost
                    };
                })
                .ToList();

            return reportData;
        }

        private double CalculateEquipmentTotalCost(List<int> equipmentIds, DateTime? startDate, DateTime? endDate, List<GFMS_Equipments> equipmentDetails)
        {
            if (equipmentIds == null || !startDate.HasValue || !endDate.HasValue)
                return 0;

            var totalHours = (endDate.Value - startDate.Value).TotalHours;
            return equipmentDetails
                .Where(e => equipmentIds.Contains(e.Id))
                .Sum(e => totalHours <= 4 ? e.HalfDayCost ?? 0 : e.WholeDayCost ?? 0);
        }

        private double CalculateRoomTotalCost(double? roomCost, DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue || !endDate.HasValue || roomCost == null)
                return 0;

            var totalHours = (endDate.Value - startDate.Value).TotalHours;
            return totalHours <= 4 ? roomCost.Value / 2 : roomCost.Value;
        }

        private double CalculateSubTotalCost(double? roomCost, List<int> equipmentIds, DateTime? startDate, DateTime? endDate, List<GFMS_Equipments> equipmentDetails)
        {
            var roomTotalCost = CalculateRoomTotalCost(roomCost, startDate, endDate);
            var equipmentTotalCost = CalculateEquipmentTotalCost(equipmentIds, startDate, endDate, equipmentDetails);
            return roomTotalCost + equipmentTotalCost;
        }

    }

}
