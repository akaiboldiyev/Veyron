using Cyclop.Windows;

var tests = new (string Name, Action Body)[]
{
    ("Currency parser accepts locale-style values", CurrencyParserAcceptsLocaleStyleValues),
    ("Meeting links only allow supported HTTPS providers", MeetingLinksAreSafe),
    ("iCalendar recurrence honors moved instances", RecurrenceOverrideReplacesOriginal),
    ("iCalendar recurrence honors cancelled instances", CancelledOverrideRemovesOriginal),
    ("iCalendar recurrence honors EXDATE and RDATE", ExclusionAndAdditionalDatesAreApplied),
    ("iCalendar omits all-day events", AllDayEventsAreOmitted),
};

var failures = new List<string>();
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine($"PASS  {name}"); }
    catch (Exception error) { failures.Add($"FAIL  {name}: {error.Message}"); }
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static void CurrencyParserAcceptsLocaleStyleValues()
{
    EqualNumber(1234.56, CurrencyService.Parse("1 234,56"));
    EqualNumber(1234.56, CurrencyService.Parse("1,234.56"));
    EqualNumber(1234.56, CurrencyService.Parse("1.234,56"));
    if (CurrencyService.Parse("not a number") is not null) throw new InvalidOperationException("Invalid text was parsed as currency.");
}

static void MeetingLinksAreSafe()
{
    EqualText("https://meet.google.com/abc-defg-hij", ICalendarParser.FindMeetingLink("Join https://meet.google.com/abc-defg-hij."));
    if (ICalendarParser.FindMeetingLink("https://example.test/meeting") is not null) throw new InvalidOperationException("An unsupported provider was accepted.");
    if (ICalendarParser.FindMeetingLink("http://meet.google.com/abc-defg-hij") is not null) throw new InvalidOperationException("An insecure meeting link was accepted.");
}

static void RecurrenceOverrideReplacesOriginal()
{
    var start = DateTime.Now.Date.AddDays(1).AddHours(10);
    var replacement = start.AddDays(1).AddHours(1);
    var events = ICalendarParser.Parse($"""
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:weekly-demo
SUMMARY:Daily standup
DTSTART:{start:yyyyMMdd'T'HHmmss}
RRULE:FREQ=DAILY;COUNT=3
END:VEVENT
BEGIN:VEVENT
UID:weekly-demo
RECURRENCE-ID:{start.AddDays(1):yyyyMMdd'T'HHmmss}
SUMMARY:Moved standup
DTSTART:{replacement:yyyyMMdd'T'HHmmss}
END:VEVENT
END:VCALENDAR
""").OrderBy(item => item.StartsAt).ToArray();

    if (events.Length != 3) throw new InvalidOperationException($"Expected three occurrences, got {events.Length}: {string.Join("; ", events.Select(item => $"{item.Title}@{item.StartsAt:O}"))}.");
    if (events.Any(item => item.StartsAt == start.AddDays(1))) throw new InvalidOperationException("The original moved occurrence is still present.");
    if (!events.Any(item => item.StartsAt == replacement && item.Title == "Moved standup")) throw new InvalidOperationException("The moved occurrence is missing.");
}

static void CancelledOverrideRemovesOriginal()
{
    var start = DateTime.Now.Date.AddDays(1).AddHours(10);
    var events = ICalendarParser.Parse($"""
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:cancel-demo
SUMMARY:Daily standup
DTSTART:{start:yyyyMMdd'T'HHmmss}
RRULE:FREQ=DAILY;COUNT=3
END:VEVENT
BEGIN:VEVENT
UID:cancel-demo
RECURRENCE-ID:{start.AddDays(1):yyyyMMdd'T'HHmmss}
STATUS:CANCELLED
END:VEVENT
END:VCALENDAR
""").ToArray();

    if (events.Length != 2) throw new InvalidOperationException($"Expected two occurrences after cancellation, got {events.Length}: {string.Join("; ", events.Select(item => $"{item.Title}@{item.StartsAt:O}"))}.");
    if (events.Any(item => item.StartsAt == start.AddDays(1))) throw new InvalidOperationException("The cancelled occurrence is still present.");
}

static void ExclusionAndAdditionalDatesAreApplied()
{
    var start = DateTime.Now.Date.AddDays(1).AddHours(10);
    var excluded = start.AddDays(1);
    var additional = start.AddDays(3);
    var events = ICalendarParser.Parse($"""
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:exdate-demo
SUMMARY:Daily standup
DTSTART:{start:yyyyMMdd'T'HHmmss}
RRULE:FREQ=DAILY;COUNT=3
EXDATE:{excluded:yyyyMMdd'T'HHmmss}
RDATE:{additional:yyyyMMdd'T'HHmmss}
END:VEVENT
END:VCALENDAR
""").OrderBy(item => item.StartsAt).ToArray();

    if (events.Length != 3) throw new InvalidOperationException($"Expected three occurrences after EXDATE/RDATE, got {events.Length}.");
    if (events.Any(item => item.StartsAt == excluded)) throw new InvalidOperationException("The excluded occurrence is still present.");
    if (!events.Any(item => item.StartsAt == additional)) throw new InvalidOperationException("The additional occurrence is missing.");
}

static void AllDayEventsAreOmitted()
{
    var date = DateTime.Now.Date.AddDays(1);
    var events = ICalendarParser.Parse($"""
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:all-day-demo
SUMMARY:Holiday
DTSTART;VALUE=DATE:{date:yyyyMMdd}
DTEND;VALUE=DATE:{date.AddDays(1):yyyyMMdd}
END:VEVENT
BEGIN:VEVENT
UID:timed-demo
SUMMARY:Evening meeting
DTSTART:{date:yyyyMMdd}T180000
DTEND:{date:yyyyMMdd}T190000
END:VEVENT
END:VCALENDAR
""").ToArray();

    if (events.Length != 1 || events[0].Title != "Evening meeting")
        throw new InvalidOperationException($"Expected only the timed event, got {events.Length}: {string.Join("; ", events.Select(item => item.Title))}.");
}

static void EqualNumber(double expected, double? actual)
{
    if (actual is null || Math.Abs(expected - actual.Value) > .000001)
        throw new InvalidOperationException($"Expected {expected}, got {actual?.ToString() ?? "null"}.");
}

static void EqualText(string expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual ?? "null"}'.");
}
