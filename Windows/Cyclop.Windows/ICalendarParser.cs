using System.Text;
using System.Text.RegularExpressions;

namespace Cyclop.Windows;

internal static partial class ICalendarParser
{
    public static IEnumerable<CalendarEvent> Read(string path)=>Parse(File.ReadAllText(path,Encoding.UTF8));

    public static IEnumerable<CalendarEvent> Parse(string raw)
    {
        var normalized=raw.Replace("\r\n ","").Replace("\n ","").Replace("\r\n\t","").Replace("\n\t","");
        var lines=normalized.Split(["\r\n","\n"],StringSplitOptions.None);
        var current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var components=new List<Dictionary<string,string>>();
        foreach(var line in lines)
        {
            if(line.Equals("BEGIN:VEVENT",StringComparison.OrdinalIgnoreCase)){current.Clear();continue;}
            if(line.Equals("END:VEVENT",StringComparison.OrdinalIgnoreCase))
            {
                components.Add(new Dictionary<string,string>(current,StringComparer.OrdinalIgnoreCase));
                current.Clear();continue;
            }
            var colon=line.IndexOf(':');if(colon<1)continue;
            var header=line[..colon];var key=header.Split(';')[0].ToUpperInvariant();
            if(key is "SUMMARY" or "DESCRIPTION" or "LOCATION" or "URL" or "RRULE" or "DURATION" or "STATUS" or "UID" or "RECURRENCE-ID")
                current[key]=current.TryGetValue(key,out var previousRule)&&key=="RRULE"?previousRule+"\n"+line[(colon+1)..]:line[(colon+1)..];
            else if(key is "DTSTART" or "DTEND")current[header]=line[(colon+1)..];
            else if(key is "EXDATE" or "RDATE")
            {
                var value=line[(colon+1)..];
                current[header]=current.TryGetValue(header,out var existing)?existing+","+value:value;
            }
        }

        // A cancelled or moved RECURRENCE-ID component replaces one instance
        // in its UID series. Remove that original slot before emitting the
        // replacement below, just as EXDATE does for an explicitly excluded
        // instance. Orphan overrides remain harmless if their master is absent.
        var overridden=components.Where(x=>!string.IsNullOrWhiteSpace(Value(x,"RECURRENCE-ID","")))
            .Where(x=>!string.IsNullOrWhiteSpace(Value(x,"UID","")))
            .Select(x=>{var item=x.First(kv=>kv.Key.StartsWith("RECURRENCE-ID",StringComparison.OrdinalIgnoreCase));return(Uid:Value(x,"UID",""),Date:TryDate(item.Value,item.Key,out var date)?(DateTime?)date:null);})
            .Where(x=>x.Date.HasValue).GroupBy(x=>x.Uid,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.Select(v=>v.Date!.Value).ToHashSet(),StringComparer.Ordinal);

        foreach(var component in components)
        {
            var cancelled=string.Equals(Value(component,"STATUS",""),"CANCELLED",StringComparison.OrdinalIgnoreCase);
            var recurrenceId=component.FirstOrDefault(x=>x.Key.StartsWith("RECURRENCE-ID",StringComparison.OrdinalIgnoreCase));
            var isOverride=recurrenceId.Key is not null;
            if(cancelled)continue;
            var start=component.FirstOrDefault(x=>x.Key.StartsWith("DTSTART",StringComparison.OrdinalIgnoreCase));
            if(start.Key is null||!TryDate(start.Value,start.Key,out var starts))continue;
            // Match the native calendar pane: all-day events are not meetings.
            // iCalendar represents them with VALUE=DATE (or a bare YYYYMMDD),
            // and showing them as midnight meetings makes the next-meeting
            // card misleading for the entire day.
            if(start.Value.Length==8||start.Key.Contains("VALUE=DATE",StringComparison.OrdinalIgnoreCase))continue;
            foreach(var item in ReadComponent(component,starts,isOverride?[]:Value(component,"RRULE","").Split('\n',StringSplitOptions.RemoveEmptyEntries)))
            {
                if(!isOverride&&overridden.TryGetValue(Value(component,"UID",""),out var dates)&&dates.Contains(item.StartsAt))continue;
                yield return item;
            }
        }
    }

