using System.Globalization;

namespace PropertyManagementSystem.Services
{
    public sealed record ReservationOccurrence(DateTime Start, DateTime End);

    public sealed record WeeklyRecurrenceExpansionResult(
        bool Succeeded,
        IReadOnlyList<ReservationOccurrence> Occurrences,
        string? Message = null)
    {
        public static WeeklyRecurrenceExpansionResult Ok(IReadOnlyList<ReservationOccurrence> occurrences) =>
            new(true, occurrences);

        public static WeeklyRecurrenceExpansionResult Fail(string message) =>
            new(false, Array.Empty<ReservationOccurrence>(), message);
    }

    /// <summary>
    /// Expands the bounded weekly iCalendar rule emitted by Kendo's RecurrenceEditor.
    /// The resulting dates are persisted as ordinary reservation detail rows so every
    /// occurrence participates in availability and equipment validation.
    /// </summary>
    public static class WeeklyRecurrenceExpander
    {
        private static readonly IReadOnlyDictionary<string, DayOfWeek> Days =
            new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
            {
                ["SU"] = DayOfWeek.Sunday,
                ["MO"] = DayOfWeek.Monday,
                ["TU"] = DayOfWeek.Tuesday,
                ["WE"] = DayOfWeek.Wednesday,
                ["TH"] = DayOfWeek.Thursday,
                ["FR"] = DayOfWeek.Friday,
                ["SA"] = DayOfWeek.Saturday
            };

        public static WeeklyRecurrenceExpansionResult Expand(
            DateTime start,
            DateTime end,
            string? recurrenceRule,
            int maxOccurrences = 52)
        {
            if (end <= start)
                return WeeklyRecurrenceExpansionResult.Fail(
                    "End date and time must be later than start date and time.");

            if (string.IsNullOrWhiteSpace(recurrenceRule))
                return WeeklyRecurrenceExpansionResult.Ok(new[] { new ReservationOccurrence(start, end) });

            if (maxOccurrences < 1)
                throw new ArgumentOutOfRangeException(nameof(maxOccurrences));

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in recurrenceRule.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0 || separator == part.Length - 1)
                    return WeeklyRecurrenceExpansionResult.Fail("The recurrence settings are invalid.");

                values[part[..separator].Trim()] = part[(separator + 1)..].Trim();
            }

            if (!values.TryGetValue("FREQ", out var frequency) ||
                !string.Equals(frequency, "WEEKLY", StringComparison.OrdinalIgnoreCase))
                return WeeklyRecurrenceExpansionResult.Fail("Only weekly recurrence is supported.");

            var interval = 1;
            if (values.TryGetValue("INTERVAL", out var intervalValue) &&
                (!int.TryParse(intervalValue, NumberStyles.None, CultureInfo.InvariantCulture, out interval) ||
                 interval < 1 || interval > 52))
                return WeeklyRecurrenceExpansionResult.Fail("The weekly interval must be from 1 to 52 weeks.");

            int? count = null;
            if (values.TryGetValue("COUNT", out var countValue))
            {
                if (!int.TryParse(countValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCount) ||
                    parsedCount < 1)
                    return WeeklyRecurrenceExpansionResult.Fail("The occurrence count must be a positive whole number.");
                count = parsedCount;
            }

            DateTime? until = null;
            if (values.TryGetValue("UNTIL", out var untilValue))
            {
                var formats = new[] { "yyyyMMdd", "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss" };
                if (!DateTime.TryParseExact(
                        untilValue,
                        formats,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                        out var parsedUntil))
                    return WeeklyRecurrenceExpansionResult.Fail("The recurrence end date is invalid.");
                until = parsedUntil;
            }

            if (!count.HasValue && !until.HasValue)
                return WeeklyRecurrenceExpansionResult.Fail(
                    "Choose an end date or a number of occurrences for the weekly recurrence.");

            if (count > maxOccurrences)
                return WeeklyRecurrenceExpansionResult.Fail(
                    $"A recurring reservation is limited to {maxOccurrences} occurrences.");

            var selectedDays = new HashSet<DayOfWeek>();
            if (values.TryGetValue("BYDAY", out var byDayValue))
            {
                foreach (var token in byDayValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Days.TryGetValue(token.Trim(), out var day))
                        return WeeklyRecurrenceExpansionResult.Fail("One or more selected weekdays are invalid.");
                    selectedDays.Add(day);
                }
            }
            else
            {
                selectedDays.Add(start.DayOfWeek);
            }

            if (selectedDays.Count == 0)
                return WeeklyRecurrenceExpansionResult.Fail("Select at least one weekday for the recurrence.");

            var duration = end - start;
            var occurrences = new List<ReservationOccurrence>();
            if (!until.HasValue || start.Date <= until.Value.Date)
                occurrences.Add(new ReservationOccurrence(start, end));
            if (count == 1 && occurrences.Count == 1)
                return WeeklyRecurrenceExpansionResult.Ok(occurrences);

            var dayCursor = start.Date.AddDays(1);
            var firstDayOfWeek = DayOfWeek.Monday;
            if (values.TryGetValue("WKST", out var weekStartValue) &&
                (!Days.TryGetValue(weekStartValue, out firstDayOfWeek)))
                return WeeklyRecurrenceExpansionResult.Fail("The recurrence week start is invalid.");
            // Kendo's weekly editor uses the week containing DTSTART as recurrence week zero.
            var weekStart = StartOfWeek(start.Date, firstDayOfWeek);
            DateTime safetyEnd;
            try
            {
                safetyEnd = until?.Date ?? start.Date.AddDays(interval * 7d * (count ?? maxOccurrences) + 7);
            }
            catch (ArgumentOutOfRangeException)
            {
                return WeeklyRecurrenceExpansionResult.Fail("The recurrence exceeds the supported date range.");
            }

            while (dayCursor <= safetyEnd)
            {
                var elapsedWeeks = (int)((StartOfWeek(dayCursor, firstDayOfWeek) - weekStart).TotalDays / 7);
                if (elapsedWeeks >= 0 && elapsedWeeks % interval == 0 && selectedDays.Contains(dayCursor.DayOfWeek))
                {
                    var occurrenceStart = dayCursor.Add(start.TimeOfDay);
                    if (!until.HasValue || occurrenceStart.Date <= until.Value.Date)
                    {
                        occurrences.Add(new ReservationOccurrence(occurrenceStart, occurrenceStart.Add(duration)));
                        if (occurrences.Count > maxOccurrences)
                            return WeeklyRecurrenceExpansionResult.Fail(
                                $"A recurring reservation is limited to {maxOccurrences} occurrences.");
                        if (count.HasValue && occurrences.Count == count.Value)
                            break;
                    }
                }

                dayCursor = dayCursor.AddDays(1);
            }

            if (occurrences.Count == 0 || (count.HasValue && occurrences.Count != count.Value))
                return WeeklyRecurrenceExpansionResult.Fail("The recurrence does not produce a valid schedule.");

            for (var index = 1; index < occurrences.Count; index++)
            {
                if (occurrences[index - 1].End > occurrences[index].Start)
                    return WeeklyRecurrenceExpansionResult.Fail(
                        "The reservation duration overlaps its next recurring occurrence.");
            }

            return WeeklyRecurrenceExpansionResult.Ok(occurrences);
        }

        private static DateTime StartOfWeek(DateTime date, DayOfWeek firstDay)
        {
            var difference = (7 + (date.DayOfWeek - firstDay)) % 7;
            return date.AddDays(-difference).Date;
        }
    }
}
