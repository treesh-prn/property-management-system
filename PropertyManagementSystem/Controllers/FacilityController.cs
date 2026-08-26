using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize("GFMS.System Admin", "GFMS.Admin Approver", "GFMS.Admin Manager")]
    public class FacilityController : Controller
    {
        private readonly DAL _context;
        private readonly ILogger<FacilityController> _logger;
        private readonly ISetupReservationGuard _reservationGuard;
        private readonly IFacilityEquipmentLinkService _equipmentLinks;

        public FacilityController(
            DAL context,
            ILogger<FacilityController> logger,
            ISetupReservationGuard reservationGuard,
            IFacilityEquipmentLinkService equipmentLinks)
        {
            _context = context;
            _logger = logger;
            _reservationGuard = reservationGuard;
            _equipmentLinks = equipmentLinks;
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ViewFacility(int FacilityId)
        {


            var facilityDetails = _context.Facilities.FirstOrDefault(f => f.FacilityId == FacilityId);
            if (facilityDetails == null)
            {
                return NotFound();
            }
            ViewBag.EquipmentLinkOptions = _equipmentLinks.GetOptions(FacilityId);
            return View(facilityDetails);
        }

        /*Display*/

        [HttpGet]
        public ActionResult getFacilities([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.AsQueryable().ToList();
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
        [HttpGet]
        public ActionResult getFacilityList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.AsQueryable().Where(x => x.FacilityClassification == "DORM").ToList();

            List<facilityList> _facilityList = new List<facilityList>();
            foreach (var item in _Facilities)
            {
                _facilityList.Add(new facilityList
                {
                    FacilityId = item.FacilityId,
                    FacilityName = item?.FacilityName
                });
            }

            return Json(_facilityList);

        }

        [HttpGet]
        public ActionResult getGHList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities
                .AsQueryable()
                .Where(x => x.FacilityClassification == "GUESTHOUSE")
                .ToList();

            List<facilityList> _ghList = new List<facilityList>();
            foreach (var item in _Facilities)
            {
                _ghList.Add(new facilityList
                {
                    FacilityId = item.FacilityId,
                    FacilityName = item?.FacilityName
                });
            }

            return Json(_ghList);

        }
        [HttpGet]
        public ActionResult getDMList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities
                .AsQueryable()
                .Where(x => x.FacilityClassification == "DORM")
                .ToList() ;

            List<facilityList> _dmList = new List<facilityList>();
            foreach(var item in _Facilities)
            {
                _dmList.Add(new facilityList
                {
                    FacilityId = item.FacilityId,
                    FacilityName = item.FacilityName
                });
            }
            return Json(_dmList);
        }
        [HttpGet]
        public ActionResult getFNList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities
                .AsQueryable()
                .Where(x => x.FacilityClassification == "FUNCTION ROOM")
                .ToList();

            List<facilityList> _fnList = new List<facilityList>();
            foreach(var item in _Facilities)
            {
                _fnList.Add(new facilityList
                {
                    FacilityId = item.FacilityId,
                    FacilityName = item.FacilityName
                });
            }
            return Json(_fnList);
        }

        /*Create*/
        [HttpPost]
        public IActionResult newFacility(
            string _name,
            string _classification,
            string _location,
            bool _isActive)
        {
            var facilityName = NormalizeFacilityName(_name);
            if (string.IsNullOrWhiteSpace(facilityName))
            {
                SetToast(
                    "Facility name is required",
                    "Enter a facility name before saving.",
                    "error");
                return RedirectToAction("Index", "Facility");
            }

            if (FacilityNameExists(facilityName))
            {
                SetToast(
                    "Duplicate facility",
                    $"A facility named {facilityName} already exists. Facility names must be unique.",
                    "warning");
                return RedirectToAction("Index", "Facility");
            }

            GFMS_Facility facility = new GFMS_Facility()
            {
                FacilityName = facilityName,
                FacilityClassification = _classification?.Trim(),
                FacilityLocation = _location?.Trim(),
                isActive = _isActive
            };


            try
            {
                _context.Facilities.Add(facility);
                _context.SaveChanges();
                SetToast(
                    "Facility created",
                    $"{facility.FacilityName} is now available in the facility directory.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to create facility {FacilityName}.", _name);
                SetToast(
                    "Facility could not be created",
                    "Review the facility details and try again.",
                    "error");
            }

            return RedirectToAction("Index", "Facility");
        }
        /*Update*/
        
        [HttpPost]
        public IActionResult EditFacility(
            int _facilityId,
            string _facilityName,
            string _facilityLocation,
            string _facilityClassification,
            bool _isActive)
        {
            var facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == _facilityId);
            if(facility == null)
            {
                SetToast("Facility not found", "The selected facility may have already been removed.", "error");
                return RedirectToAction("Index", "Facility");
            }

            var facilityName = NormalizeFacilityName(_facilityName);
            if (string.IsNullOrWhiteSpace(facilityName))
            {
                SetToast(
                    "Facility name is required",
                    "Enter a facility name before saving.",
                    "error");
                return RedirectToAction("ViewFacility", "Facility", new { FacilityId = _facilityId });
            }

            if (FacilityNameExists(facilityName, _facilityId))
            {
                SetToast(
                    "Duplicate facility",
                    $"A facility named {facilityName} already exists. Choose a different name.",
                    "warning");
                return RedirectToAction("ViewFacility", "Facility", new { FacilityId = _facilityId });
            }

            if (facility.isActive != false && !_isActive)
            {
                var blockingReservationCount = _reservationGuard
                    .CountBlockingReservationsForFacility(_facilityId, DateTime.Now);

                if (blockingReservationCount > 0)
                {
                    var reservationLabel = blockingReservationCount == 1
                        ? "reservation"
                        : "reservations";
                    SetToast(
                        "Facility cannot be made inactive",
                        $"{facility.FacilityName} has {blockingReservationCount} pending, ongoing, or upcoming {reservationLabel}. Reassign, cancel, or complete them before changing its status.",
                        "warning");
                    return RedirectToAction("ViewFacility", "Facility", new { FacilityId = _facilityId });
                }
            }

            facility.FacilityName = facilityName;
            facility.FacilityLocation = _facilityLocation?.Trim();
            facility.FacilityClassification = _facilityClassification?.Trim();
            facility.isActive = _isActive;

            try
            {
                _context.Facilities.Update(facility);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to update facility {FacilityId}.", _facilityId);
                SetToast(
                    "Facility could not be updated",
                    "The changes were not saved. Review the details and try again.",
                    "error");
                return RedirectToAction("ViewFacility", "Facility", new { FacilityId = _facilityId });
            }
            SetToast(
                "Facility updated",
                $"Changes to {facility.FacilityName} were saved successfully.",
                "success");
            string redirectUrl = Url.Action("ViewFacility", "Facility", new { FacilityId = _facilityId});
            return Redirect(redirectUrl);
        }

        /*Delete*/

        [HttpPost]
        public IActionResult DeleteFacility(int FacilityId)
        {
            GFMS_Facility facility = _context.Facilities.Find(FacilityId);
            if(facility == null)
            {
                SetToast("Facility not found", "The selected facility may have already been removed.", "error");
                return RedirectToAction("Index", "Facility");
            }
            try
            {
                var facilityName = facility.FacilityName;
                _context.Facilities.Remove(facility);
                _context.SaveChanges();
                SetToast(
                    "Facility deleted",
                    $"{facilityName} was removed from the facility directory.",
                    "success");
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Failed to delete facility {FacilityId}.", FacilityId);
                SetToast(
                    "Facility could not be deleted",
                    "This facility may still be linked to rooms, equipment, or reservations.",
                    "error");
            }
            return RedirectToAction("Index", "Facility");
        }

        private void SetToast(string title, string message, string type)
        {
            TempData["ToastTitle"] = title;
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = type;
        }

        private bool FacilityNameExists(string facilityName, int? excludedFacilityId = null)
        {
            var normalizedName = facilityName.ToUpperInvariant();
            return _context.Facilities
                .AsNoTracking()
                .Any(facility =>
                    facility.FacilityName != null &&
                    facility.FacilityName.Trim().ToUpper() == normalizedName &&
                    (!excludedFacilityId.HasValue || facility.FacilityId != excludedFacilityId.Value));
        }

        private static string NormalizeFacilityName(string? facilityName) =>
            facilityName?.Trim() ?? string.Empty;

    }
}