    private static IEnumerable<CalendarEvent> ReadComponent(Dictionary<string,string> component,DateTime starts,string[] rules)
    {
        var title=Unescape(Value(component,"SUMMARY","Встреча"));
        var description=Unescape(Value(component,"DESCRIPTION",""));
        var location=Unescape(Value(component,"LOCATION",""));
        var url=FindMeetingLink(Value(component,"URL","")+" "+description+" "+location);
        var duration=TimeSpan.Zero;
        var end=component.FirstOrDefault(x=>x.Key.StartsWith("DTEND",StringComparison.OrdinalIgnoreCase));
        if(end.Key is not null&&TryDate(end.Value,end.Key,out var ends))duration=ends-starts;
        else if(!string.IsNullOrWhiteSpace(Value(component,"DURATION","")))
        {
            try{duration=System.Xml.XmlConvert.ToTimeSpan(Value(component,"DURATION",""));}catch(FormatException){ }
        }
        if(duration<TimeSpan.Zero)duration=TimeSpan.Zero;
        var exclusions=component.Where(x=>x.Key.StartsWith("EXDATE",StringComparison.OrdinalIgnoreCase))
            .SelectMany(x=>x.Value.Split(',',StringSplitOptions.RemoveEmptyEntries)
                .Select(value=>TryDate(value,x.Key,out var excluded)?(DateTime?)excluded:null))
            .Where(x=>x.HasValue).Select(x=>x!.Value).ToHashSet();
        var occurrences=rules.Length==0?ExpandRecurrence(starts,""):rules.SelectMany(rule=>ExpandRecurrence(starts,rule));
        var additional=component.Where(x=>x.Key.StartsWith("RDATE",StringComparison.OrdinalIgnoreCase))
            .SelectMany(x=>x.Value.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(value=>TryDate(value,x.Key,out var date)?(DateTime?)date:null))
            .Where(x=>x.HasValue).Select(x=>x!.Value);
        foreach(var occurrence in occurrences.Concat(additional).Distinct().Order())
            if(!exclusions.Contains(occurrence))yield return new CalendarEvent(title,occurrence,url,location){EndsAt=occurrence+duration};
    }

