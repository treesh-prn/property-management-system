namespace PropertyManagementSystem.Services
{
    public static class DormHousingEligibilityPolicy
    {
        public const int DormUnit = 0;
        public const int HousingUnit = 1;

        public static string NormalizeCivilStatus(string? civilStatus) =>
            (civilStatus ?? string.Empty).Trim().ToUpperInvariant();

        public static bool CanApplyForUnit(string? civilStatus, int appliedUnit)
        {
            if (appliedUnit == DormUnit)
            {
                return true;
            }

            return appliedUnit == HousingUnit &&
                NormalizeCivilStatus(civilStatus) == "MARRIED";
        }

        public static string GetCivilStatusLabel(string? civilStatus)
        {
            var normalized = NormalizeCivilStatus(civilStatus);
            return string.IsNullOrWhiteSpace(normalized)
                ? "Not specified"
                : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                    normalized.ToLowerInvariant());
        }
    }
}
