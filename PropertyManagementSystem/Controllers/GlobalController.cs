using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ical.Net;
using Ical.Net.DataTypes;
using System.IO;
using Ical.Net.CalendarComponents;
using Ical.Net.Serialization.DataTypes;

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

        public IActionResult GetRoomsByFacility(int facilityId)
        {
            var rooms = _context.Rooms
                .Where(r => r.FacilityId == facilityId)
                .Select(r => new
                {
                    r.RoomId,
                    r.RoomName,
                    r.RoomStatus
                })
                .ToList();

            return Json(rooms);
        }

        public IActionResult GetDormersByRoom(int roomId)
        {
            var dormers = _context.ReservationDetails
                .Where(rd => rd.RoomId == roomId) // Filter by RoomId
                .Join(_context.ReservationHeaders,
                      rd => rd.ReservationId, // Join on ReservationId
                      rh => rh.ReservationId,
                      (rd, rh) => new
                      {
                          DormerName = rh.Requestor, // Fetch the Requestor from ReservationHeader
                          ContactNumber = rd.ContactNumber,
                          rd.ReservationDetailId,
                          rh.GUID,
                          rh.ReservationType
                      })
                .ToList();

            return Json(dormers);
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

        private List<DateTime> GetRecurrenceDates(GFMS_ReservationDetails reservation)
        {
            var recurrenceDates = new List<DateTime>();

            if (!reservation.StartDate.HasValue || string.IsNullOrEmpty(reservation.RecurrenceRule))
            {
                return recurrenceDates; // No recurrence if no start date or rule
            }

            DateTime startDate = reservation.StartDate.Value.Date;
            string rule = reservation.RecurrenceRule.ToUpper();
            string frequency = "";
            int count = 0;
            DateTime? until = null;

            var parts = rule.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("FREQ="))
                {
                    frequency = part.Replace("FREQ=", "");
                }
                else if (part.StartsWith("COUNT="))
                {
                    int.TryParse(part.Replace("COUNT=", ""), out count);
                }
                else if (part.StartsWith("UNTIL="))
                {
                    if (DateTime.TryParse(part.Replace("UNTIL=", ""), out DateTime dt))
                    {
                        until = dt.Date;
                    }
                }
            }

            int maxIterations = count > 0 ? count : 500; // Limit iterations if no COUNT
            DateTime current = startDate;
            DateTime today = DateTime.Today;

            // Generate recurrence dates, starting from past reservations that still occur in the future
            for (int i = 0; i < maxIterations; i++)
            {
                if (until.HasValue && current > until.Value)
                    break;

                if (current >= today) // Only add future and present occurrences
                {
                    recurrenceDates.Add(current);
                }

                switch (frequency)
                {
                    case "DAILY":
                        current = current.AddDays(1);
                        break;
                    case "WEEKLY":
                        current = current.AddDays(7);
                        break;
                    case "MONTHLY":
                        current = current.AddMonths(1);
                        break;
                    default:
                        return new List<DateTime> { startDate }; // If invalid frequency, return the single start date
                }
            }

            return recurrenceDates;
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
        private List<int> GetBookedRoomsForFacility(int facilityId, int currentReservationDetailId)
        {
            var bookedRooms = new HashSet<int>();

            // Get all reservations with this facility (excluding the current reservation)
            var otherReservations = _context.ReservationDetails
                .Where(r => r.FacilityId == facilityId && r.ReservationDetailId != currentReservationDetailId)
                .ToList();

            foreach (var reservation in otherReservations)
            {
                // Get recurrence dates for each reservation
                var recurrenceDates = GetRecurrenceDates(reservation);

                // If any recurrence date overlaps, mark the room as booked
                if (recurrenceDates.Any())
                    bookedRooms.Add(reservation.RoomId);
            }

            return bookedRooms.ToList();
        }

        [HttpGet]
        public IActionResult GetRoom(int? facilityId, int reservationDetailId, [FromQuery] List<string> recurrenceDates)
        {
            if (facilityId == null)
                return BadRequest("FacilityId is required.");

            // Parse recurrenceDates
            var parsedDates = new List<DateTime>();
            foreach (var str in recurrenceDates ?? new List<string>())
            {
                if (DateTime.TryParse(str, out DateTime parsed))
                    parsedDates.Add(parsed.Date);
            }

            // Get booked rooms for the facility
            var bookedRooms = GetBookedRoomsForFacility(facilityId.Value, reservationDetailId);

            // Fetch available rooms
            var availableRooms = _context.Rooms
                .Where(r => r.FacilityId == facilityId && !bookedRooms.Contains(r.RoomId))
                .Select(room => new { room.RoomId, room.RoomName })
                .ToList();

            return Json(availableRooms);
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
        public IActionResult GetHousing()
        {
            var facilities = _context.Facilities
                .Where(f => f.FacilityClassification == "HOUSING") // Filter only HOUSING
                .Where(f => !_context.ReservationDetails
                    .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                    .Any(joined => joined.rd.FacilityId == f.FacilityId && joined.rh.Status != "CANCELLED" && joined.rh.Status != "DISAPPROVED")) // Check if assigned to a reservation
                .Select(f => new
                {
                    FacilityId = f.FacilityId,
                    FacilityName = f.FacilityName,
                    FacilityClass = f.FacilityClassification
                })
                .OrderBy(f => f.FacilityName)
                .ToList();

            return Json(facilities);
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
                Console.WriteLine($"Room: {room.RoomName}, Assigned: {room.AssignedGuests}/{room.RoomCapacity}");
            }

            // Ensure the initially selected room is included if it's not overbooked
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

            Console.WriteLine($"Final Room Count: {rooms.Count}");

            return Json(rooms);
        }

        public IActionResult GetAvailableRooms(int? facilityId, int reservationDetailId)
        {
            var rooms = _context.Rooms
                .Where(room => room.FacilityId == facilityId && room.isActive)
                .Select(room => new
                {
                    RoomId = room.RoomId,
                    RoomName = room.RoomName,
                    RoomCapacity = room.Capacity,
                    AssignedGuests = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(joined =>
                            joined.rd.RoomId == room.RoomId &&
                            joined.rh.Status != "CANCELLED" &&
                            joined.rh.Status != "DISAPPROVED")
                        .Count()
                })
                .Where(room => room.AssignedGuests < room.RoomCapacity) // Only available rooms
                .ToList();

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
            var equipments = _context.Equipments
                .Where(eq => eq.isFacilityEquipment == false)
                .ToList();

            var eqList = new List<equipmentList>();

            foreach(var item in equipments)
            {
                bool isReserved = _context.ReservationEquipment
                    .Any(re => re.EquipmentId == item.Id &&
                    _context.ReservationDetails.Any(rd => rd.ReservationDetailId == re.ReservationDetailId
                    && rd.StartDate < endDate &&
                    rd.EndDate > startDate));

                eqList.Add(new equipmentList
                {
                    Id = item.Id,
                    EquipmentName = item.EquipmentName
                });
            }

            return Json(eqList);
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

        public IActionResult getHousingList([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.Where(f => f.FacilityClassification == "HOUSING").ToList();
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

        public IActionResult getRequestor(int facilityId)
        {
            var requestors = _context.ReservationDetails
                .Where(rd => rd.FacilityId == facilityId)
                .Join(
                    _context.ReservationHeaders,
                    rd => rd.ReservationId,
                    rh => rh.ReservationId,
                    (rd, rh) => new
                    {
                        RequestorName = rh.Requestor,
                        ContactNumber = rd.ContactNumber,
                        Company = rh.ChargeToCompany,
                        ReservationType = rh.ReservationType,
                        GUID = rh.GUID
                    }
                )
                .Distinct()
                .ToList();

            // Check if GUID is null
            foreach (var req in requestors)
            {
                Console.WriteLine($"Requestor: {req.RequestorName}, GUID: {req.GUID}");
            }

            return Json(requestors);
        }
    }
}
