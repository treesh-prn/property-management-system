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
        public IActionResult GetFacility()
        {
            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClassification = c.FacilityClassification })
                .Where(p => p.FacilityClassification == "GUESTHOUSE")
                .OrderBy(o => o.FacilityName).ToList());
        }
        public IActionResult Index()
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

            var newReservationCount = _context.ReservationHeaders.Count(r => r.Status == "PENDING" || r.Status == "DRAFT");
            var occupiedRoomsCount = _context.ReservationHeaders.Count(r => r.Status == "APPROVED");
            var availableRoomsCount = _context.ReservationHeaders.Count(r => r.Status == "PENDING");


            var viewModel = new ReservationViewModel
            {
                NewReservationsCount = newReservationCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }
        public IActionResult Guesthouse()
        {
            return View();
        }

        public IActionResult ReservationList()
        {

            return View();

        }
        public IActionResult ViewReservationDetails(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    return BadRequest("GUID cannot be null or empty");
                }

                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationHeader.ReservationId)
                    .ToList();

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
                                select new
                                {
                                    resDetails.ReservationId,
                                    resHeader.GUID,
                                    resHeader.Requestor,
                                    resHeader.ChargeToCompany,
                                    resHeader.Status,
                                    StartDate = resDetails.StartDate,
                                    EndDate = resDetails.EndDate,
                                    resDetails.Purpose,
                                    resDetails.RoomId,
                                    resDetails.FacilityId
                                }).AsEnumerable()
                                  .Select(r => new ReservationViewModel
                                  {
                                      ReservationId = r.ReservationId,
                                      GUID = r.GUID,
                                      Requestor = r.Requestor,
                                      ChargeToCompany = r.ChargeToCompany,
                                      Status = r.Status,
                                      Start = r.StartDate.HasValue && DateTime.TryParse(r.StartDate.ToString(), out DateTime startDate)
                                              ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc)
                                              : DateTime.MinValue,
                                      End = r.EndDate.HasValue && DateTime.TryParse(r.EndDate.ToString(), out DateTime endDate)
                                            ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
                                            : DateTime.MinValue,
                                      Title = r.Purpose,
                                      RoomId = r.RoomId,
                                      FacilityId = r.FacilityId
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
                Status = "DRAFT",
                ReservationType = "GH",

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
                Console.WriteLine($"InvalidCastException: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, "Internal server error");
            }
            catch (Exception ex)
            {
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

            return RedirectToAction("Index", "Accommodation");
        }



        public ActionResult getGuestHouseReservation([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_ReservationHeader> _reservations = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.Status == "PENDING" || x.Status == "DRAFT")
                .Where(X => X.ReservationType == "GH")
                .ToList();

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
                ChargeToCompany = _reservation.ChargeToCompany,
                ReservationType = _reservation.ReservationType
            }) ;
            return Json(result);

        }

        /*Update*/
        [HttpPost]
        public IActionResult UpdateReservationDetails(int reservationId, List<string> _guests, List<string> _company, List<string> _relationship, DateTime startDate, DateTime endDate, string purpose)
        {
            if (startDate < new DateTime(1753, 1, 1) || startDate > new DateTime(9999, 12, 31) ||
                endDate < new DateTime(1753, 1, 1) || endDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }
            Console.WriteLine("Reservation Id: " + reservationId);
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == reservationId)
                .ToList();

            for (int i = 0; i < existingReservationDetails.Count; i++)
            {
                var existingDetail = existingReservationDetails[i];


                var index = _guests.FindIndex(g => string.Equals(g, existingDetail.GuestName, StringComparison.OrdinalIgnoreCase));


                if (index != -1)
                {
                    existingDetail.GuestName = _guests[index];
                    existingDetail.Company = _company[index];
                    existingDetail.Relationship = _relationship[index];
                    existingDetail.StartDate = startDate;
                    existingDetail.EndDate = endDate;
                    existingDetail.Purpose = purpose;


                    _context.ReservationDetails.Update(existingDetail);
                }
                else
                {
                    _context.ReservationDetails.Remove(existingDetail);
                }
            }


            for (int i = 0; i < _guests.Count; i++)
            {
                var existingDetail = existingReservationDetails.FirstOrDefault(rd => string.Equals(rd.GuestName, _guests[i], StringComparison.OrdinalIgnoreCase));
                if (existingDetail == null)
                {
                    GFMS_ReservationDetails newDetail = new GFMS_ReservationDetails
                    {
                        ReservationId = reservationId,
                        GuestName = _guests[i],
                        Company = _company[i],
                        Relationship = _relationship[i],
                        StartDate = startDate,
                        EndDate = endDate,
                        Purpose = purpose
                    };

                    _context.ReservationDetails.Add(newDetail);
                }
            }
            _context.SaveChanges();
            return RedirectToAction("Index", "Accommodation");
        }

        /*Delete*/
        [HttpPost]
        public IActionResult DeleteReservationDetail(int ReservationId)
        {
            try
            {
                Console.WriteLine(ReservationId);
                var reservationDetail = _context.ReservationDetails
                    .Include(rd => rd.ReservationHeader)
                    .Where(rd => rd.ReservationId == ReservationId).ToList();

                if (reservationDetail == null)
                {
                    return NotFound("Reservation detail not found.");
                }


                _context.ReservationDetails.RemoveRange(reservationDetail);


                var reservationHeader = reservationDetail.First().ReservationHeader;
                if (reservationHeader != null)
                {
                    _context.ReservationHeaders.Remove(reservationHeader);
                }

                _context.SaveChanges();

                return RedirectToAction("Index", "Accommodation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

    }
}
