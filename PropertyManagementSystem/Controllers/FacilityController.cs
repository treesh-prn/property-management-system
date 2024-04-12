using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Collections;
using System.Collections.Generic;

namespace PropertyManagementSystem.Controllers
{
    public class FacilityController : Controller
    {
        private readonly DAL _context;

        public FacilityController(DAL context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }

        public ActionResult getFacilities([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.AsQueryable().ToList();
            DataSourceResult result = _Facilities.ToDataSourceResult(request, _Facility => new
            {
                FacilityId = _Facility.FacilityId,
                FacilityName = _Facility.FacilityName,
                FacilityLocation = _Facility.FacilityLocation,
                isActive = _Facility.isActive
            });
            return Json(result);

        }
        public ActionResult getFacilityList()
        {
            List<GFMS_Facility> _Facilities = _context.Facilities.AsQueryable().ToList();

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

        public IActionResult newFacility(string _name, string _location, bool _isActive ) 
        {
            GFMS_Facility facility = new GFMS_Facility()
            {
                FacilityName = _name,
                FacilityLocation = _location,
                isActive = _isActive
            };


            _context.Facilities.Add( facility );
            _context.SaveChanges();

            return RedirectToAction("Index", "Facility");
        }
    }


}
