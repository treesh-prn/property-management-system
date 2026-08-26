using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize("GFMS.System Admin", "GFMS.Admin Approver", "GFMS.Admin Manager")]
    public class EquipmentController : Controller
    {
        private readonly EquipmentService _equipmentService;
        private readonly ILogger<EquipmentController> _logger;
        private readonly ISetupReservationGuard _reservationGuard;

        public EquipmentController(
            EquipmentService equipmentService,
            ILogger<EquipmentController> logger,
            ISetupReservationGuard reservationGuard)
        {
            _equipmentService = equipmentService;
            _logger = logger;
            _reservationGuard = reservationGuard;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult getEquipments([DataSourceRequest] DataSourceRequest request)
        {
            return Json(_equipmentService.GetEquipments(request));
        }

        [HttpPost]
        public ActionResult newEquipments(EquipmentRequest request)
        {
            var validationMessage = ValidateEquipmentRequest(request)
                ?? _equipmentService.ValidateLocation(request);
            if (validationMessage != null)
            {
                SetToast("Equipment details are invalid", validationMessage, "warning");
                return RedirectToAction("Index", "Equipment");
            }
            try
            {
                _equipmentService.CreateEquipment(request);
                SetToast(
                    "Equipment created",
                    $"{request._name} is now available in the equipment inventory.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to create equipment {EquipmentName}.", request._name);
                SetToast(
                    "Equipment could not be created",
                    "Review the equipment details and try again.",
                    "error");
            }
            return RedirectToAction("Index", "Equipment");
        }

        public IActionResult ViewEquipment(int Id)
        {
            var equipment = _equipmentService.GetEquipment(Id);
            if (equipment == null)
            {
                return NotFound();
            }

            return View(equipment);
        }

        [HttpPost]
        public IActionResult EditEquipment(EditEquipmentRequest request)
        {
            var validationMessage = ValidateEquipmentRequest(request)
                ?? _equipmentService.ValidateLocation(request);
            if (validationMessage != null)
            {
                SetToast("Equipment details are invalid", validationMessage, "warning");
                return RedirectToAction("ViewEquipment", "Equipment", new { Id = request.Id });
            }
            try
            {
                var existing = _equipmentService.GetEquipment(request.Id);
                if (existing?.isActive != false && !request._isActive)
                {
                    var blockingCount = _reservationGuard.CountBlockingReservationsForEquipment(
                        request.Id, DateTime.Now);
                    if (blockingCount > 0)
                    {
                        SetToast(
                            "Equipment cannot be made inactive",
                            $"{existing?.EquipmentName ?? "This equipment"} is assigned to {blockingCount} pending, ongoing, or upcoming reservation{(blockingCount == 1 ? string.Empty : "s")}. Reassign or complete them first.",
                            "warning");
                        return RedirectToAction("ViewEquipment", "Equipment", new { Id = request.Id });
                    }
                }

                if (!_equipmentService.UpdateEquipment(request))
                {
                    SetToast("Equipment not found", "The selected equipment may have already been removed.", "error");
                    return RedirectToAction("Index", "Equipment");
                }

                SetToast(
                    "Equipment updated",
                    $"Changes to {request._name} were saved successfully.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to update equipment {EquipmentId}.", request.Id);
                SetToast(
                    "Equipment could not be updated",
                    "The changes were not saved. Review the details and try again.",
                    "error");
            }

            return RedirectToAction("ViewEquipment", "Equipment", new { Id = request.Id });
        }

        [HttpPost]
        public IActionResult DeleteEquipment(int Id)
        {
            var equipment = _equipmentService.GetEquipment(Id);
            if (equipment == null)
            {
                SetToast("Equipment not found", "The selected equipment may have already been removed.", "error");
                return RedirectToAction("Index", "Equipment");
            }

            try
            {
                _equipmentService.DeleteEquipment(Id);
                SetToast(
                    "Equipment deleted",
                    $"{equipment.EquipmentName} was removed from the equipment inventory.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to delete equipment {EquipmentId}.", Id);
                SetToast(
                    "Equipment could not be deleted",
                    "This equipment may still be linked to a facility, room, or reservation.",
                    "error");
            }

            return RedirectToAction("Index", "Equipment");
        }

        private void SetToast(string title, string message, string type)
        {
            TempData["ToastTitle"] = title;
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = type;
        }

        private static string? ValidateEquipmentRequest(EquipmentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request._name))
                return "Enter an equipment name.";
            if (request._quantity < 1 || request._quantity > 100000)
                return "Quantity must be between 1 and 100,000.";
            if (request._purCost < 0 || request._halfCost < 0 || request._wholeCost < 0)
                return "Equipment costs cannot be negative.";
            if (request._preserveLocationLinks || !request._isFacilityEquipment)
                return null;
            if (!request._facilityId.HasValue || request._facilityId.Value <= 0)
                return "Select a facility for linked equipment.";
            if (request._defaultQuantity < 1 || request._defaultQuantity > request._quantity)
                return "Default linked quantity must be between 1 and the inventory quantity.";
            if (request._linkType is not EquipmentLinkTypes.Included and not EquipmentLinkTypes.AddOn)
                return "Choose a valid reservation behavior.";
            return null;
        }
    }
}
