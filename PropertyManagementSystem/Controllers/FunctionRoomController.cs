using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using PropertyManagementSystem.Data;
using Microsoft.EntityFrameworkCore;
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
    public class FunctionRoomController : Controller
    {
        private readonly FunctionRoomReservationService _functionRoomReservationService;
        private readonly DAL _context;
        private readonly IReservationAuthorizationService _authorization;
        private readonly IReservationScheduleStatusService _scheduleStatus;

        public FunctionRoomController(
            FunctionRoomReservationService functionRoomReservationService,
            DAL context,
            IReservationAuthorizationService authorization,
            IReservationScheduleStatusService scheduleStatus)
        {
            _functionRoomReservationService = functionRoomReservationService;
            _context = context;
            _authorization = authorization;
            _scheduleStatus = scheduleStatus;
        }

        public IActionResult Index()
        {
            var viewData = _functionRoomReservationService.GetIndexViewData();
            ViewData["Rooms"] = viewData.Rooms;
            ViewData["Facilities"] = viewData.Facilities;

            return View(viewData.Summary);
        }

        public IActionResult FunctionRoom()
        {
            return View();
        }

        public async Task<IActionResult> ReservationList()
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            ViewData["ReservationYears"] = await _functionRoomReservationService
                .GetMyReservationYearsAsync(User);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            return Json(await _functionRoomReservationService.GetCalendarReservationsAsync(request, User));
        }

        public IActionResult GetFacility()
        {
            return Json(_functionRoomReservationService.GetFacilities());
        }

        public IActionResult GetRoom(int? facilityId)
        {
            return Json(_functionRoomReservationService.GetRooms(facilityId));
        }

        public async Task<IActionResult> GetDraftAvailableFacilities()
        {
            var actor = await _authorization.GetActorAsync(User);
            if (actor.ActiveRole is not ReservationRoles.AdminApprover and not ReservationRoles.AdminManager)
                return Forbid();

            return Json(_functionRoomReservationService.GetDraftAvailableFacilities());
        }

        public async Task<IActionResult> GetDraftAvailableRooms(
            int? facilityId,
            DateTime startDate,
            DateTime endDate)
        {
            var actor = await _authorization.GetActorAsync(User);
            if (actor.ActiveRole is not ReservationRoles.AdminApprover and not ReservationRoles.AdminManager)
                return Forbid();

            return Json(_functionRoomReservationService.GetDraftAvailableRooms(
                facilityId, startDate, endDate));
        }

        public async Task<IActionResult> GetAvailableFacilities(int reservationDetailId)
        {
            var header = FindHeaderForDetail(reservationDetailId);
            if (header == null || header.ReservationType != "FN") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            if (!access.Succeeded) return access.Failure == ReservationAuthorizationFailure.Forbidden
                ? Forbid()
                : Conflict("The reservation state changed. Refresh and try again.");

            var includeOccupied = string.Equals(
                await _functionRoomReservationService.GetActiveRoleAsync(User),
                ReservationRoles.AdminManager,
                StringComparison.Ordinal);
            return Json(_functionRoomReservationService.GetAvailableFacilities(
                reservationDetailId, User, includeOccupied));
        }

        public async Task<IActionResult> GetAvailableRooms(int? facilityId, int reservationDetailId)
        {
            var header = FindHeaderForDetail(reservationDetailId);
            if (header == null || header.ReservationType != "FN") return NotFound();
            var access = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Assign);
            if (!access.Succeeded) return access.Failure == ReservationAuthorizationFailure.Forbidden
                ? Forbid()
                : Conflict("The reservation state changed. Refresh and try again.");

            var includeOccupied = string.Equals(
                await _functionRoomReservationService.GetActiveRoleAsync(User),
                ReservationRoles.AdminManager,
                StringComparison.Ordinal);
            return Json(_functionRoomReservationService.GetAvailableRooms(
                facilityId, reservationDetailId, User, includeOccupied));
        }

        [HttpGet]
        public async Task<ActionResult> GetFunctionRoomReservation(
            [DataSourceRequest] DataSourceRequest? request,
            string? gridType = "myReservation",
            int? year = null)
        {
            await _scheduleStatus.RefreshAllAsync(DateTime.Now);
            // Kendo supplies the request descriptor; keep the endpoint safe for direct
            // navigations and partial requests as well.
            request ??= new DataSourceRequest();
            gridType = string.IsNullOrWhiteSpace(gridType) ? "myReservation" : gridType;
            return Json(await _functionRoomReservationService.GetReservationGrid(request, gridType, User, year));
        }

        [HttpPost]
        public async Task<IActionResult> CreateReservation(CreateFunctionRoomReservationRequest request)
        {
            var result = await _functionRoomReservationService.CreateReservation(request, User);
            var failure = FailureResult(result);
            if (failure != null) return failure;

            return RedirectToAction("ViewReservationDetails", new { Id = result.RedirectGuid });
        }

        public async Task<IActionResult> ViewReservationDetails(string Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return BadRequest("GUID cannot be null or empty");
            }

            var header = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id && x.ReservationType == "FN");
            if (header == null)
            {
                return NotFound("Reservation Header not found");
            }

            await _scheduleStatus.RefreshAsync(header, DateTime.Now);

            var authorization = await _authorization.AuthorizeAsync(User, header, ReservationOperation.Read);
            if (!authorization.Succeeded)
            {
                return Forbid();
            }

            var reservation = _functionRoomReservationService.GetReservationDetailsViewModel(Id);
            if (reservation == null) return NotFound("Reservation Header not found");

            return View(reservation);
        }

        [HttpPost]
        public async Task<IActionResult> CreateReservationDetails(CreateFunctionRoomReservationDetailsRequest request)
        {
            var result = await _functionRoomReservationService.CreateReservationDetails(request, User);

            // A schedule validation error is an expected form error. Return the
            // requestor to the draft instead of rendering a bare HTTP 400 page.
            if (!result.Success &&
                result.Failure == ReservationOperationFailure.Validation &&
                !string.IsNullOrWhiteSpace(result.RedirectGuid))
            {
                SetAlert(result);
                return RedirectToAction("ViewReservationDetails", new { id = result.RedirectGuid });
            }

            var failure = FailureResult(result);
            if (failure != null) return failure;
            SetAlert(result);

            return RedirectToAction("ReservationList", "FunctionRoom");
        }

        [HttpPost]
        public async Task<IActionResult> EditReservationDetails(EditFunctionRoomReservationDetailsRequest request)
        {
            var result = await _functionRoomReservationService.EditReservationDetails(request, User);
            var failure = FailureResult(result);
            if (failure != null) return failure;

            SetAlert(result);
            return RedirectToAction("ReservationList", "FunctionRoom");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteReservationDetail(int reservationId)
        {
            var result = await _functionRoomReservationService.DeleteReservation(reservationId, User);
            var failure = FailureResult(result);
            if (failure != null) return failure;

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReservationStatusAsync(UpdateFunctionRoomReservationStatusRequest request)
        {
            FunctionRoomOperationResult result;
            try
            {
                result = await _functionRoomReservationService.UpdateReservationStatus(request, User);
            }
            catch (DbUpdateConcurrencyException)
            {
                SetFailureAlert(
                    "The reservation changed while it was being processed.",
                    "The latest information has been loaded. Review it before trying again.");
                return RedirectToAction("ReservationList", "FunctionRoom");
            }
            var failure = FailureResult(result);
            if (failure != null)
            {
                if (result.Failure is ReservationOperationFailure.Conflict or ReservationOperationFailure.Validation)
                {
                    SetFailureAlert(
                        "The request could not be updated.",
                        result.ErrorMessage ?? "Refresh the reservation and try again.");
                    return RedirectToAction("ReservationList", "FunctionRoom");
                }

                return failure;
            }

            SetAlert(result);
            return RedirectToAction("ReservationList", "FunctionRoom");
        }

        private void SetAlert(FunctionRoomOperationResult result)
        {
            if (string.IsNullOrEmpty(result.AlertMessage))
            {
                return;
            }

            TempData["AlertMessage"] = result.AlertMessage;
            TempData["AlertSubMessage"] = result.AlertSubMessage;
            TempData["AlertType"] = result.AlertType;
        }

        private void SetFailureAlert(string message, string subMessage)
        {
            TempData["AlertMessage"] = message;
            TempData["AlertSubMessage"] = subMessage;
            TempData["AlertType"] = "warning";
        }

        private IActionResult? FailureResult(FunctionRoomOperationResult result)
        {
            if (result.Success) return null;

            return result.Failure switch
            {
                ReservationOperationFailure.NotFound => NotFound(result.ErrorMessage),
                ReservationOperationFailure.Forbidden => Forbid(),
                ReservationOperationFailure.Conflict => Conflict(result.ErrorMessage),
                _ => BadRequest(result.ErrorMessage)
            };
        }

        private GFMS_ReservationHeader? FindHeaderForDetail(int reservationDetailId)
        {
            return _context.ReservationDetails
                .Where(d => d.ReservationDetailId == reservationDetailId)
                .Join(
                    _context.ReservationHeaders,
                    d => d.ReservationId,
                    h => h.ReservationId,
                    (d, h) => h)
                .FirstOrDefault();
        }
    }
}
