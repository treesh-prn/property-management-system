using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
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
            var _companyList = _context.Companies.Select(c => new { c.Id, Company = c.Company.ToUpper(), CompanyCode = c.CompanyCode }).AsQueryable().ToList();

            return Json(_companyList, new JsonSerializerSettings());
        }
        public string GenerateId()
        {
            //{00000000-0000-0000-0000-000000000000}
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            return unique;
        }

        public JsonResult getDepartments(int companyId)
        {
            var _deptList = _context.Departments.Where(d => d.CompanyId == companyId);

            return Json(_deptList, new JsonSerializerSettings());
        }

        public IActionResult GetAllFacility()
        {
            var facilities = _context.Facilities
                .Where(f => f.isActive == true)
                .Select(f => new
                {
                    FacilityId = f.FacilityId,
                    FacilityName = f.FacilityName,

                }).ToList();

            return Json(facilities);
        }

        public IActionResult GetAllRooms(int facilityId)
        {
            var rooms = _context.Rooms
               .Where(room => room.FacilityId == facilityId)
               .Select(r => new
               {
                   RoomId = r.RoomId,
                   RoomName = r.RoomName
               }).ToList();

            return Json(rooms);
        }

        public IActionResult GetFacility(int ReservationDetailId)
        {
            Console.WriteLine("ReservationDetailId:" + ReservationDetailId);
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == ReservationDetailId);

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
                            rd.ReservationDetailId != ReservationDetailId &&
                            ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                             (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                             (rd.StartDate >= startDate && rd.EndDate <= endDate))))
                })
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            var selectedFacility = _context.Facilities
                .Where(f => f.FacilityId == reservation.FacilityId)
                .Select(f => new { FacilityId = f.FacilityId, FacilityName = f.FacilityName })
                .FirstOrDefault();

            if (selectedFacility != null && !facilities.Any(f => f.FacilityId == selectedFacility.FacilityId))
            {
                facilities.Insert(0, selectedFacility);
            }


            return Json(facilities);
        }
        public IActionResult GetRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine("FacilityId: " + facilityId);
            Console.WriteLine("Reservation Id: " + reservationDetailId);
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

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
                       rd.ReservationDetailId != reservationDetailId && 
                       ((rd.StartDate <= startDate && rd.EndDate > startDate) ||
                        (rd.StartDate <= endDate && rd.EndDate >= endDate) ||
                        (rd.StartDate >= startDate && rd.EndDate <= endDate))))
         .Select(room => new { RoomId = room.RoomId, RoomName = room.RoomName })
         .ToList();

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

        public IActionResult GetGH(int ReservationDetailId)
        {
            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == ReservationDetailId);

            if (reservation == null)
            {
                return NotFound();
            }

            var startDate = reservation.StartDate;
            var endDate = reservation.EndDate;

            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "GUESTHOUSE" || f.FacilityId == reservation.FacilityId)
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        room.isActive == false ||  // Include inactive rooms
                        !_context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Any(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED" &&
                                ((joined.rd.StartDate <= startDate && joined.rd.EndDate > startDate) ||
                                 (joined.rd.StartDate <= endDate && joined.rd.EndDate >= endDate) ||
                                 (joined.rd.StartDate >= startDate && joined.rd.EndDate <= endDate))))
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

            // Select the current facility based on the reservation's FacilityId
            var selectedFacility = _context.Facilities
                .Where(f => f.FacilityId == reservation.FacilityId)
                .Select(f => new
                {
                    FacilityId = f.FacilityId,
                    FacilityName = f.FacilityName,
                    FacilityClass = f.FacilityClassification
                })
                .FirstOrDefault();

            // Insert the selected facility at the top if it's not already in the list
            if (selectedFacility != null && !facilities.Any(f => f.FacilityId == selectedFacility.FacilityId))
            {
                facilities.Insert(0, selectedFacility);
            }

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

        public IActionResult GetDRM()
        {
            var facilities = _context.Facilities
                .Include(f => f.Rooms) // Load rooms
                .Where(f => f.FacilityClassification == "DORM") // Filter only dorms
                .Select(f => new
                {
                    Facility = f,
                    AvailableRooms = f.Rooms.Where(room =>
                        room.isActive == true && // Ensure room is active
                        _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Count(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED") < room.Capacity) // Check if assigned reservations are less than capacity
                })
                .Where(f => f.AvailableRooms.Any()) // Ensure the facility has at least one available room
                .Select(f => new
                {
                    FacilityId = f.Facility.FacilityId,
                    FacilityName = f.Facility.FacilityName,
                    FacilityClass = f.Facility.FacilityClassification,
                    AvailableRooms = f.AvailableRooms.Select(room => new
                    {
                        RoomId = room.RoomId,
                        RoomName = room.RoomName,
                        Capacity = room.Capacity,
                        AssignedReservations = _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Count(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED"),
                        RemainingCapacity = room.Capacity - _context.ReservationDetails
                            .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                            .Count(joined =>
                                joined.rd.RoomId == room.RoomId &&
                                joined.rh.Status != "CANCELLED" &&
                                joined.rh.Status != "DISAPPROVED")
                    }).ToList()
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            return Json(facilities);
        }
        public IActionResult GetDRMRoom(int? facilityId, int reservationDetailId)
        {
            Console.WriteLine($"Facility ID: {facilityId}, ReservationDetail ID: {reservationDetailId}");

            var reservation = _context.ReservationDetails
                .FirstOrDefault(r => r.ReservationDetailId == reservationDetailId);

            if (reservation == null)
            {
                Console.WriteLine("❌ Reservation not found.");
                return NotFound();
            }

            // Get active rooms for the given facility
            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId && room.isActive)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    RoomCapacity = room.Capacity,

                    // Count assigned guests but only from the FIRST record per ReservationId
                    AssignedGuests = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(joined =>
                            joined.rd.RoomId == room.RoomId &&
                            joined.rh.Status != "CANCELLED" &&
                            joined.rh.Status != "DISAPPROVED" &&
                            _context.ReservationDetails
                                .Where(inner => inner.ReservationId == joined.rd.ReservationId)
                                .OrderBy(inner => inner.ReservationDetailId) // Ensures first record
                                .First().RoomId == room.RoomId // Only count if it's the assigned room
                        )
                        .Count()
                })
                .Where(room => room.AssignedGuests < room.RoomCapacity) // Exclude fully booked rooms
                .ToList(); // Convert to list for in-memory operations

            // 🔍 Debugging: Print room capacity status
            Console.WriteLine("Available Rooms:");
            foreach (var room in rooms)
            {
                Console.WriteLine($"✅ Room: {room.RoomName}, Assigned: {room.AssignedGuests}/{room.RoomCapacity}");
            }

            // ✅ Ensure the initially selected room is included if it's not overbooked
            var selectedRoomEntity = _context.Rooms.FirstOrDefault(room => room.RoomId == reservation.RoomId);

            if (selectedRoomEntity != null)
            {
                var assignedGuests = _context.ReservationDetails
                    .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                    .Where(joined =>
                        joined.rd.RoomId == selectedRoomEntity.RoomId &&
                        joined.rh.Status != "CANCELLED" &&
                        joined.rh.Status != "DISAPPROVED" &&
                        _context.ReservationDetails
                            .Where(inner => inner.ReservationId == joined.rd.ReservationId)
                            .OrderBy(inner => inner.ReservationDetailId)
                            .First().RoomId == selectedRoomEntity.RoomId
                    )
                    .Count();

                // Add selected room only if it has space
                if (assignedGuests < selectedRoomEntity.Capacity)
                {
                    var selectedRoom = new
                    {
                        RoomId = selectedRoomEntity.RoomId,
                        RoomName = selectedRoomEntity.RoomName,
                        RoomCapacity = selectedRoomEntity.Capacity,
                        AssignedGuests = assignedGuests
                    };

                    if (!rooms.Any(r => r.RoomId == selectedRoom.RoomId))
                    {
                        rooms.Insert(0, selectedRoom);
                    }
                }
            }

            Console.WriteLine($"🎯 Final Room Count: {rooms.Count}");

            return Json(rooms);
        }



        [HttpGet]
        public IActionResult GetEquipmentFNAssign(int facilityId, int roomId)
        {
            Console.WriteLine($"Facility ID: {facilityId}, Room ID: {roomId}");

            // Get facility-assigned equipment IDs
            var facilityEquipmentIds = _context.RoomEquipments
                .Where(e => e.RoomId == roomId)
                .Select(e => e.EquipmentId)
                .ToList();

            Console.WriteLine("Facility Equipment IDs: " + string.Join(",", facilityEquipmentIds));

            // Get all equipment assigned to this room (including facility equipment)
            var equipment = _context.Equipments
                .Where(e => (e.RoomId == roomId || facilityEquipmentIds.Contains(e.Id)) && e.isActive == true)
                .Select(e => new { e.Id, e.EquipmentName })
                .ToList();

            Console.WriteLine("Retrieved Equipment: " + string.Join(",", equipment.Select(e => e.EquipmentName)));

            return Json(equipment);
        }


        public ActionResult GetEquipments(DateTime startDate, DateTime endDate)
        {
            // Fetch all equipment data
            List<GFMS_Equipments> _Equipments = _context.Equipments.AsQueryable()
                .Where(eq => eq.isFacilityEquipment == false).ToList();
            List<equipmentList> _eqList = new List<equipmentList>();

            foreach (var item in _Equipments)
            {
                // Check if the equipment is currently reserved
                bool isReserved = _context.ReservationDetails
                    .Any(r => r.EquipmentNeeded.Contains(item.Id.ToString())
                              && r.StartDate <= DateTime.Now
                              && r.EndDate >= DateTime.Now);

                // Add equipment to the list with reservation status
                _eqList.Add(new equipmentList
                {
                    Id = item.Id,
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
                else if (reservationType == "GH")
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
        public ActionResult getDormList([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.Where(f => f.FacilityClassification == "DORM").ToList();
            DataSourceResult result = _Facilities.ToDataSourceResult(request, _Facility => new
            {
                FacilityId = _Facility.FacilityId,
                FacilityName = _Facility.FacilityName,
                FacilityClassification = _Facility.FacilityClassification,
                FacilityLocation = _Facility.FacilityLocation,
                isActive = _Facility.isActive
            });
            return Json(result);

        }
    }
}
