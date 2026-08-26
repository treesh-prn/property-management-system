namespace PropertyManagementSystem.Services
{
    public static class DormHousingApplicationFeature
    {
        // Dorm and Housing application filing is intentionally unavailable until
        // the workflow is ready for release. Existing records and admin occupancy
        // tools remain accessible.
        public const bool IsEnabled = false;
        public const string UnavailableMessage =
            "Dorm and Housing applications are not available yet. This service will be enabled in a future release.";
    }
}
