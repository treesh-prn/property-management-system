using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class ReservationOwnership
    {
        public static bool IsOwner(
            GFMS_ReservationHeader reservation,
            params string?[] candidateEmails)
        {
            if (string.IsNullOrWhiteSpace(reservation.RequestorEmail))
            {
                return false;
            }

            return candidateEmails.Any(email =>
                !string.IsNullOrWhiteSpace(email) &&
                string.Equals(
                    reservation.RequestorEmail.Trim(),
                    email.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
