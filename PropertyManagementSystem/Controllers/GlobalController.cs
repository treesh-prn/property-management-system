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
            var _companyList = _context.Companies.Select(c => new { c.Id, Company = c.Company.ToUpper(), CompanyCode = c.CompanyCode}).AsQueryable().ToList();

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
        public IActionResult GetRoom(int? facilityId, int reservationId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationId == reservationId);

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

            // Add the initially selected room if it's not already in the list
            var selectedRoom = _context.Rooms
                .Where(room => room.RoomId == reservation.RoomId)
                .Select(room => new { RoomId = room.RoomId, RoomName = room.RoomName })
                .FirstOrDefault();

            if (selectedRoom != null && !rooms.Any(r => r.RoomId == selectedRoom.RoomId))
            {
                rooms.Insert(0, selectedRoom);
            }

            return Json(rooms);
        }
        public IActionResult GetGH(int reservationDetailId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "GUESTHOUSE")
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        room.isActive == false ||

                        !_context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Any(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate))) &&
                        _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Where(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                            .Select(joined => joined.rd.GuestName)
                            .Count() < room.Capacity)
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

        public IActionResult GetGHRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine(facilityId);
            Console.WriteLine(reservationDetailId);

            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    OccupiedGuestCount = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(joined =>
                            room.isActive == false ||
                            joined.rd.RoomId == room.RoomId &&
                            joined.rh.Status != "CANCELLED" &&
                            joined.rh.Status != "DISAPPROVED" &&
                            ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                             (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                             (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate)))
                        .Select(joined => joined.rd.GuestName)
                        .Count(),
                    RoomCapacity = room.Capacity
                })
                .Where(room => room.OccupiedGuestCount < room.RoomCapacity)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .ToList();

            // Add the initially selected room if it's not already in the list
            var selectedRoom = _context.Rooms
                .Where(room => room.RoomId == reservation.RoomId)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                })
                .FirstOrDefault();

            if (selectedRoom != null && !rooms.Any(r => r.RoomId == selectedRoom.RoomId))
            {
                rooms.Insert(0, selectedRoom);
            }

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
        public IActionResult CancelReservation(int ReservationId, string Remarks, string reservationType)
        {
            Console.WriteLine("RESERVATION TYPE:" + reservationType);
            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            if (reservation == null)
            {
                return NotFound();
            }
            else
            {
                if (reservationType == "FN")
                {
                    reservation.Status = "CANCELLED";
                    reservation.Remarks = Remarks;
                    _context.SaveChanges();


                    return RedirectToAction("ReservationList", "FunctionRoom");
                }
                else if(reservationType == "GH")
                {
                    reservation.Status = "CANCELLED";
                    reservation.Remarks = Remarks;
                    _context.SaveChanges();


                    return RedirectToAction("ReservationList", "Accommodation");
                }
                else
                {
                    return RedirectToAction("Index", "Home");
                }
            }
            
        }
    }
}
