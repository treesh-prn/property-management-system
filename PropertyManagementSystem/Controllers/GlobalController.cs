using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.IO;
using System.Security.Claims;

namespace PropertyManagementSystem.Controllers
{
    public class GlobalController : Controller
    {
        private readonly DAL _context;

        public GlobalController(DAL context)
        {

            _context = context;

        }
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult getCompanies()
        {
            var _companyList = _context.Companies.AsQueryable().ToList();

            return Json(_companyList , new JsonSerializerSettings());
        }
        public string GenerateId()
        {
            //{00000000-0000-0000-0000-000000000000}
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";
            return unique;
        }

        public JsonResult getDepartments()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var _deptList = _context.Departments.AsQueryable().ToList();

            return Json(_deptList , new JsonSerializerSettings());
        }

        public ActionResult GetEquipments()
        {
            List<GFMS_Equipments> _Equipments = _context.Equipments.AsQueryable().ToList();
            List<equipmentList> _eqList = new List<equipmentList>();
            foreach(var item in _Equipments)
            {
                _eqList.Add(new equipmentList
                {
                    EquipmentId = item.EquipmentId,
                    EquipmentName = item.EquipmentName,
                });
            }
            return Json(_eqList);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CancelReservation(int ReservationId, string Remarks)
        {
            var reservation = _context.ReservationHeaders.FirstOrDefault(r => r.ReservationId == ReservationId);
            if (reservation == null)
            {
                return NotFound();
            }
            else
            {
                reservation.Status = "CANCELLED";
                reservation.Remarks = Remarks;
                _context.SaveChanges();
            }

            return RedirectToAction("ReservationList","FunctionRoom");
        }
    }
}
