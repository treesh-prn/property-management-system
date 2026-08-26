using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class WeeklyRecurrenceExpanderTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 9, 0, 0);
    private static readonly DateTime End = new(2026, 8, 20, 11, 30, 0);

    [Fact]
    public void EmptyRule_ReturnsTheSingleRequestedSchedule()
    {
        var result = WeeklyRecurrenceExpander.Expand(Start, End, null);

        Assert.True(result.Succeeded);
        var occurrence = Assert.Single(result.Occurrences);
        Assert.Equal(Start, occurrence.Start);
        Assert.Equal(End, occurrence.End);
    }

    [Fact]
    public void WeeklyCount_PreservesTimeAndDuration()
    {
        var result = WeeklyRecurrenceExpander.Expand(
            Start,
            End,
            "FREQ=WEEKLY;INTERVAL=1;BYDAY=TH;COUNT=4");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            new[]
            {
                new DateTime(2026, 8, 20, 9, 0, 0),
                new DateTime(2026, 8, 27, 9, 0, 0),
                new DateTime(2026, 9, 3, 9, 0, 0),
                new DateTime(2026, 9, 10, 9, 0, 0)
            },
            result.Occurrences.Select(item => item.Start));
        Assert.All(result.Occurrences, item => Assert.Equal(TimeSpan.FromHours(2.5), item.End - item.Start));
    }

    [Fact]
    public void MultipleWeekdays_AlwaysIncludesTheRequestedStartAsFirstOccurrence()
    {
        var result = WeeklyRecurrenceExpander.Expand(
            Start,
            End,
            "FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4;WKST=SU");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Start, result.Occurrences[0].Start);
        Assert.Equal(new DateTime(2026, 8, 24, 9, 0, 0), result.Occurrences[1].Start);
        Assert.Equal(new DateTime(2026, 8, 26, 9, 0, 0), result.Occurrences[2].Start);
        Assert.Equal(new DateTime(2026, 8, 31, 9, 0, 0), result.Occurrences[3].Start);
    }

    [Fact]
    public void WeeklyUntil_IncludesOccurrencesOverlappingTheSelectedEndDate()
    {
        var result = WeeklyRecurrenceExpander.Expand(
            Start,
            End,
            "FREQ=WEEKLY;BYDAY=TH;UNTIL=20260903T000000Z");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Occurrences.Count);
        Assert.Equal(new DateTime(2026, 9, 3, 9, 0, 0), result.Occurrences[^1].Start);
    }

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=4", "Only weekly")]
    [InlineData("FREQ=WEEKLY;BYDAY=TH", "end date or a number")]
    [InlineData("FREQ=WEEKLY;BYDAY=TH;COUNT=53", "limited to 52")]
    [InlineData("FREQ=WEEKLY;INTERVAL=0;COUNT=4", "from 1 to 52")]
    public void InvalidOrUnsafeRule_IsRejected(string rule, string expectedMessage)
    {
        var result = WeeklyRecurrenceExpander.Expand(Start, End, rule);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedMessage, result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public void DurationThatOverlapsTheNextWeek_IsRejected()
    {
        var result = WeeklyRecurrenceExpander.Expand(
            Start,
            Start.AddDays(8),
            "FREQ=WEEKLY;BYDAY=TH;COUNT=2");

        Assert.False(result.Succeeded);
        Assert.Contains("overlaps", result.Message!, StringComparison.OrdinalIgnoreCase);
    }
}
