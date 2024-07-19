using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.IO;
using System.Security.Claims;

namespace PropertyManagementSystem.Controllers
{
    public class GlobalController : Controller
    {
        private readonly DAL _context;

        public GlobalController(DAL context)
        {

            _context = context;

        }
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult getCompanies()
        {
            var _companyList = _context.Companies.AsQueryable().ToList();

            return Json(_companyList , new JsonSerializerSettings());
        }
        public string GenerateId()
        {
            //{00000000-0000-0000-0000-000000000000}
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            return unique;
        }

        public JsonResult getDepartments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var _deptList = _context.Departments.AsQueryable().ToList();

            return Json(_deptList , new JsonSerializerSettings());
        }

        public IActionResult GetFacility(int ReservationId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationId == ReservationId);

            if (reservation == null)
            {
                return NotFound();
            }

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "FUNCTION ROOM")
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        !_context.ReservationDetails.Any(rd =>
                            rd.RoomId == room.RoomId &&
                            ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                             (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                             (rd.StartDate >= startDate && rd.EndDate <= endDate))))
                })
                .Where(f => f.AvailableRooms.Any())
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName,
                    FacilityClass = f.Facility.FacilityClassification
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            return Json(facilities);
        }


        public IActionResult GetRoom(int? facilityId, int ReservationId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationId == ReservationId);

            if (reservation == null)
            {
                return NotFound();
            }

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId &&
                               !_context.ReservationDetails.Any(rd =>
                                   rd.RoomId == room.RoomId &&
                                   ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                                    (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                                    (rd.StartDate >= startDate && rd.EndDate <= endDate))))
                .Select(room => new { RoomId = room.RoomId, RoomName = room.RoomName })
                .ToList();

            return Json(rooms);
        }
        public ActionResult GetEquipments()
        {
            List<GFMS_Equipments> _Equipments = _context.Equipments.AsQueryable().ToList();
            List<equipmentList> _eqList = new List<equipmentList>();
            foreach(var item in _Equipments)
            {
                _eqList.Add(new equipmentList
                {
                    EquipmentId = item.EquipmentId,
                    EquipmentName = item.EquipmentName,
                });
            }
            return Json(_eqList);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CancelReservation(int ReservationId, string Remarks)
        {
            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            if (reservation == null)
            {
                return NotFound();
            }
            else
            {
                reservation.Status = "CANCELLED";
                reservation.Remarks = Remarks;
                _context.SaveChanges();
            }

            return RedirectToAction("ReservationList","FunctionRoom");
        }
    }
}
