using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using System.Reflection.PortableExecutable;

namespace PropertyManagementSystem.Controllers
{

    public class FunctionRoomController : Controller
    {
        private readonly DAL _context;
        private readonly IRoleService _roleService;

        public FunctionRoomController(DAL context, IRoleService roleService)
        {
            _context = context;
            _roleService = roleService;
        }
        public IActionResult Index()
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

        public IActionResult FunctionRoom()
        {
            return View();
        }
        public IActionResult ReservationList()
        {

            return View();

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
        public ActionResult GetFunctionRoomReservation([DataSourceRequest] DataSourceRequest request, string gridType)
        {
            Task<Gen_Employee> employeeTask = _roleService.GetUserName(User);
            var employee = employeeTask.Result;

            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.Status == "PENDING" || x.Status == "DRAFT")
                .Where(x => x.ReservationType == "FN");

            if (gridType == "myReservation")
            {
                query = query.Where(x => x.Requestor == employee.Fullname);
            }
            else if (gridType == "reservationApproval")
            {
                if (User.IsInRole("GFMS.Department Head"))
                {
                    query = query.Where(x => x.Approver == employee.Fullname);
                }
            }

            var reservationHeaders = query.ToList();
            var reservationViewModels = new List<ReservationViewModel>();

            foreach (var header in reservationHeaders)
            {
                var department = _context.Departments.FirstOrDefault(d => d.Id == header.Department);
                var details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList();

                var viewModel = new ReservationViewModel
                {
                    ReservationId = header.ReservationId,
                    Requestor = header.Requestor,
                    RequestorEmail = header.RequestorEmail,
                    Status = header.Status,
                    GUID = header.GUID,
                    ChargeToCompany = header.ChargeToCompany,
                    ReservationType = header.ReservationType,
                    DepartmentName = department?.DepartmentName,
                    ReservationDetailId = details.FirstOrDefault()?.ReservationDetailId ?? 0
                };

                reservationViewModels.Add(viewModel);
            }

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
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

        [HttpPost]
        public IActionResult CreateReservation(int _company, string _requestor, string _requestorEmail, int _department)
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
                Department = _department,
                ReservationType = "FN"

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

        public IActionResult CreateReservationDetails(int _reservationId, int _numOfGuests, int _facilityId, int _roomId, DateTime _startDate, DateTime _endDate, string _purpose, string _equipment)
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
                var equipmentNeeded = _equipment?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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
                    EquipmentNeeded = string.Join(",", equipmentNeeded.Select(e => e.Trim()))
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

            return RedirectToAction("Index", "FunctionRoom");
        }

        /*EditReservation*/

        [HttpPost]
        public IActionResult EditReservationDetails(int ReservationDetailId, int ReservationId, int NumberOfGuest, int _facilityId, int _roomId, DateTime StartDate, DateTime EndDate, string Purpose, string[] EquipmentNeeded)
        {
            if (StartDate < new DateTime(1753, 1, 1) || StartDate > new DateTime(9999, 12, 31) ||
                EndDate < new DateTime(1753, 1, 1) || EndDate > new DateTime(9999, 12, 31))
            {
                throw new ArgumentOutOfRangeException("The dates are out of the valid range for SQL Server datetime.");
            }

            var reservationDetail = _context.ReservationDetails.FirstOrDefault(rd => rd.ReservationDetailId == ReservationDetailId);
            if (reservationDetail == null)
            {
                return NotFound("Reservation detail not found.");
            }

            reservationDetail.NumberOfGuest = NumberOfGuest;
            reservationDetail.FacilityId = _facilityId;
            reservationDetail.RoomId = _roomId;
            reservationDetail.StartDate = StartDate;
            reservationDetail.EndDate = EndDate;
            reservationDetail.Purpose = Purpose;

            if (EquipmentNeeded != null && EquipmentNeeded.Any())
            {
                reservationDetail.EquipmentNeeded = string.Join(",", EquipmentNeeded.Select(e => e.Trim()));
            }
            else
            {
                reservationDetail.EquipmentNeeded = string.Empty;
            }

            try
            {
                _context.ReservationDetails.Update(reservationDetail);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"An error occurred while updating changes: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }

            return RedirectToAction("Index", "FunctionRoom");
        }



        /*Delete*/
        [HttpPost]
        public IActionResult DeleteReservationDetail(int ReservationId)
        {
            try
            {
                var reservationDetail = _context.ReservationDetails
                    .Include(rd => rd.ReservationHeader)
                    .FirstOrDefault(rd => rd.ReservationId == ReservationId);

                if (reservationDetail == null)
                {
                    return NotFound("Reservation detail not found.");
                }


                _context.ReservationDetails.Remove(reservationDetail);


                var reservationHeader = reservationDetail.ReservationHeader;
                if (reservationHeader != null)
                {
                    _context.ReservationHeaders.Remove(reservationHeader);
                }

                _context.SaveChanges();

                return RedirectToAction("Index", "FunctionRoom"); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

    }
}
