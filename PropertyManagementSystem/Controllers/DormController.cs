using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using PropertyManagementSystem.Services;
using System.Security.Claims;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using PropertyManagementSystem.Attributes;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize(
        ReservationRoles.Requestor,
        ReservationRoles.DepartmentHead,
        ReservationRoles.AdminManager,
        ReservationRoles.AdminApprover,
        ReservationRoles.SystemAdmin)]
    public class DormController : Controller
    {
        private readonly DAL _context;
        private static readonly HashSet<string> AllowedUploadExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" };
        private const long MaxUploadBytes = 10 * 1024 * 1024;
        private readonly IRoleService _roleService;
        private readonly EmailService _emailService;
        private readonly EmployeeService _employeeService;
        private readonly IConfiguration _configuration;
        private readonly IReservationAuthorizationService _authorization;
        private readonly IReservationAuditService _audit;
        private readonly IReservationAssignmentService _assignments;
        private readonly IReservationCodeService _reservationCodes;
        private readonly IReservationProcessingPolicy _processingPolicy;
        private readonly IReservationScheduleStatusService _scheduleStatus;
        private readonly IReservationEquipmentAllocationService _equipmentAllocations;

        public DormController(
            DAL context,
            IRoleService roleService,
            EmailService emailService,
            EmployeeService employeeService,
            IConfiguration configuration,
            IReservationAuthorizationService authorization,
            IReservationAuditService audit,
            IReservationAssignmentService assignments,
            IReservationCodeService reservationCodes,
            IReservationProcessingPolicy processingPolicy,
            IReservationScheduleStatusService scheduleStatus,
            IReservationEquipmentAllocationService equipmentAllocations)
        {
            _context = context;
            _roleService = roleService;
            _emailService = emailService;
            _employeeService = employeeService;
            _configuration = configuration;
            _authorization = authorization;
            _audit = audit;
            _assignments = assignments;
            _reservationCodes = reservationCodes;
            _processingPolicy = processingPolicy;
            _scheduleStatus = scheduleStatus;
            _equipmentAllocations = equipmentAllocations;
        }
        public IActionResult Index()
        {
            var rooms = _context.Rooms
           .Include(r => r.Facility)
           .Where(x => x.RoomType == 2)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 2))
           .Select(facility => new GFMS_Facility
           {
               FacilityId = facility.FacilityId,
               FacilityName = facility.FacilityName,
           })
            .ToList();

            var newReservationCount = _context.ReservationHeaders.Count(r =>
                r.ReservationType == "DRM" &&
                (r.Status == "FOR DEPARTMENT HEAD'S APPROVAL" ||
                 r.Status == "FOR ADMIN MANAGER'S APPROVAL" ||
                 r.Status == "FOR ADMIN APPROVER'S APPROVAL"));

            var approvedAssignments =
                from detail in _context.ReservationDetails
                join header in _context.ReservationHeaders on detail.ReservationId equals header.ReservationId
                where header.ReservationType == "DRM"
                      && header.Status == "APPROVED"
                      && detail.RoomId.HasValue
                select new { header.ReservationId, RoomId = detail.RoomId.Value };

            var activeRoomCapacity = _context.Rooms
                .Where(r => r.RoomType == 2 && r.isActive)
                .Sum(r => (int?)r.Capacity) ?? 0;
            var assignedDormerCount = approvedAssignments
                .Select(a => a.ReservationId)
                .Distinct()
                .Count();
            var occupiedRoomsCount = approvedAssignments
                .Select(a => a.RoomId)
                .Distinct()
                .Count();
            var availableBedCount = Math.Max(activeRoomCapacity - assignedDormerCount, 0);


            var viewModel = new ReservationViewModel
            {
                Requestor = _context.ReservationHeaders.FirstOrDefault()?.Requestor,
                NewDRMReservationsCount = newReservationCount,
                ActiveGuestCount = assignedDormerCount,
                OccupiedRoomsCount = occupiedRoomsCount,
                AvailableRoomsCount = availableBedCount,
            };

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;

            return View(viewModel);
        }

        public async Task<ActionResult> getDormRoomsByFacility([DataSourceRequest] DataSourceRequest request, int facilityId)
        {
            if (!await CanViewDormOccupancyAsync())
            {
                return Forbid();
            }

            var rooms = _context.Rooms
                .Where(room => room.RoomType == 2 && room.FacilityId == facilityId)
                .Select(room => new
                {
                    room.RoomId,
                    room.RoomName,
                    room.RoomStatus,
                    room.Capacity,
                    room.isActive,
                    OccupiedCount = _context.ReservationDetails
                        .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                        .Where(x => x.rd.RoomId == room.RoomId && x.rh.ReservationType == "DRM" && x.rh.Status == "APPROVED")
                        .Select(x => x.rh.ReservationId)
                        .Distinct()
                        .Count(),
                })
                .AsEnumerable()
                .Select(room => new
                {
                    room.RoomId,
                    room.RoomName,
                    room.RoomStatus,
                    room.Capacity,
                    room.isActive,
                    room.OccupiedCount,
                    AvailableCount = Math.Max(room.Capacity - room.OccupiedCount, 0),
                    OccupancyPercent = room.Capacity > 0 ? Math.Round((double)room.OccupiedCount / room.Capacity * 100, 0) : 0,
                    AvailabilityStatus = !room.isActive ? "Inactive" :
                        room.Capacity <= room.OccupiedCount ? "Full" :
                        room.OccupiedCount > 0 ? "Partially Occupied" : "Available"
                })
                .ToList();

            return Json(rooms.ToDataSourceResult(request));
        }

        public async Task<ActionResult> GetDormFacilities([DataSourceRequest] DataSourceRequest request)
        {
            if (!await CanViewDormOccupancyAsync())
            {
                return Forbid();
            }

            var facilities = _context.Facilities
                .AsNoTracking()
                .Where(facility => facility.FacilityClassification == "DORM")
                .ToList();

            var result = facilities.ToDataSourceResult(request, facility => new
            {
                facility.FacilityId,
                facility.FacilityName,
                facility.FacilityLocation,
                facility.isActive,
                RoomCount = _context.Rooms.Count(room => room.FacilityId == facility.FacilityId && room.RoomType == 2),
                TotalCapacity = _context.Rooms
                    .Where(room => room.FacilityId == facility.FacilityId && room.RoomType == 2)
                    .Sum(room => (int?)room.Capacity) ?? 0,
                OccupiedCount = _context.ReservationDetails
                    .Join(_context.ReservationHeaders, detail => detail.ReservationId, header => header.ReservationId, (detail, header) => new { detail, header })
                    .Join(_context.Rooms, item => item.detail.RoomId, room => (int?)room.RoomId, (item, room) => new { item.header, room })
                    .Where(item => item.room.FacilityId == facility.FacilityId
                        && item.header.ReservationType == "DRM"
                        && item.header.Status == "APPROVED")
                    .Select(item => item.header.ReservationId)
                    .Distinct()
                    .Count()
            });

            return Json(result);
        }

        public async Task<ActionResult> getDormersByRoom([DataSourceRequest] DataSourceRequest request, int roomId)
        {
            if (!await CanViewDormOccupancyAsync())
            {
                return Forbid();
            }

            var dormers = _context.ReservationDetails
                .Join(_context.ReservationHeaders, rd => rd.ReservationId, rh => rh.ReservationId, (rd, rh) => new { rd, rh })
                .Where(x => x.rd.RoomId == roomId && x.rh.ReservationType == "DRM" && x.rh.Status == "APPROVED")
                .Select(x => new
                {
                    x.rd.ReservationDetailId,
                    x.rh.ReservationId,
                    x.rh.ReservationCode,
                    Dormer = x.rh.Requestor,
                    x.rh.RequestorEmail,
                    x.rd.ContactNumber,
                    x.rd.ScheduleType,
                    x.rd.AppliedUnit,
                    x.rh.Status,
                    x.rh.ReservationType,
                    x.rh.GUID
                })
                .AsEnumerable()
                .GroupBy(x => x.ReservationId)
                .Select(group => group.First())
                .ToList();

            return Json(dormers.ToDataSourceResult(request));
        }

        public async Task<ActionResult> GetDormerDirectory([DataSourceRequest] DataSourceRequest request)
        {
            if (!await CanViewDormOccupancyAsync())
            {
                return Forbid();
            }

            var assignments = (
                from detail in _context.ReservationDetails.AsNoTracking()
                join header in _context.ReservationHeaders.AsNoTracking()
                    on detail.ReservationId equals header.ReservationId
                join room in _context.Rooms.AsNoTracking()
                    on detail.RoomId equals (int?)room.RoomId
                join facility in _context.Facilities.AsNoTracking()
                    on room.FacilityId equals facility.FacilityId
                where header.ReservationType == "DRM" && header.Status == "APPROVED"
                select new
                {
                    header.ReservationId,
                    detail.ReservationDetailId,
                    header.ReservationCode,
                    Dormer = header.Requestor,
                    header.RequestorEmail,
                    detail.ContactNumber,
                    detail.ScheduleType,
                    facility.FacilityName,
                    room.RoomName,
                    header.Status,
                    header.GUID,
                    header.ReservationType
                })
                .AsEnumerable()
                .GroupBy(item => item.ReservationId)
                .Select(group => group.First())
                .Select(item => new
                {
                    item.ReservationDetailId,
                    item.ReservationCode,
                    item.Dormer,
                    item.RequestorEmail,
                    item.ContactNumber,
                    ScheduleType = item.ScheduleType switch
                    {
                        0 => "Regular",
                        1 => "Swing",
                        2 => "Graveyard",
                        _ => "Not specified"
                    },
                    item.FacilityName,
                    item.RoomName,
                    item.Status,
                    item.GUID,
                    item.ReservationType
                })
                .OrderBy(item => item.FacilityName)
                .ThenBy(item => item.RoomName)
                .ThenBy(item => item.Dormer)
                .ToList();

            return Json(assignments.ToDataSourceResult(request));
        }

        private async Task<bool> CanViewDormOccupancyAsync()
        {
            var activeRole = await _roleService.GetActiveRoleAsync(User);
            return activeRole == "GFMS.Department Head"
                || activeRole == "GFMS.Admin Manager"
                || activeRole == "GFMS.Admin Approver"
                || activeRole == "GFMS.System Admin";
        }

        public async Task<IActionResult> ReservationList()
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            var employee = await _roleService.GetUserName(User);
            var requestorEmail = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            ViewData["ReservationYears"] = await ReservationHistoryYearService.GetAvailableYearsAsync(
                _context, "DRM", requestorEmail, employee?.Email, employee?.Fullname);

            return View();

        }
        public async Task<IActionResult> GetDorm(int ReservationId)
        {
            var header = _context.ReservationHeaders
                .FirstOrDefault(h => h.ReservationId == ReservationId && h.ReservationType == "DRM");
            if (header == null) return NotFound();
            var authorization = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Read);
            if (!authorization.Succeeded) return Forbid();
            var facilities = _context.Facilities.AsQueryable().ToList();
            return Json(facilities
                .Select(c => new { FacilityId = c.FacilityId, FacilityName = c.FacilityName, FacilityClass = c.FacilityClassification })
                .Where(p => p.FacilityClass == "DORM")
                .OrderBy(o => o.FacilityName).ToList());
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upload(List<IFormFile> files, string fileUid, int chunkIndex)
        {
            if (!DormHousingApplicationFeature.IsEnabled)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    DormHousingApplicationFeature.UnavailableMessage);
            }

            try
            {
                if (files == null || files.Count == 0 || files.Count > 5)
                    return BadRequest("No files supplied.");
                List<string> uploadedFilePaths = new List<string>();

                foreach (var file in files)
                {
                    if (file != null && file.Length > 0)
                    {
                        if (file.Length > MaxUploadBytes)
                            return BadRequest("File exceeds the 10 MB limit.");
                        var originalName = Path.GetFileName(file.FileName ?? string.Empty);
                        var extension = Path.GetExtension(originalName);
                        if (string.IsNullOrWhiteSpace(originalName) || originalName != file.FileName ||
                            !AllowedUploadExtensions.Contains(extension))
                            return BadRequest("Unsupported or invalid file name.");

                        string directoryPath = GetUploadDirectory();
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

                        // Never use a client supplied name.  A stable opaque name permits chunked uploads
                        // while preventing traversal, overwrite and executable-file attacks.
                        var chunkKey = Guid.TryParse(fileUid, out var parsedUid) ? parsedUid.ToString("N") :
                            (string.IsNullOrWhiteSpace(fileUid) ? Guid.NewGuid().ToString("N") :
                            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(fileUid))).Substring(0, 32));
                        var storedName = chunkKey + extension.ToLowerInvariant();
                        var filepath = Path.Combine(directoryPath, storedName);
                        using (var fs = new FileStream(filepath, FileMode.Append, FileAccess.Write, FileShare.None))
                        {
                            file.CopyTo(fs);
                        }
                        string relativePath = Url.Action(nameof(Download), "Dorm", new { fileName = storedName })!;
                        uploadedFilePaths.Add(relativePath);

                        Console.WriteLine($"Uploaded file: {relativePath}");
                    }
                }

                return Json(new { success = true, fileUid = fileUid, chunkIndex = chunkIndex, filepaths = uploadedFilePaths });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading chunk: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Remove([FromBody] string[] files)
        {
            foreach (var fullName in files)
            {
                var candidate = (fullName ?? string.Empty).Split('?', 2)[0];
                var physicalPath = Path.Combine(GetUploadDirectory(), Path.GetFileName(candidate));

                if (System.IO.File.Exists(physicalPath))
                {
                    System.IO.File.Delete(physicalPath);
                }
            }

            return Content("");
        }

        [HttpGet]
        public async Task<IActionResult> Download(string fileName)
        {
            var safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName ||
                !AllowedUploadExtensions.Contains(Path.GetExtension(safeName)))
                return NotFound();

            var path = Path.Combine(GetUploadDirectory(), safeName);
            if (!System.IO.File.Exists(path)) return NotFound();

            var url = Url.Action(nameof(Download), "Dorm", new { fileName = safeName });
            var header = _context.ReservationDetails
                .Join(_context.ReservationHeaders, d => d.ReservationId, h => h.ReservationId, (d, h) => new { d, h })
                .AsEnumerable()
                .Where(x => (x.d.FileAttachment ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Any(v => string.Equals(v.Trim(), url, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.h)
                .FirstOrDefault();
            if (header == null) return Forbid();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Read);
            if (!access.Succeeded) return Forbid();

            var contentType = Path.GetExtension(safeName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
                ".doc" => "application/msword", _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            };
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }

        private string GetUploadDirectory()
        {
            var configuredPath = _configuration["Qa:UploadRoot"];
            return string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Uploads")
                : Path.GetFullPath(configuredPath);
        }

        [HttpGet]
        public async Task<ActionResult> getDormReservation(
            [DataSourceRequest] DataSourceRequest? request,
            string? gridType = "myReservation",
            int? FacilityId = null,
            int? RoomId = null,
            int? year = null)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            Console.WriteLine("FacilityId: " + FacilityId);
            Console.WriteLine("RoomId: " + RoomId);
            var employee = await _roleService.GetUserName(User);
            var assignedRoles = await _roleService.GetRolesAsync(User);

            var requestorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name
                ?? string.Empty;
            var employeeEmail = employee?.Email ?? string.Empty;
            request ??= new DataSourceRequest();
            gridType = string.IsNullOrWhiteSpace(gridType) ? "myReservation" : gridType;
            var query = _context.ReservationHeaders.AsQueryable()
                .Where(x => x.ReservationType == "DRM")
                .Where(x => x.Status != "DELETED");

            if (gridType == "myReservation")
            {
                var selectedYear = NormalizeReservationYear(year);
                if (selectedYear.HasValue)
                {
                    var yearStart = new DateTime(selectedYear.Value, 1, 1);
                    var nextYearStart = yearStart.AddYears(1);
                    query = query.Where(header =>
                        header.Status == ReservationStatuses.ForRescheduling ||
                        _context.ReservationDetails.Any(detail =>
                            detail.ReservationId == header.ReservationId &&
                            detail.StartDate >= yearStart && detail.StartDate < nextYearStart) ||
                        (!_context.ReservationDetails.Any(detail => detail.ReservationId == header.ReservationId) &&
                         header.DateFiled >= yearStart && header.DateFiled < nextYearStart));
                }

                var requestorName = employee?.Fullname;
                query = query.Where(x =>
                    (!string.IsNullOrEmpty(requestorEmail) && x.RequestorEmail == requestorEmail) ||
                    (!string.IsNullOrEmpty(employeeEmail) && x.RequestorEmail == employeeEmail) ||
                    (!string.IsNullOrEmpty(requestorName) && x.Requestor == requestorName));
            }
            else if (gridType == "reservationApproval")
            {
                query = query.ForAssignedApprovalRoles(
                    assignedRoles,
                    employee,
                    _context.Employees.AsNoTracking(),
                    requestorEmail);
            }
            else
            {
                query = query.Where(_ => false);
            }

            var reservationHeaders = query
                .Select(header => new
                {
                    header,
                    Department = _context.Departments.FirstOrDefault(d => d.Id == header.Department),
                    Details = _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).ToList(),
                    Facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).FirstOrDefault().FacilityId),
                    Room = _context.Rooms.FirstOrDefault(r => r.RoomId == _context.ReservationDetails.Where(rd => rd.ReservationId == header.ReservationId).FirstOrDefault().RoomId)
                })
                .ToList();

            var reservationViewModels = reservationHeaders.Select(reservation => new ReservationViewModel
            {
                ReservationId = reservation.header.ReservationId,
                ReservationCode = reservation.header.ReservationCode,
                Requestor = reservation.header.Requestor,
                RequestorEmail = reservation.header.RequestorEmail,
                Status = reservation.header.Status,
                GUID = reservation.header.GUID,
                CanDelete = reservation.header.Status == "DRAFT",
                FacilityName = reservation.Facility?.FacilityName,
                RoomName = reservation.Room?.RoomName,
                FacilityLocation = reservation.Facility?.FacilityLocation,
                FacilityId = reservation.Facility?.FacilityId ?? 0,
                ReservationType = reservation.header.ReservationType,
                DepartmentName = reservation.Department?.DepartmentCode,
                ReservationDetailId = reservation.Details.FirstOrDefault()?.ReservationDetailId ?? 0
            }).ToList();

            DataSourceResult result = reservationViewModels.ToDataSourceResult(request, model => new
            {
                model.ReservationId,
                model.ReservationCode,
                model.Requestor,
                model.DepartmentName,
                model.RequestorEmail,
                model.FacilityName,
                model.RoomName,
                model.FacilityLocation,
                model.Status,
                model.GUID,
                model.CanDelete,
                model.ReservationType,
                model.ReservationDetailId
            });

            return Json(result);
        }

        private static int? NormalizeReservationYear(int? year)
        {
            if (year == 0) return null;
            return year is >= 2000 && year <= 9999 ? year : DateTime.Today.Year;
        }


        public async Task<IActionResult> getRoomDetails([DataSourceRequest] DataSourceRequest request, int facilityId, int RoomId)
        {
            if (!await CanViewDormOccupancyAsync()) return Forbid();
            var reservationDetails = _context.ReservationDetails
                .Where(rd => rd.FacilityId == facilityId && rd.RoomId == RoomId)
                .ToList();

            var room = _context.Rooms.FirstOrDefault(r => r.RoomId == RoomId);

            var viewModels = reservationDetails.Select(rd =>
            {
                var header = _context.ReservationHeaders
                    .FirstOrDefault(rh => rh.ReservationId == rd.ReservationId);

                return new ReservationViewModel
                {
                    ReservationDetailId = rd?.ReservationDetailId,
                    RoomName = room?.RoomName,
                    Requestor = header?.Requestor,
                };
            }).ToList();

            return Json(viewModels.ToDataSourceResult(request));
        }


        /*Create Reservation*/
        [HttpPost]
        public async Task<IActionResult> CreateReservationAsync(int _department, string _requestor, string _requestorEmail)
        {
            if (!DormHousingApplicationFeature.IsEnabled)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    DormHousingApplicationFeature.UnavailableMessage);
            }

            var actor = await _authorization.GetActorAsync(User);
            if (!await _authorization.CanCreateAsync(User)) return Forbid();
            var userId = actor.UserId;
            Console.WriteLine("User Id: " + userId);
            Console.WriteLine("Department Id: " + _department);

            if (string.IsNullOrWhiteSpace(actor.EmployeeEmail) ||
                string.IsNullOrWhiteSpace(actor.EmployeeName) ||
                !actor.DepartmentId.HasValue)
            {
                return Forbid();
            }

            var (managerEmail, managerFullName) = await _employeeService.GetManagerInfo(userId);

            if (managerEmail == null || managerFullName == null)
            {
                return NotFound("Manager not found for the given department.");
            }

            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader()
            {
                GUID = unique,
                Requestor = actor.EmployeeName,
                RequestorEmail = actor.EmployeeEmail,
                Approver = managerFullName,
                ApproverEmail = managerEmail,
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = "DRAFT",
                Department = actor.DepartmentId.Value,
                ReservationType = "DRM"

            };

            await _reservationCodes.CreateAsync(_reservation, "RSQ-DRM");

            return RedirectToAction("ViewReservationDetails", new { Id = unique});

        }

        public async Task<IActionResult> ViewReservationDetails(string Id)
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

                if (reservationHeader.ReservationType != "DRM") return NotFound();
                await _scheduleStatus.RefreshAsync(reservationHeader, DateTime.Now);
                var access = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.Read);
                if (!access.Succeeded) return Forbid();

                int reservationId = reservationHeader.ReservationId;
                var requestorEmail = reservationHeader.RequestorEmail;

                Console.WriteLine("Requestor Email: " + requestorEmail);

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();


                Gen_Department department = null;
                GFMS_Facility facility = null;
                GFMS_Rooms rooms = null;

                var employee = _context.Employees.FirstOrDefault(e => e.Email == requestorEmail);
                if (employee == null)
                {
                    return NotFound("Employee not found");
                }
                Console.WriteLine("Employee:" + employee.Email);

                if (reservationDetails.Any())
                {
                    var firstDetail = reservationDetails.FirstOrDefault();
                    facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == firstDetail.FacilityId);
                    rooms = _context.Rooms.FirstOrDefault(r => r.RoomId == firstDetail.RoomId);
                }

                department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);
                var position = _context.Positions.FirstOrDefault(p => p.Id == employee.PositionId);
                var level = _context.EmploymentLevels.FirstOrDefault(l => l.Id == employee.EmploymentLevelId);


                var reservation = new ReservationViewModel
                {
                    Department = department,
                    Facility = facility,
                    Room = rooms,
                    EmploymentLevel = level,
                    Position = position,
                    ApplicantCivilStatus = DormHousingEligibilityPolicy.NormalizeCivilStatus(employee.MaritalStatus),
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    NumberOfGuest = reservationDetails.Sum(rd => rd.NumberOfGuest)
                };
                var equipment = _equipmentAllocations.GetForReservation(reservationId);
                reservation.IncludedEquipments = equipment.Where(item => item.IsAutomatic).ToList();
                reservation.AdditionalEquipments = equipment.Where(item => !item.IsAutomatic).ToList();

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

        public async Task<IActionResult> ReservationDetails(string Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return BadRequest("GUID cannot be null or empty");
            }

            if (!DormHousingApplicationFeature.IsEnabled)
            {
                return RedirectToAction(nameof(ViewReservationDetails), new { Id });
            }

            try
            {
                var reservationHeader = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id);
                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                if (reservationHeader.ReservationType != "DRM") return NotFound();
                var access = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.Read);
                if (!access.Succeeded) return Forbid();

                int reservationId = reservationHeader.ReservationId;

                var reservationDetails = _context.ReservationDetails
                    .Where(x => x.ReservationId == reservationId)
                    .ToList();

                var department = _context.Departments.FirstOrDefault(d => d.Id == reservationHeader.Department);
                if (department == null)
                {
                    return NotFound("Department not found");
                }

                var reservation = new ReservationDetailViewModel
                {
                    ReservationHeader = reservationHeader,
                    ReservationDetails = reservationDetails,
                    Gen_Department = department
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
        /*Create*/
        [HttpPost]
        public async Task<IActionResult> CreateReservationDetails(
            int _reservationId, int _unitApplied, int _schedType, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation,int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, string[] _dependents, string[] _age, string[] _location, string[] _position, string filepath, string[] _dependent_status)
        {
            if (!DormHousingApplicationFeature.IsEnabled)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    DormHousingApplicationFeature.UnavailableMessage);
            }

            try
            {
                var reservationHeader = _context.ReservationHeaders
                    .FirstOrDefault(rh => rh.ReservationId == _reservationId);

                if (reservationHeader == null)
                {
                    return NotFound("Reservation Header not found");
                }

                if (reservationHeader.ReservationType != "DRM") return NotFound();
                var access = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.CreateDetails);
                var accessFailure = AuthorizationFailure(access);
                if (accessFailure != null) return accessFailure;

                var employee = _context.Employees
                    .AsNoTracking()
                    .FirstOrDefault(e => e.Email == reservationHeader.RequestorEmail);
                if (employee == null)
                {
                    return NotFound("Employee profile not found.");
                }

                var civilStatus = DormHousingEligibilityPolicy.NormalizeCivilStatus(employee.MaritalStatus);
                if (!DormHousingEligibilityPolicy.CanApplyForUnit(civilStatus, _unitApplied))
                {
                    return BadRequest("Housing is available only to employees whose civil status is Married. Dorm remains available.");
                }

                // Dependent fields are optional. The previous implementation required
                // every dependent array, so an application with no dependents was
                // redirected without ever creating its reservation detail.
                _dependents ??= Array.Empty<string>();
                _age ??= Array.Empty<string>();
                _location ??= Array.Empty<string>();
                _position ??= Array.Empty<string>();
                _dependent_status ??= Array.Empty<string>();

                var dependentCount = new[]
                {
                    _dependents.Length,
                    _age.Length,
                    _location.Length,
                    _position.Length,
                    _dependent_status.Length
                }.Max();

                var dependentIndexes = Enumerable.Range(0, dependentCount)
                    .Where(i => !string.IsNullOrWhiteSpace(GetValue(_dependents, i))
                        || !string.IsNullOrWhiteSpace(GetValue(_age, i))
                        || !string.IsNullOrWhiteSpace(GetValue(_location, i))
                        || !string.IsNullOrWhiteSpace(GetValue(_position, i))
                        || !string.IsNullOrWhiteSpace(GetValue(_dependent_status, i)))
                    .ToList();

                // Keep one detail row for the applicant even when no dependents were added.
                if (dependentIndexes.Count == 0)
                {
                    dependentIndexes.Add(-1);
                }

                foreach (var index in dependentIndexes)
                {
                    var reservationDetails = new GFMS_ReservationDetails
                    {
                        ReservationId = _reservationId,
                        AppliedUnit = _unitApplied,
                        ScheduleType = _schedType,
                        ContactNumber = _contactNumber ?? string.Empty,
                        PermanentAddress = _permanentAdd ?? string.Empty,
                        CurrentAddress = _currentAdd ?? string.Empty,
                        HomeOwnership = _homeOwnership,
                        OwnedHouseLocation = _ownedLocation,
                        SpecificLocation = _specificLocation ?? string.Empty,
                        ModeOfTransportation = _modeOfTransport,
                        VehicleType = _vehicleType ?? string.Empty,
                        TravelTime = _travelTime ?? string.Empty,
                        Fare = _fare ?? string.Empty,
                        CivilStatus = civilStatus,
                        DependentName = index < 0 ? string.Empty : GetValue(_dependents, index),
                        DependentAge = index < 0 ? string.Empty : GetValue(_age, index),
                        WorkPosition = index < 0 ? string.Empty : GetValue(_position, index),
                        StudyLocation = index < 0 ? string.Empty : GetValue(_location, index),
                        OtherInfo = index < 0 ? string.Empty : GetValue(_dependent_status, index),
                        DateAdded = DateTime.UtcNow,
                        FileAttachment = filepath ?? string.Empty
                    };

                    _context.ReservationDetails.Add(reservationDetails);
                }

                var previousStatus = reservationHeader.Status;
                reservationHeader.Status = await _authorization.GetSubmissionStatusAsync(reservationHeader);
                reservationHeader.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
                _context.SaveChanges();
                await AddSubmissionAuditAsync(reservationHeader, previousStatus, "SUBMIT");
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

            return RedirectToAction("Index", "Dorm");
        }

        static string GetValue(string[] values, int index)
        {
            return index >= 0 && index < values.Length ? values[index] ?? string.Empty : string.Empty;
        }



        [HttpPost]
        public async Task<IActionResult> UpdateReservationDetails(int reservationId, int _unitApplied, int _schedType, string _contactNumber, string _permanentAdd, string _currentAdd, int _homeOwnership, int _ownedLocation, string _specificLocation, int _modeOfTransport, string _vehicleType, string _travelTime, string _fare, List<string>? _dependents, List<string>? _age, List<string>? _location, List<string>? _position, List<string>? _dependent_status, string filepath)
        {
            var existingReservationDetails = _context.ReservationDetails
                .Where(rd => rd.ReservationId == reservationId)
                .OrderBy(rd => rd.ReservationDetailId)
                .ToList();

            var reservationHeader = _context.ReservationHeaders
                .FirstOrDefault(rh => rh.ReservationId == reservationId);

            if (reservationHeader == null)
            {
                return NotFound("Reservation Header not found");
            }

            if (reservationHeader.ReservationType != "DRM") return NotFound();
            var access = await _authorization.AuthorizeAsync(
                User, reservationHeader, ReservationOperation.Edit);
            var accessFailure = AuthorizationFailure(access);
            if (accessFailure != null) return accessFailure;
            var previousStatus = reservationHeader.Status;
            var isResubmission = previousStatus == ReservationStatuses.ForRescheduling;

            var employee = _context.Employees
                .AsNoTracking()
                .FirstOrDefault(e => e.Email == reservationHeader.RequestorEmail);
            if (employee == null)
            {
                return NotFound("Employee profile not found.");
            }

            var civilStatus = DormHousingEligibilityPolicy.NormalizeCivilStatus(employee.MaritalStatus);
            if (!DormHousingEligibilityPolicy.CanApplyForUnit(civilStatus, _unitApplied))
            {
                return BadRequest("Housing is available only to employees whose civil status is Married. Dorm remains available.");
            }

            _dependents ??= new List<string>();
            _age ??= new List<string>();
            _location ??= new List<string>();
            _position ??= new List<string>();
            _dependent_status ??= new List<string>();

            var dependentCount = new[]
            {
                _dependents.Count,
                _age.Count,
                _location.Count,
                _position.Count,
                _dependent_status.Count
            }.Max();

            var dependentIndexes = Enumerable.Range(0, dependentCount)
                .Where(i => !string.IsNullOrWhiteSpace(GetValue(_dependents, i))
                    || !string.IsNullOrWhiteSpace(GetValue(_age, i))
                    || !string.IsNullOrWhiteSpace(GetValue(_location, i))
                    || !string.IsNullOrWhiteSpace(GetValue(_position, i))
                    || !string.IsNullOrWhiteSpace(GetValue(_dependent_status, i)))
                .ToList();

            if (dependentIndexes.Count == 0)
            {
                dependentIndexes.Add(-1);
            }

            for (var rowIndex = 0; rowIndex < dependentIndexes.Count; rowIndex++)
            {
                var dependentIndex = dependentIndexes[rowIndex];
                GFMS_ReservationDetails detail;

                if (rowIndex < existingReservationDetails.Count)
                {
                    detail = existingReservationDetails[rowIndex];
                }
                else
                {
                    detail = new GFMS_ReservationDetails
                    {
                        ReservationId = reservationId,
                        DateAdded = DateTime.UtcNow
                    };
                    _context.ReservationDetails.Add(detail);
                }

                detail.AppliedUnit = _unitApplied;
                detail.ScheduleType = _schedType;
                detail.ContactNumber = _contactNumber ?? string.Empty;
                detail.PermanentAddress = _permanentAdd ?? string.Empty;
                detail.CurrentAddress = _currentAdd ?? string.Empty;
                detail.HomeOwnership = _homeOwnership;
                detail.OwnedHouseLocation = _ownedLocation;
                detail.SpecificLocation = _specificLocation ?? string.Empty;
                detail.ModeOfTransportation = _modeOfTransport;
                detail.VehicleType = _vehicleType ?? string.Empty;
                detail.TravelTime = _travelTime ?? string.Empty;
                detail.Fare = _fare ?? string.Empty;
                detail.CivilStatus = civilStatus;
                detail.DependentName = dependentIndex < 0 ? string.Empty : GetValue(_dependents, dependentIndex);
                detail.DependentAge = dependentIndex < 0 ? string.Empty : GetValue(_age, dependentIndex);
                detail.StudyLocation = dependentIndex < 0 ? string.Empty : GetValue(_location, dependentIndex);
                detail.WorkPosition = dependentIndex < 0 ? string.Empty : GetValue(_position, dependentIndex);
                detail.OtherInfo = dependentIndex < 0 ? string.Empty : GetValue(_dependent_status, dependentIndex);

                if (!string.IsNullOrWhiteSpace(filepath))
                {
                    detail.FileAttachment = filepath;
                }
            }

            if (existingReservationDetails.Count > dependentIndexes.Count)
            {
                _context.ReservationDetails.RemoveRange(
                    existingReservationDetails.Skip(dependentIndexes.Count));
            }

            if (isResubmission)
            {
                reservationHeader.Status = await _authorization.GetSubmissionStatusAsync(reservationHeader);
                reservationHeader.DateSubmitted = _processingPolicy.GetSubmissionTimestamp();
                reservationHeader.Remarks = null;
                reservationHeader.ApprovedBy = null;
            }

            _context.SaveChanges();
            if (isResubmission)
            {
                await AddSubmissionAuditAsync(
                    reservationHeader, previousStatus, "RESCHEDULE_AND_RESUBMIT");
            }
            else
            {
                await _audit.AddAsync(
                    reservationHeader, User, "EDIT", previousStatus, reservationHeader.Status);
            }

            return RedirectToAction("Index", "Dorm");
        }

        static string GetValue(IReadOnlyList<string> values, int index)
        {
            return index >= 0 && index < values.Count ? values[index] ?? string.Empty : string.Empty;
        }

        private async Task AddSubmissionAuditAsync(
            GFMS_ReservationHeader reservation,
            string? previousStatus,
            string action)
        {
            var bypassed = reservation.Status == ReservationStatuses.AdminApproverApproval &&
                await _authorization.ShouldBypassDepartmentHeadAsync(reservation);
            await _audit.AddAsync(
                reservation,
                User,
                bypassed ? $"{action}_DEPARTMENT_HEAD_BYPASSED" : action,
                previousStatus,
                reservation.Status,
                bypassed
                    ? "Department Head review was not required because the assigned approver also holds the Admin Manager role."
                    : null);
        }


        /*Delete*/
        [HttpPost]
        public async Task<IActionResult> DeleteReservationDetail(int reservationId)
        {
            try
            {
                Console.WriteLine(reservationId);

                var reservationHeader = _context.ReservationHeaders
                    .FirstOrDefault(rd => rd.ReservationId == reservationId);

                if (reservationHeader == null)
                {
                    return NotFound("Reservation detail not found.");
                }

                if (reservationHeader.ReservationType != "DRM") return NotFound();
                var access = await _authorization.AuthorizeAsync(
                    User, reservationHeader, ReservationOperation.Delete);
                var accessFailure = AuthorizationFailure(access);
                if (accessFailure != null) return accessFailure;

                var previousStatus = reservationHeader.Status;
                reservationHeader.Status = ReservationStatuses.Deleted;
                _context.SaveChanges();
                await _audit.AddAsync(
                    reservationHeader, User, "DELETE", previousStatus, reservationHeader.Status);

                return RedirectToAction("Index", "Dorm");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting reservation detail: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatusAsync(int ReservationId, string action, int _facilityId, int _roomId, string Remarks, bool _isCritical, bool _isOnCall, string _deptRemarks)
        {
            if (ReservationId <= 0 || string.IsNullOrEmpty(action))
            {
                return BadRequest("Invalid parameters.");
            }

            var reservation = _context.ReservationHeaders
                .FirstOrDefault(r => r.ReservationId == ReservationId && r.ReservationType == "DRM");
            if (reservation == null)
            {
                return NotFound();
            }

            var reservationDetails = _context.ReservationDetails.FirstOrDefault(r => r.ReservationId == reservation.ReservationId);
            if (reservationDetails == null)
            {
                return NotFound("Reservation detail not found");
            }

            var operation = action switch
            {
                "Approve" => ReservationOperation.Approve,
                "Reject" => ReservationOperation.Reject,
                _ => (ReservationOperation?)null
            };
            if (!operation.HasValue) return BadRequest("Invalid action.");

            var overdueTransition = await _scheduleStatus.RefreshAsync(reservation, DateTime.Now);
            if (overdueTransition != null)
            {
                TempData["AlertMessage"] = "This request is no longer eligible for approval.";
                TempData["AlertSubMessage"] = overdueTransition.Message;
                TempData["AlertType"] = "warning";
                return RedirectToAction("ReservationList", "Dorm");
            }

            var authorization = await _authorization.AuthorizeAsync(User, reservation, operation.Value);
            var accessFailure = AuthorizationFailure(authorization);
            if (accessFailure != null) return accessFailure;

            var actor = await _authorization.GetActorAsync(User);
            var previousStatus = reservation.Status;

            if (action == "Approve" && reservation.Status == ReservationStatuses.DepartmentHeadApproval)
            {
                reservation.Status = authorization.NextStatus;
                reservationDetails.DeptHeadRemarks = _deptRemarks;
                reservationDetails.isCritical = _isCritical;
                reservationDetails.isOnCall = _isOnCall;
                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";
            }
            else if (action == "Approve" && reservation.Status == ReservationStatuses.AdminManagerApproval)
            {
                reservation.Status = authorization.NextStatus;
                reservation.ApprovedBy = actor.EmployeeName;

                TempData["AlertMessage"] = "Approved!";
                TempData["AlertSubMessage"] = "Reservation has been approved";
                TempData["AlertType"] = "success";

                /*await _emailService.SendApprovalNotification(
                     reservation,
                     reservation.RequestorEmail
                 );*/
            }
            else if (action == "Approve" && reservation.Status == ReservationStatuses.AdminApproverApproval)
            {
                var startDate = reservationDetails.StartDate ??
                    reservationDetails.DateAdded ?? reservation.DateFiled ?? DateTime.UtcNow;
                var endDate = reservationDetails.EndDate ?? new DateTime(9999, 12, 31);
                var committed = await _assignments.AssignAsync(new ReservationAssignmentCommand(
                    new ReservationSlotRequest(
                        ReservationId,
                        reservationDetails.ReservationDetailId,
                        _facilityId,
                        _roomId,
                        "DRM",
                        startDate,
                        endDate,
                        Math.Max(reservationDetails.NumberOfGuest, 1),
                        ReservationRoomTypes.DormOrHousing),
                    previousStatus!,
                    reservation.RowVersion.ToArray(),
                    reservationDetails.RowVersion.ToArray(),
                    authorization.NextStatus,
                    actor.EmployeeName));
                var commitFailure = CommitFailure(committed);
                if (commitFailure != null) return commitFailure;
                reservation = committed.Reservation!;
                reservationDetails = committed.Detail!;

                TempData["AlertMessage"] = "Room details assigned successfully!";
                TempData["AlertSubMessage"] = "Rooms are assigned to reservation";
                TempData["AlertType"] = "success";

                /*await _emailService.RoomAssignmentApprovalEmail(
                       reservation,
                       reservation.RequestorEmail,
                       reservationDetails.StartDate.ToString(),
                       reservationDetails.EndDate.ToString(),
                       reservationDetails.NumberOfGuest,
                       facilityName,
                       roomName,
                       reservationDetails.Purpose
                   );

                await _emailService.SendReservationApprovalEmail(
                      reservation,
                      reservation.RequestorEmail,
                      reservationDetails.StartDate.ToString(),
                      reservationDetails.EndDate.ToString(),
                      reservationDetails.NumberOfGuest,
                      facilityName,
                      roomName,
                      reservationDetails.Purpose
                  );*/
            }
            else if (action == "Reject")
            {
                reservation.Status = authorization.NextStatus;
                reservation.Remarks = Remarks;
                reservation.ApprovedBy = actor.EmployeeName;
                await _emailService.SendDisapprovalNotification(
                      reservation,
                      reservation.RequestorEmail
                  );
            }
            if (!(action == "Approve" && previousStatus == ReservationStatuses.AdminApproverApproval))
            {
                try
                {
                    _context.SaveChanges();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Conflict("The reservation state changed. Refresh and try again.");
                }
            }
            if (action == "Approve" && previousStatus == ReservationStatuses.AdminApproverApproval)
            {
                await _audit.AddAsync(reservation, User, "ASSIGN", previousStatus, reservation.Status);
            }
            await _audit.AddAsync(
                reservation,
                User,
                action == "Approve" ? "APPROVE" : "REJECT",
                previousStatus,
                reservation.Status);

            return RedirectToAction("ReservationList", "Dorm");
        }

        private IActionResult? AuthorizationFailure(ReservationAuthorizationResult authorization)
        {
            if (authorization.Succeeded) return null;
            return authorization.Failure == ReservationAuthorizationFailure.Forbidden
                ? Forbid()
                : Conflict("The reservation state changed. Refresh and try again.");
        }

        private IActionResult? CommitFailure(ReservationCommitResult result)
        {
            if (result.Succeeded) return null;
            return result.Failure switch
            {
                ReservationCommitFailure.NotFound => NotFound(result.Message),
                ReservationCommitFailure.Conflict => Conflict(result.Message),
                _ => BadRequest(result.Message)
            };
        }

    }
}
