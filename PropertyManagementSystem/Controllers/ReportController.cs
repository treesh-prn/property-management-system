using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using System;
using System.Threading.Tasks;

namespace PropertyManagementSystem.Controllers
{
    public class ReportController : Controller
    {
        private readonly ReportServices _reportServices;
        private readonly DAL _context;
        public ReportController(ReportServices reportServices, DAL context)
        {
            _reportServices = reportServices;
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }
        public async Task<IActionResult> ViewReport(int reservationId)
        {
            double equipmentTotalCost = 0;
            double roomTotalCost = 0;
            double subTotalCost = 0;
            double totalCost = 0;

            var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.ReservationId == reservationId);
            if (reservationHeader == null)
            {
                return NotFound("Reservation Header not found");
            }
            var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationHeader.ReservationId)
                    .ToList();

            var facilities = _context.Facilities.FirstOrDefault(x => x.FacilityId == reservationDetails.FirstOrDefault().FacilityId);
            var rooms = _context.Rooms.FirstOrDefault(x => x.RoomId == reservationDetails.FirstOrDefault().RoomId);
            var company = _context.Companies.FirstOrDefault(x => x.Id == reservationHeader.ChargeToCompany);
            var department = _context.Departments.FirstOrDefault(x => x.Id == reservationHeader.Department);

            if (reservationDetails.Any())
            {
                var startDate = reservationDetails.FirstOrDefault().StartDate;
                var endDate = reservationDetails.FirstOrDefault().EndDate;

                double? hours = null;

                if (startDate.HasValue && endDate.HasValue)
                {
                    hours = (endDate.Value - startDate.Value).TotalHours;
                }

                var equipments = reservationDetails.FirstOrDefault().EquipmentNeeded;
                List<string> equipmentNames = new List<string>();

                if (!string.IsNullOrEmpty(equipments))
                {
                    var equipmentList = equipments.Split(',')
                            .Select(e => int.Parse(e.Trim()))
                            .ToList();
                    var equipmentDetails = _context.Equipments.Where(e => equipmentList.Contains(e.Id)).ToList();

                    equipmentNames = equipmentDetails.Select(e => e.EquipmentName).ToList();

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
                    roomTotalCost += (rooms.CostPerDay ?? 0) / 2;
                }
                else
                {
                    roomTotalCost += rooms.CostPerDay ?? 0;
                }
                subTotalCost = roomTotalCost + equipmentTotalCost;
                totalCost = subTotalCost;
                var detailViewModel = new ReservationViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Company = company,
                    Department = department,
                    FacilityName = facilities?.FacilityName,
                    RoomName = rooms?.RoomName,
                    HourSpent = hours,
                    Equipments = equipmentNames,
                    EquipmentTotalCost = equipmentTotalCost,
                    RoomTotalCost = roomTotalCost,
                    SubTotalCost = subTotalCost,
                    TotalCost = totalCost
                };
                return View(detailViewModel);
            }
            return NotFound("Reservation details not found");
        }

        public ActionResult GetFunctionRoomReservation([DataSourceRequest] DataSourceRequest request)
        {
            var today = DateTime.Today;
            var targetDateTime = new DateTime(today.Year, today.Month, today.Day, 16, 0, 0);

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "FN" && x.Status == "APPROVED");

            var reservationHeaders = query.ToList();
            var reservationViewModels = reservationHeaders.Select(header =>
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList();

                return new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    ReservationCode = header.ReservationCode,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    Status = header.Status,
                    GUID = header.GUID,
                    ChargeToCompany = header.ChargeToCompany,
                    ReservationType = header.ReservationType,
                    DepartmentName = department?.DepartmentCode,
                    ReservationDetailId = details.FirstOrDefault()?.ReservationDetailId ?? 0
                };
            }).ToList();

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.Status,
                model.GUID,
                model.ChargeToCompany,
                model.ReservationType,
                model.ReservationDetailId
            });

            return Json(result);
        }
        public IActionResult GetRoomOccupancyData()
        {
            var today = DateTime.Today;

            var occupancyData = _context.Facilities
                .Where(f => f.isActive == true) 
                .Select(f => new
                {
                    FacilityName = f.FacilityName,

                    OccupiedRooms = f.Rooms.Count(room => _context.ReservationDetails
                        .Any(rd => rd.RoomId == room.RoomId &&
                                   rd.StartDate <= today && rd.EndDate >= today &&
                                   rd.ReservationHeader.Status == "APPROVED")),

                    UnoccupiedRooms = f.Rooms.Count(room => !_context.ReservationDetails
                        .Any(rd => rd.RoomId == room.RoomId &&
                                   rd.StartDate <= today && rd.EndDate >= today &&
                                   rd.ReservationHeader.Status == "APPROVED"))
                }).ToList();

            return Json(occupancyData);
        }

        //monthly charges
        public IActionResult MonthlyChargesReport()
        {
            var reportData = _reportServices.GenerateCompanyChargesReport();

            return View(reportData);
        }

    }
}
