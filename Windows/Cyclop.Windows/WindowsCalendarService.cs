using Windows.ApplicationModel.Appointments;

namespace Cyclop.Windows;

internal sealed class WindowsCalendarService
{
    private AppointmentStore? store;
    public static WindowsCalendarService Current{get;}=new();
    public bool IsConnected=>store is not null;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            store=await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            return store is not null;
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop Windows calendar permission: "+ex.Message);return false;}
    }
    public async Task<WindowsCalendarSnapshot> ReadUpcomingAsync(IReadOnlyDictionary<string,bool> visibilityOverrides)
    {
        if(store is null)return new([],[]);
        var calendars=await store.FindAppointmentCalendarsAsync();
        var options=new FindAppointmentsOptions();
        options.FetchProperties.Add(AppointmentProperties.Subject);
        options.FetchProperties.Add(AppointmentProperties.Details);
        options.FetchProperties.Add(AppointmentProperties.Location);
        options.FetchProperties.Add(AppointmentProperties.OnlineMeetingLink);
        options.FetchProperties.Add(AppointmentProperties.AllDay);
        options.FetchProperties.Add(AppointmentProperties.IsCanceledMeeting);
        var calendarOptions=new List<WindowsCalendarOption>();
        var events=new List<CalendarEvent>();
        foreach(var calendar in calendars)
        {
            var shown=visibilityOverrides.TryGetValue(calendar.LocalId,out var overrideShown)?overrideShown:!calendar.IsHidden;
            calendarOptions.Add(new WindowsCalendarOption(calendar.LocalId,calendar.DisplayName,shown));
            if(!shown)continue;
            var appointments=await calendar.FindAppointmentsAsync(DateTimeOffset.Now,TimeSpan.FromDays(7),options);
            foreach(var item in appointments)
            {
                if(item.AllDay||item.IsCanceledMeeting)continue;
                var location=item.Location??"";var details=item.Details??"";
                var meeting=ICalendarParser.FindMeetingLink((item.OnlineMeetingLink??"")+" "+details+" "+location);
                var starts=item.StartTime.LocalDateTime;
                events.Add(new CalendarEvent(item.Subject??"Встреча",starts,meeting,location){CalendarName=calendar.DisplayName,CalendarId=calendar.LocalId,EndsAt=starts+item.Duration});
            }
        }
        return new WindowsCalendarSnapshot(calendarOptions.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToArray(),events.OrderBy(x=>x.StartsAt).ToArray());
    }
}
