using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Net.NetworkInformation;
using System;
using Microsoft.EntityFrameworkCore;

namespace PropertyManagementSystem.Controllers
{
    public class AccommodationController : Controller
    {
        private readonly DAL _context;

        public AccommodationController(DAL context)
        {
            _context = context;
        }
        public IActionResult Guesthouse()
        {
            var rooms = _context.Rooms
           .Include(r => r.Facility)
           .Where(x => x.RoomType == 0)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 0))
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

        public IActionResult ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            var reservations = (from resDetails in _context.ReservationDetails
                                join resHeader in _context.ReservationHeaders on resDetails.ReservationId equals resHeader.ReservationId
                                select new ReservationViewModel
                                {
                                    ReservationId = resDetails.ReservationId,
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
                    ReservationDetails = reservationDetails
                };

                return View(reservation);
            }
            catch (InvalidCastException ex)
            {
                // Log detailed error
                Console.WriteLine($"InvalidCastException: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, "Internal server error");
            }
            catch (Exception ex)
            {
                // Log any other exceptions
                Console.WriteLine($"Exception: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }


        public IActionResult CreateReservationDetails(int _reservationId, string[] _guests, string[] _company, string[] _relationship, DateTime _startDate, DateTime _endDate, string _purpose)
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
                if (_guests != null && _company != null && _relationship != null)
                {
                    int length = Math.Min(Math.Min(_guests.Length, _company.Length), _relationship.Length);

                    for (int i = 0; i < length; i++)
                    {
                        GFMS_ReservationDetails reservationDetails = new GFMS_ReservationDetails()
                        {
                            ReservationId = _reservationId,
                            GuestName = _guests[i],
                            Company = _company[i],
                            Relationship = _relationship[i],
                            FacilityId = 1003,
                            RoomId = 1010,
                            StartDate = _startDate,
                            EndDate = _endDate,
                            Purpose = _purpose,
                            DateAdded = DateTime.UtcNow,
                        };

                        _context.ReservationDetails.Add(reservationDetails);
                    }

                    _context.SaveChanges();
                }
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

            return RedirectToAction("Guesthouse", "Accommodation");
        }



        public ActionResult getGuestHouseReservation([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_ReservationHeader> _reservations = _context.ReservationHeaders.AsQueryable().Where(x => x.Status == 0).ToList();
            DataSourceResult result = _reservations.ToDataSourceResult(request, _reservation => new
            {
                ReservationId = _reservation.ReservationId,
                DateFiled = _reservation.DateFiled,
                Requestor = _reservation.Requestor,
                RequestorEmail = _reservation.RequestorEmail,
                GUID = _reservation.GUID,
                Status = _reservation.Status,
                DateSubmitted = _reservation.DateSubmitted,
                ReservationCode = _reservation.ReservationCode,
                Approver = _reservation.Approver,
                ApprovedDateTime = _reservation.ApprovedDateTime,
                ApproverEmail = _reservation.ApproverEmail,
                ChargeToCompany = _reservation.ChargeToCompany
            });
            return Json(result);

        }
        public IActionResult ReservationList()
        {

            return View();

        }

    }
}
