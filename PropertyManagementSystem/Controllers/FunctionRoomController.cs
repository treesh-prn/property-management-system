using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Reflection.PortableExecutable;

namespace PropertyManagementSystem.Controllers
{

    public class FunctionRoomController : Controller
    {
        private readonly DAL _context;

        public FunctionRoomController(DAL context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult FunctionRoom()
        {
            var rooms = _context.Rooms
           .Include(r => r.Facility)
           .Where(x => x.RoomType == 1)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 1))
           .Select(facility => new GFMS_Facility
           {
               FacilityId = facility.FacilityId,
               FacilityName = facility.FacilityName,
           })
            .ToList();

            var newReservationCount = _context.ReservationHeaders.Count(r => r.Status == 0);
            var occupiedRoomsCount = _context.ReservationHeaders.Count(r => r.Status == 1);
            var availableRoomsCount = _context.ReservationHeaders.Count(r => r.Status == 0);

            var viewModel = new ReservationViewModel
            {
                NewReservationsCount = newReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }

        public IActionResult ViewReservationDetails(string Id)
        {
            try
            {
                if (string.IsNullOrEmpty(Id))
                {
                    return BadRequest("GUID cannot be null or empty");
                }

                Console.WriteLine($"Incoming GUID: {Id}");

                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                Console.WriteLine($"ReservationId: {reservationHeader.ReservationId}");
                Console.WriteLine($"Type of reservationHeader.ReservationId: {reservationHeader.ReservationId.GetType()}");

                if (reservationHeader.ReservationId.GetType() != typeof(int))
                {
                    throw new InvalidCastException("ReservationId is not of type int.");
                }

                var reservationDetails = _context.ReservationDetails
                    .AsQueryable()
                    .Where(x => x.ReservationId == reservationHeader.ReservationId)
                    .ToList();

                foreach (var detail in reservationDetails)
                {
                    Console.WriteLine($"Detail - ReservationDetailId: {detail.ReservationDetailId}, Type: {detail.ReservationDetailId.GetType()}");
                    Console.WriteLine($"Detail - ReservationId: {detail.ReservationId}, Type: {detail.ReservationId.GetType()}");
                }

                var detailViewModel = new ReservationViewModel
                {
                   ReservationHeader = reservationHeader,
                   ReservationDetails = reservationDetails
                };

                return View(detailViewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        public IActionResult ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            var reservations = (from resDetails in _context.ReservationDetails
                                join resHeader in _context.ReservationHeaders on resDetails.ReservationId equals resHeader.ReservationId
                                select new ReservationViewModel
                                {
                                    ReservationId = resDetails.ReservationId,
                                    GUID = resHeader.GUID,
                                    Requestor = resHeader.Requestor,
                                    ChargeToCompany = resHeader.ChargeToCompany,
                                    Status = resHeader.Status,
                                    Start = DateTime.SpecifyKind(resDetails.StartDate.Value, DateTimeKind.Utc),
                                    End = DateTime.SpecifyKind(resDetails.EndDate.Value, DateTimeKind.Utc),
                                    Title = resDetails.Purpose,
                                    RoomId = resDetails.RoomId,
                                    FacilityId = resDetails.FacilityId
                                }).ToList();

            var groupedReservations = reservations.GroupBy(r => r.ReservationId)
                                                  .Select(group => new ReservationViewModel
                                                  {
                                                      ReservationId = group.Key,
                                                      GUID = group.First().GUID,
                                                      Requestor = group.First().Requestor,
                                                      ChargeToCompany = group.First().ChargeToCompany,
                                                      Status = group.First().Status,
                                                      Start = group.First().Start,
                                                      End = group.First().End,
                                                      Title = group.First().Title,
                                                      RoomId = group.First().RoomId,
                                                      FacilityId = group.First().FacilityId
                                                  }).ToList();

            return Json(groupedReservations.ToDataSourceResult(request));
        }

        public IActionResult GetFacility()
        {
            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClass = c.FacilityClassification })
                .Where(p => p.FacilityClass == "FUNCTION ROOM")
                .OrderBy(o => o.FacilityName).ToList());
        }

        public IActionResult GetRoom(int? facilityId)
        {
            var rooms = _context.Rooms.AsQueryable();
            if (facilityId.HasValue)
            {
                rooms = rooms.Where(p => p.FacilityId == facilityId.Value);
            }
            return Json(rooms.Select(p => new { RoomId = p.RoomId, RoomName = p.RoomName }).ToList());
        }

        [HttpPost]
        public IActionResult CreateReservation(int _company, string _requestor, string _requestorEmail)
        {
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";

            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader()
            {
                GUID = unique,
                ChargeToCompany = _company,
                Requestor = _requestor,
                RequestorEmail = _requestorEmail,
                Approver = "Jerwin Alimuin",
                ApproverEmail = "jerwin_alimuin@tecoindustrialpark.com",
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = 0

            };

            _context.ReservationHeaders.Add(_reservation);
            _context.SaveChanges();

            return RedirectToAction("ReservationDetails", new { Id = unique });

        }
        public IActionResult ReservationDetails(string Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return BadRequest("GUID cannot be null or empty");
            }

            try
            {
                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                int reservationId = reservationHeader.ReservationId;

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();

                var reservation = new ReservationDetailViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    NumberOfGuest = reservationDetails.Sum(rd => rd.NumberOfGuest)
                };

                return View(reservation);
            }
            catch (InvalidCastException ex)
            {

                Console.WriteLine($"InvalidCastException: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return RedirectToAction("Error", "Error");
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Exception: {ex.Message}");
                return RedirectToAction("Error", "Error");
            }
        }

        public IActionResult CreateReservationDetails(int _reservationId, int _numOfGuests, int _facilityId, int _roomId, DateTime _startDate, DateTime _endDate, string _purpose)
        {
            Console.WriteLine($"Start Date: {_startDate}");
            Console.WriteLine($"End Date: {_endDate}");

            if (_startDate < new DateTime(1753, 1, 1) || _startDate > new DateTime(9999, 12, 31) ||
                _endDate < new DateTime(1753, 1, 1) || _endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            try
            {
                GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                {
                    ReservationId = _reservationId,
                    NumberOfGuest = _numOfGuests,
                    FacilityId = _facilityId,
                    RoomId = _roomId,
                    StartDate = _startDate,
                    EndDate = _endDate,
                    Purpose = _purpose,
                    DateAdded = DateTime.UtcNow,
                };

                _context.ReservationDetails.Add(reservationDetails);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while saving changes: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }

            return RedirectToAction("FunctionRoom", "FunctionRoom");
        }



    }
}
