namespace PropertyManagementSystem.Services
{
    public static class RoomCapacityPolicy
    {
        public const int MinimumCapacity = 1;
        public const int MaximumCapacity = 1000;

        public static bool IsValid(int capacity) =>
            capacity >= MinimumCapacity && capacity <= MaximumCapacity;

        public static string ValidationMessage =>
            $"Capacity must be a whole number from {MinimumCapacity:N0} to {MaximumCapacity:N0}.";
    }
}
