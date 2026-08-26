using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class RoomCapacityPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(int.MaxValue)]
    public void InvalidCapacity_IsRejected(int capacity)
    {
        Assert.False(RoomCapacityPolicy.IsValid(capacity));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(1000)]
    public void SupportedCapacity_IsAccepted(int capacity)
    {
        Assert.True(RoomCapacityPolicy.IsValid(capacity));
    }
}
