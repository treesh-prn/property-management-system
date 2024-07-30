using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Collections;
using System.Collections.Generic;

namespace PropertyManagementSystem.Controllers
{
    public class EquipmentController : Controller
    {
        private readonly DAL _context;

        public EquipmentController(DAL context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }

        public ActionResult getEquipments([DataSourceRequest] DataSourceRequest request)
        {
            List <GFMS_Equipments> _Equipments = _context.Equipments.AsQueryable().ToList();
            DataSourceResult result = _Equipments.ToDataSourceResult(request, _Equipments => new
            {
                Id = _Equipments.Id,
                EquipmentName = _Equipments.EquipmentName,
                EquipmentQty = _Equipments.EquipmentQty,
                PurchaseCost = _Equipments.PurchaseCost,
                WholeDayCost = _Equipments.WholeDayCost,
                HalfDayCost = _Equipments.HalfDayCost,
                Remarks = _Equipments.Remarks,
                isActive = _Equipments.isActive,
            });
            return Json(result);
        }
        public ActionResult newEquipments(string _name, int _quantity, int _purCost, int _halfCost, int _wholeCost, string _remarks, bool _isActive)
        {
            GFMS_Equipments equipments = new GFMS_Equipments()
            {
                EquipmentName = _name,
                EquipmentQty = _quantity,
                PurchaseCost = _purCost,
                HalfDayCost = _halfCost,
                WholeDayCost = _wholeCost,
                Remarks = _remarks,
                isActive = _isActive,

            };
            _context.Equipments.Add(equipments);
            _context.SaveChanges();
            return RedirectToAction("Index", "Equipment");
        }

        public IActionResult ViewEquipment(int Id)
        {
            var equipmentDetail = _context.Equipments.FirstOrDefault(e => e.Id == Id);
            if(equipmentDetail == null)
            {
                return NotFound();
            }
            return View(equipmentDetail);
        }
        /*Update*/
        public IActionResult EditEquipment(int Id, string _name, int _quantity, int _purCost, int _halfCost, int _wholeCost, string _remarks, bool _isActive)
        {
            var equipment = _context.Equipments.FirstOrDefault(e => e.Id == Id);
            if (equipment == null)
            {
                return NotFound("Equipment not found");
            }

            equipment.EquipmentName = _name;
            equipment.EquipmentQty = _quantity;
            equipment.PurchaseCost = _purCost;
            equipment.HalfDayCost = _halfCost;
            equipment.WholeDayCost = _wholeCost;
            equipment.Remarks = _remarks;
            equipment.isActive = _isActive;
            try
            {
                _context.Equipments.Update(equipment);
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
            string redirectUrl = Url.Action("ViewEquipment", "Equipment", new { Id = Id });
            return Redirect(redirectUrl);
        }
        /*Delete*/

        [HttpPost]
        public IActionResult DeleteEquipment(int Id)
        {
            GFMS_Equipments equipments= _context.Equipments.Find(Id);
            if (equipments == null)
            {
                return NotFound();
            }
            _context.Equipments.Remove(equipments);
            _context.SaveChanges();
            return RedirectToAction("Index", "Equipment");
        }
    }
}