using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public interface IFacilityEquipmentLinkService
    {
        List<FacilityEquipmentLinkOption> GetOptions(int? facilityId = null);
        string? Validate(int facilityId, IEnumerable<FacilityEquipmentLinkInput>? inputs);
        void ReplaceFacilityLinks(int facilityId, IEnumerable<FacilityEquipmentLinkInput>? inputs);
        void SyncLegacyEquipmentLink(GFMS_Equipments equipment, string linkType, int defaultQuantity, bool isFixed);
    }

    public sealed class FacilityEquipmentLinkService : IFacilityEquipmentLinkService
    {
        private readonly DAL _context;

        public FacilityEquipmentLinkService(DAL context) => _context = context;

        public List<FacilityEquipmentLinkOption> GetOptions(int? facilityId = null)
        {
            var links = facilityId.HasValue
                ? _context.FacilityEquipmentLinks.AsNoTracking()
                    .Where(link => link.FacilityId == facilityId.Value && !link.RoomId.HasValue)
                    .ToDictionary(link => link.EquipmentId)
                : new Dictionary<int, GFMS_FacilityEquipmentLink>();

            return _context.Equipments.AsNoTracking()
                .Where(equipment => equipment.isActive != false || links.Keys.Contains(equipment.Id))
                .OrderBy(equipment => equipment.EquipmentName)
                .AsEnumerable()
                .Select(equipment =>
                {
                    links.TryGetValue(equipment.Id, out var link);
                    return new FacilityEquipmentLinkOption
                    {
                        EquipmentId = equipment.Id,
                        EquipmentName = equipment.EquipmentName ?? $"Equipment {equipment.Id}",
                        AvailableQuantity = Math.Max(equipment.EquipmentQty ?? 0, 0),
                        Selected = link?.IsActive == true,
                        DefaultQuantity = link?.DefaultQuantity ?? 1,
                        LinkType = link?.LinkType ?? EquipmentLinkTypes.Included,
                        IsFixed = link?.IsFixed ?? false
                    };
                })
                .ToList();
        }

        public string? Validate(int facilityId, IEnumerable<FacilityEquipmentLinkInput>? inputs)
        {
            var selected = (inputs ?? Array.Empty<FacilityEquipmentLinkInput>())
                .Where(input => input.Selected)
                .ToList();
            if (selected.GroupBy(input => input.EquipmentId).Any(group => group.Count() > 1))
                return "The same equipment cannot be linked more than once.";

            var equipmentIds = selected.Select(input => input.EquipmentId).ToList();
            var equipment = _context.Equipments.AsNoTracking()
                .Where(item => equipmentIds.Contains(item.Id))
                .ToDictionary(item => item.Id);
            if (equipment.Count != equipmentIds.Count)
                return "One or more selected equipment records no longer exist.";

            foreach (var input in selected)
            {
                var item = equipment[input.EquipmentId];
                if (item.isActive == false)
                    return $"{item.EquipmentName} is inactive and cannot be linked.";
                if (input.DefaultQuantity < 1 || input.DefaultQuantity > Math.Max(item.EquipmentQty ?? 0, 0))
                    return $"The linked quantity for {item.EquipmentName} must be between 1 and its available inventory.";
                if (input.LinkType is not EquipmentLinkTypes.Included and not EquipmentLinkTypes.AddOn)
                    return "Choose a valid equipment relationship.";
                if (input.IsFixed && _context.FacilityEquipmentLinks.AsNoTracking().Any(link =>
                        link.EquipmentId == input.EquipmentId && link.FacilityId != facilityId &&
                        link.IsFixed && link.IsActive))
                    return $"{item.EquipmentName} is fixed to another facility or room.";
            }
            return null;
        }

        public void ReplaceFacilityLinks(int facilityId, IEnumerable<FacilityEquipmentLinkInput>? inputs)
        {
            var selected = (inputs ?? Array.Empty<FacilityEquipmentLinkInput>())
                .Where(input => input.Selected)
                .ToDictionary(input => input.EquipmentId);
            var existing = _context.FacilityEquipmentLinks
                .Where(link => link.FacilityId == facilityId && !link.RoomId.HasValue)
                .ToList();

            foreach (var link in existing)
            {
                if (!selected.TryGetValue(link.EquipmentId, out var input))
                {
                    _context.FacilityEquipmentLinks.Remove(link);
                    continue;
                }
                Apply(link, input);
                selected.Remove(link.EquipmentId);
            }

            foreach (var input in selected.Values)
            {
                var link = new GFMS_FacilityEquipmentLink
                {
                    FacilityId = facilityId,
                    EquipmentId = input.EquipmentId,
                    RoomId = null
                };
                Apply(link, input);
                _context.FacilityEquipmentLinks.Add(link);
            }
        }

        public void SyncLegacyEquipmentLink(
            GFMS_Equipments equipment,
            string linkType,
            int defaultQuantity,
            bool isFixed)
        {
            var existing = _context.FacilityEquipmentLinks
                .Where(link => link.EquipmentId == equipment.Id)
                .ToList();
            if (equipment.isFacilityEquipment != true || !equipment.FacilityId.HasValue)
            {
                _context.FacilityEquipmentLinks.RemoveRange(existing);
                return;
            }

            var link = existing.FirstOrDefault(item =>
                item.FacilityId == equipment.FacilityId && item.RoomId == equipment.RoomId);
            foreach (var stale in existing.Where(item => item != link))
                _context.FacilityEquipmentLinks.Remove(stale);

            link ??= new GFMS_FacilityEquipmentLink
            {
                FacilityId = equipment.FacilityId.Value,
                EquipmentId = equipment.Id,
                RoomId = equipment.RoomId
            };
            link.DefaultQuantity = Math.Clamp(defaultQuantity, 1, Math.Max(equipment.EquipmentQty ?? 1, 1));
            link.LinkType = linkType is EquipmentLinkTypes.AddOn ? EquipmentLinkTypes.AddOn : EquipmentLinkTypes.Included;
            link.IsFixed = isFixed || equipment.RoomId.HasValue;
            link.IsActive = true;
            if (link.FacilityEquipmentLinkId == 0)
                _context.FacilityEquipmentLinks.Add(link);
        }

        private static void Apply(GFMS_FacilityEquipmentLink link, FacilityEquipmentLinkInput input)
        {
            link.DefaultQuantity = input.DefaultQuantity;
            link.LinkType = input.LinkType;
            link.IsFixed = input.IsFixed;
            link.IsActive = true;
        }
    }
}
