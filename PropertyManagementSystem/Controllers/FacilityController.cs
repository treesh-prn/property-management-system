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

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class FacilityController : Controller
    {
        private readonly DAL _context;

        public FacilityController(DAL context)
        {
            _context = context;
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult Index()
        {
            return View();
        }

        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult ViewFacility(int FacilityId)
        {


            var facilityDetails = _context.Facilities.FirstOrDefault(f => f.FacilityId == FacilityId);
            if (facilityDetails == null)
            {
                return NotFound();
            }
            return View(facilityDetails);
        }

        /*Display*/

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

        public JsonResult GetOccupants()
        {
            var occupants = _context.RoomOccupants.FromSqlRaw("EXEC GFMS_GetRoomOccupantsHierarchy").ToList();

            return Json(occupants);
        }



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
        public ActionResult getDMList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities
                .AsQueryable()
                .Where(x => x.FacilityClassification == "DORM")
                .ToList();

            List<facilityList> _dmList = new List<facilityList>();
            foreach (var item in _Facilities)
            {
                _dmList.Add(new facilityList
                {
                    FacilityId = item.FacilityId,
                    FacilityName = item.FacilityName
                });
            }
            return Json(_dmList);
        }
        public ActionResult getFNList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities
                .AsQueryable()
                .Where(x => x.FacilityClassification == "FUNCTION ROOM")
                .ToList();

            List<facilityList> _fnList = new List<facilityList>();
            foreach (var item in _Facilities)
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
        public IActionResult newFacility(string _name, string _classification, string _location, bool _isActive)
        {
            GFMS_Facility facility = new GFMS_Facility()
            {
                FacilityName = _name,
                FacilityClassification = _classification,
                FacilityLocation = _location,
                isActive = _isActive
            };


            _context.Facilities.Add(facility);
            _context.SaveChanges();

            return RedirectToAction("Index", "Facility");
        }
        /*Update*/

        [HttpPost]
        public IActionResult EditFacility(int _facilityId, string _facilityName, string _facilityLocation, string _facilityClassification, bool _isActive)
        {
            var facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == _facilityId);
            if (facility == null)
            {
                return NotFound("Reservation detail not found.");
            }
            facility.FacilityName = _facilityName;
            facility.FacilityLocation = _facilityLocation;
            facility.FacilityClassification = _facilityClassification;
            facility.isActive = _isActive;

            try
            {
                _context.Facilities.Update(facility);
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
            string redirectUrl = Url.Action("ViewFacility", "Facility", new { FacilityId = _facilityId });
            return Redirect(redirectUrl);
        }

        /*Delete*/

        [HttpPost]
        public IActionResult DeleteFacility(int FacilityId)
        {
            GFMS_Facility facility = _context.Facilities.Find(FacilityId);
            if (facility == null)
            {
                return NotFound();
            }
            _context.Facilities.Remove(facility);
            _context.SaveChanges();
            return RedirectToAction("Index", "Facility");
        }
    }
}