    private static bool TryDate(string value,string key,out DateTime date)
    {
        var formats=new[]{"yyyyMMdd'T'HHmmss'Z'","yyyyMMdd'T'HHmmss","yyyyMMdd'T'HHmm'Z'","yyyyMMdd'T'HHmm","yyyyMMdd"};
        var styles=value.EndsWith('Z')?System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal:System.Globalization.DateTimeStyles.None;
        if(DateTime.TryParseExact(value,formats,System.Globalization.CultureInfo.InvariantCulture,styles,out date))
        {
            if(value.EndsWith('Z'))date=DateTime.SpecifyKind(date,DateTimeKind.Utc).ToLocalTime();
            else
            {
                var zone=key.Split(';').FirstOrDefault(x=>x.StartsWith("TZID=",StringComparison.OrdinalIgnoreCase))?[5..];
                if(!string.IsNullOrWhiteSpace(zone))
                {
                    try{date=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById(zone)).ToLocalTime();}
                    catch(TimeZoneNotFoundException){date=DateTime.SpecifyKind(date,DateTimeKind.Local);}
                }
                else date=DateTime.SpecifyKind(date,DateTimeKind.Local);
            }
            return true;
        }
        return DateTime.TryParse(value,out date);
    }
    private static string Unescape(string text)=>text.Replace("\\n","\n").Replace("\\N","\n").Replace("\\,",",").Replace("\\;",";").Replace("\\\\","\\");
    private static string Value(Dictionary<string,string> values,string key,string fallback)=>values.FirstOrDefault(x=>x.Key.StartsWith(key,StringComparison.OrdinalIgnoreCase)).Value??fallback;
    private static IEnumerable<DateTime> ExpandRecurrence(DateTime start,string rule)
    {
        var now=DateTime.Now;var end=now.AddDays(7);
        if(string.IsNullOrWhiteSpace(rule)){if(start>=now&&start<end)yield return start;yield break;}
        var values=rule.Split(';').Select(x=>x.Split('=',2)).Where(x=>x.Length==2).ToDictionary(x=>x[0].ToUpperInvariant(),x=>x[1],StringComparer.OrdinalIgnoreCase);
        var frequency=values.GetValueOrDefault("FREQ","").ToUpperInvariant();
        if(!int.TryParse(values.GetValueOrDefault("INTERVAL","1"),out var interval)||interval<1)interval=1;
        DateTime? until=null;if(values.TryGetValue("UNTIL",out var untilText)&&TryDate(untilText,"DTSTART",out var parsedUntil))until=parsedUntil;
        var byday=values.TryGetValue("BYDAY",out var bydayText)?bydayText.Split(',').Select(ParseByDay).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray():[];
        var bymonthdays=ParseIntegerList(values.GetValueOrDefault("BYMONTHDAY",""));
        var bymonths=ParseIntegerList(values.GetValueOrDefault("BYMONTH",""));
        var bysetpos=ParseIntegerList(values.GetValueOrDefault("BYSETPOS",""));
        var weekStart=values.TryGetValue("WKST",out var wkst)?ParseWeekday(wkst)??DayOfWeek.Monday:DayOfWeek.Monday;
        var firstWeek=WeekStart(start.Date,weekStart);
        var maxCount=int.TryParse(values.GetValueOrDefault("COUNT"),out var parsedCount)&&parsedCount>0?parsedCount:int.MaxValue;
        for(var date=now.Date;date<=end.Date;date=date.AddDays(1))
        {
            var candidate=date.Add(start.TimeOfDay);
            if(candidate<start)continue;
            if(until.HasValue&&candidate>until.Value)yield break;
            if(!IsOccurrenceDate(date,start,frequency,interval,weekStart,firstWeek,byday,bymonthdays,bymonths,bysetpos))continue;
            // COUNT applies from DTSTART, including occurrences before the visible
            // seven-day window, so count the series from its anchor date.
            var occurrenceIndex=1;
            for(var prior=start.Date;prior<date;prior=prior.AddDays(1))
                if(IsOccurrenceDate(prior,start,frequency,interval,weekStart,firstWeek,byday,bymonthdays,bymonths,bysetpos))occurrenceIndex++;
            if(occurrenceIndex>maxCount)yield break;
            if(candidate>=now&&candidate<end)yield return candidate;
        }
    }
    private static bool IsOccurrenceDate(DateTime date,DateTime start,string frequency,int interval,DayOfWeek weekStart,DateTime firstWeek,ByDay[] byday,int[] bymonthdays,int[] bymonths,int[] bysetpos)
    {
        if(!MatchesRecurrenceDate(date,start,frequency,interval,weekStart,firstWeek,byday,bymonthdays,bymonths))return false;
        if(bysetpos.Length==0)return true;
        DateTime periodStart,periodEnd;
        switch(frequency)
        {
            case "WEEKLY":periodStart=WeekStart(date,weekStart);periodEnd=periodStart.AddDays(6);break;
            case "MONTHLY":periodStart=new DateTime(date.Year,date.Month,1);periodEnd=periodStart.AddMonths(1).AddDays(-1);break;
            case "YEARLY":periodStart=new DateTime(date.Year,1,1);periodEnd=new DateTime(date.Year,12,31);break;
            default:periodStart=date.Date;periodEnd=periodStart;break;
        }
        var selected=new List<DateTime>();
        for(var item=periodStart;item<=periodEnd;item=item.AddDays(1))
            if(MatchesRecurrenceDate(item,start,frequency,interval,weekStart,firstWeek,byday,bymonthdays,bymonths))selected.Add(item);
        var index=selected.IndexOf(date);
        if(index<0)return false;
        return bysetpos.Any(position=>position>0?index+1==position:index-selected.Count==position);
    }
    private static bool MatchesRecurrenceDate(DateTime date,DateTime start,string frequency,int interval,DayOfWeek weekStart,DateTime firstWeek,ByDay[] byday,int[] bymonthdays,int[] bymonths)
    {
        if(date<start.Date)return false;
        if(bymonths.Length>0&&!bymonths.Contains(date.Month))return false;
        var intervalMatch=frequency switch
        {
            "DAILY"=>(date-start.Date).Days%interval==0,
            "WEEKLY"=>(WeekStart(date,weekStart)-firstWeek).Days/7%interval==0,
            "MONTHLY"=>((date.Year-start.Year)*12+date.Month-start.Month)%interval==0,
            "YEARLY"=>(date.Year-start.Year)%interval==0,
            _=>false
        };
        if(!intervalMatch)return false;
        if(bymonthdays.Length>0)
        {
            var days=DateTime.DaysInMonth(date.Year,date.Month);
            if(!bymonthdays.Any(day=>date.Day==(day>0?day:days+day+1)))return false;
        }
        else if(frequency is "MONTHLY" or "YEARLY"&&byday.Length==0&&date.Day!=start.Day)return false;
        if(byday.Length>0)
        {
            var matches=byday.Where(x=>x.Day==date.DayOfWeek).ToArray();
            if(matches.Length==0)return false;
            if(frequency is "MONTHLY" or "YEARLY")
            {
                var numbered=matches.Where(x=>x.Ordinal.HasValue).ToArray();
                if(numbered.Length>0&&!numbered.Any(x=>WeekdayOrdinal(date,x.Day,frequency=="YEARLY"&&bymonths.Length==0,x.Ordinal!.Value<0)==x.Ordinal))return false;
            }
            else if(frequency!="WEEKLY"&&!matches.Any(x=>!x.Ordinal.HasValue))return false;
        }
        else if(frequency=="WEEKLY"&&date.DayOfWeek!=start.DayOfWeek)return false;
        if(frequency=="YEARLY"&&bymonths.Length==0&&bymonthdays.Length==0&&byday.Length==0&&date.Month!=start.Month)return false;
        return true;
    }
    private static int WeekdayOrdinal(DateTime date,DayOfWeek day,bool yearScope,bool negative)
    {
        var first=yearScope?new DateTime(date.Year,1,1):new DateTime(date.Year,date.Month,1);
        var last=yearScope?new DateTime(date.Year,12,31):new DateTime(date.Year,date.Month,DateTime.DaysInMonth(date.Year,date.Month));
        var ordinal=((date-first).Days+((7+(int)first.DayOfWeek-(int)day)%7))/7+1;
        var endOrdinal=((last-date).Days+((7+(int)last.DayOfWeek-(int)day)%7))/7+1;
        return negative? -endOrdinal:ordinal;
    }
    private static DateTime WeekStart(DateTime date,DayOfWeek firstDay)=>date.AddDays(-((7+(int)date.DayOfWeek-(int)firstDay)%7));
    private static int[] ParseIntegerList(string text)=>text.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(x=>int.TryParse(x,out var value)?value:0).Where(x=>x!=0).ToArray();
    private static ByDay? ParseByDay(string text)
    {
        if(text.Length<2)return null;
        var day=ParseWeekday(text[^2..]);if(!day.HasValue)return null;
        int? ordinal=null;
        if(text.Length>2&&int.TryParse(text[..^2],out var value)&&value is >=-5 and <=5&&value!=0)ordinal=value;
        return new ByDay(day.Value,ordinal);
    }
    private readonly record struct ByDay(DayOfWeek Day,int? Ordinal);
    private static DayOfWeek? ParseWeekday(string day)=>day.ToUpperInvariant() switch{"MO"=>DayOfWeek.Monday,"TU"=>DayOfWeek.Tuesday,"WE"=>DayOfWeek.Wednesday,"TH"=>DayOfWeek.Thursday,"FR"=>DayOfWeek.Friday,"SA"=>DayOfWeek.Saturday,"SU"=>DayOfWeek.Sunday,_=>null};
    public static string? FindMeetingLink(string text)
    {
        foreach(Match match in MeetingLinkRegex().Matches(text))
        {
            var candidate=match.Value.TrimEnd('.',',',';',')');
            if(IsJoinableMeetingLink(candidate))return candidate;
        }
        return null;
    }
    public static bool IsJoinableMeetingLink(string? candidate)
    {
        if(!Uri.TryCreate(candidate,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps)return false;
        var host=uri.Host.ToLowerInvariant();
        return host=="meet.google.com"||host.EndsWith(".zoom.us")||host=="zoom.us"||host=="teams.microsoft.com"||host.EndsWith(".teams.microsoft.com")||host=="teams.live.com"||host=="webex.com"||host.EndsWith(".webex.com")||host=="whereby.com"||host.EndsWith(".whereby.com")||host=="meet.jit.si"||host=="telemost.yandex.ru"||host=="discord.gg";
    }
    [GeneratedRegex("https?://[^\\s<>\\\"']+",RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLinkRegex();
}
