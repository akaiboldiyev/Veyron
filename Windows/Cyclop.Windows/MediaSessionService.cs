using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Cyclop.Windows;

internal sealed class MediaSessionService
{
    private readonly DispatcherTimer positionTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly DispatcherTimer eventRefreshTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool requestedActive;
    private string lastArtworkKey="";
    private bool hasPositionAnchor;
    private bool positionAnchorPlaying;
    private string positionTrackKey="";
    private TimeSpan positionAnchorElapsed;
    private DateTimeOffset positionAnchorAt;
    private DateTimeOffset lastTimelineUpdatedAt=DateTimeOffset.MinValue;
    private TimeSpan lastTimelineElapsed;
    private const byte VkMediaNextTrack=0xB0;
    private const byte VkMediaPreviousTrack=0xB1;
    private const byte VkMediaPlayPause=0xB3;
    private const uint KeyUp=0x0002;
    public static MediaSessionService Current{get;}=new();
    public event Action<MediaSnapshot>? Updated;
    public MediaSnapshot Snapshot{get;private set;}=new("", "", "", false, TimeSpan.Zero, TimeSpan.Zero, null, "Нет активного медиаплеера",false,false,false,false);

    private MediaSessionService()
    {
        positionTimer.Tick+=async(_,_)=>await RefreshAsync(includeMediaProperties:false);
        eventRefreshTimer.Tick+=async(_,_)=>{eventRefreshTimer.Stop();await RefreshAsync();};
    }
    public async Task StartAsync()
    {
        if(manager is not null){SetActive(requestedActive);await RefreshAsync();return;}
        try
        {
            manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged+=OnCurrentSessionChanged;
            AttachSession(FindSession(manager));
            SetActive(requestedActive);
            await RefreshAsync();
        }
        catch(Exception ex){Snapshot=Snapshot with{Status="Медиадоступ Windows недоступен: "+ex.Message};Updated?.Invoke(Snapshot);}
    }
    public void SetActive(bool value)
    {
        var wasActive=requestedActive;
        requestedActive=value;
        if(requestedActive&&manager is not null)
        {
            positionTimer.Start();
            if(!wasActive)_=RefreshAsync();
        }
        else positionTimer.Stop();
    }
    public void Stop()
    {
        SetActive(false);eventRefreshTimer.Stop();
        if(manager is not null)manager.CurrentSessionChanged-=OnCurrentSessionChanged;
        manager=null;AttachSession(null);
    }
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args)
        =>Application.Current?.Dispatcher.BeginInvoke(new Action(()=>{if(!ReferenceEquals(manager,sender))return;AttachSession(FindSession(sender));QueueEventRefresh();}));
    private void AttachSession(GlobalSystemMediaTransportControlsSession? next)
    {
        if(ReferenceEquals(session,next))return;
        if(session is not null){session.MediaPropertiesChanged-=OnSessionChanged;session.PlaybackInfoChanged-=OnSessionChanged;session.TimelinePropertiesChanged-=OnSessionChanged;}
        session=next;
        ResetPositionClock();
        if(session is not null){session.MediaPropertiesChanged+=OnSessionChanged;session.PlaybackInfoChanged+=OnSessionChanged;session.TimelinePropertiesChanged+=OnSessionChanged;}
    }
    private void ResetPositionClock()
    {
        hasPositionAnchor=false;positionAnchorPlaying=false;positionTrackKey="";
        positionAnchorElapsed=TimeSpan.Zero;positionAnchorAt=DateTimeOffset.MinValue;
        lastTimelineUpdatedAt=DateTimeOffset.MinValue;lastTimelineElapsed=TimeSpan.Zero;
    }
    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender,object args)
        =>Application.Current?.Dispatcher.BeginInvoke(new Action(QueueEventRefresh));
    private void QueueEventRefresh(){eventRefreshTimer.Stop();eventRefreshTimer.Start();}
    private static GlobalSystemMediaTransportControlsSession? FindSession(GlobalSystemMediaTransportControlsSessionManager source)
    {
        // Some desktop players publish sessions through GetSessions before Windows
        // designates one as CurrentSession. Prefer the actively playing session,
        // then fall back to Windows' current session and any paused session.
        var sessions=source.GetSessions();
        var playing=sessions.FirstOrDefault(candidate=>candidate.GetPlaybackInfo()?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if(playing is not null)return playing;
        var current=source.GetCurrentSession();
        if(current is not null)return current;
        return sessions.FirstOrDefault(candidate=>candidate.GetPlaybackInfo()?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
            ??sessions.FirstOrDefault();
    }
    public async Task RefreshAsync(bool includeMediaProperties=true)
    {
        var currentManager=manager;
        if(currentManager is null)return;
        try
        {
            var current=FindSession(currentManager);
            if(!ReferenceEquals(current,session))AttachSession(current);
            if(current is null){lastArtworkKey="";Snapshot=new("","","",false,TimeSpan.Zero,TimeSpan.Zero,null,"Нет активного медиаплеера",false,false,false,false);Updated?.Invoke(Snapshot);return;}
            var properties=includeMediaProperties?await current.TryGetMediaPropertiesAsync():null;
            if(!ReferenceEquals(current,session)||!ReferenceEquals(currentManager,manager))return;
            var timeline=current.GetTimelineProperties();
            var playback=current.GetPlaybackInfo();
            var playing=playback?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var controls=playback?.Controls;
            var duration=timeline.EndTime-timeline.StartTime;
            if(duration<TimeSpan.Zero)duration=TimeSpan.Zero;
            var artist=properties is null?Snapshot.Artist:string.IsNullOrWhiteSpace(properties.Artist)?properties.AlbumArtist??"":properties.Artist;
            var title=properties?.Title??Snapshot.Title;
            var album=properties?.AlbumTitle??Snapshot.Album;
            // Position is only current as of LastUpdatedTime. Keep a local
            // anchor between system updates, and re-anchor at play/pause
            // transitions so time spent paused is never added on resume.
            var elapsed=ResolveElapsed(current.SourceAppUserModelId,title,artist,album,timeline.Position,timeline.StartTime,timeline.LastUpdatedTime,playing,DateTimeOffset.UtcNow);
            if(duration>TimeSpan.Zero&&elapsed>duration)elapsed=duration;
            var artwork=Snapshot.Artwork;
            if(properties is not null)
            {
                var artworkKey=current.SourceAppUserModelId+"\0"+properties.Title+"\0"+artist+"\0"+properties.AlbumTitle;
                if(artworkKey!=lastArtworkKey){artwork=await ReadArtworkAsync(properties.Thumbnail);if(!ReferenceEquals(current,session))return;lastArtworkKey=artworkKey;}
            }
            var source=AppName(current.SourceAppUserModelId);
            Snapshot=new(title,artist,album,playing,elapsed,duration,artwork,source,
                controls?.IsPreviousEnabled??true,
                (controls?.IsPlayPauseToggleEnabled??true)||(playing?(controls?.IsPauseEnabled??false):(controls?.IsPlayEnabled??false)),
                controls?.IsNextEnabled??true,controls?.IsPlaybackPositionEnabled??true,timeline.StartTime);
            Updated?.Invoke(Snapshot);
        }
        catch(Exception ex){if(ReferenceEquals(currentManager,manager)){Snapshot=Snapshot with{Status="Не удалось прочитать медиасессию: "+ex.Message};Updated?.Invoke(Snapshot);}}
    }
    private TimeSpan ResolveElapsed(string source,string title,string artist,string album,TimeSpan position,TimeSpan startTime,DateTimeOffset lastUpdatedAt,bool isPlaying,DateTimeOffset now)
    {
        var elapsed=position-startTime;
        if(elapsed<TimeSpan.Zero)elapsed=TimeSpan.Zero;
        var trackKey=string.Join("\0",source,title,artist,album);
        if(!hasPositionAnchor||trackKey!=positionTrackKey)
        {
            SetPositionAnchor(elapsed,now,isPlaying);
            positionTrackKey=trackKey;
        }
        else
        {
            var projected=positionAnchorElapsed;
            if(positionAnchorPlaying)
            {
                var age=now-positionAnchorAt;
                if(age>TimeSpan.Zero)projected+=age;
            }

            var timelineChanged=lastUpdatedAt!=DateTimeOffset.MinValue&&lastUpdatedAt>lastTimelineUpdatedAt
                &&Math.Abs((elapsed-lastTimelineElapsed).TotalSeconds)>=0.15;
            if(positionAnchorPlaying!=isPlaying)
            {
                // The latest reported position may be a fraction behind the
                // local clock, so preserve the greater value at the edge.
                SetPositionAnchor(elapsed>projected?elapsed:projected,now,isPlaying);
            }
            else if(!isPlaying&&timelineChanged)
            {
                // A paused seek should still move the thumb, but never tick.
                SetPositionAnchor(elapsed,now,false);
            }
            else if(isPlaying&&lastUpdatedAt!=DateTimeOffset.MinValue&&lastUpdatedAt>lastTimelineUpdatedAt)
            {
                // Accept only a newly published timeline point from the
                // current playing interval. A stale point from before resume
                // would otherwise include the entire paused interval.
                // Anchor it to our local observation time: providers' UTC
                // timestamps can be skewed, and must not freeze the UI clock.
                SetPositionAnchor(elapsed,now,true);
            }
        }
        lastTimelineElapsed=elapsed;
        if(lastUpdatedAt>lastTimelineUpdatedAt)lastTimelineUpdatedAt=lastUpdatedAt;
        var result=positionAnchorElapsed;
        if(positionAnchorPlaying)
        {
            var age=now-positionAnchorAt;
            if(age>TimeSpan.Zero)result+=age;
        }
        return result<TimeSpan.Zero?TimeSpan.Zero:result;
    }
    private void SetPositionAnchor(TimeSpan elapsed,DateTimeOffset at,bool isPlaying)
    {positionAnchorElapsed=elapsed;positionAnchorAt=at;positionAnchorPlaying=isPlaying;hasPositionAnchor=true;}
    private static async Task<BitmapSource?> ReadArtworkAsync(IRandomAccessStreamReference? reference)
    {
        if(reference is null)return null;
        try
        {
            using var stream=await reference.OpenReadAsync();
            var length=(uint)Math.Min(stream.Size,8*1024*1024);
            var buffer=new global::Windows.Storage.Streams.Buffer(length);
            await stream.ReadAsync(buffer,length,InputStreamOptions.None);
            var bytes=new byte[buffer.Length];DataReader.FromBuffer(buffer).ReadBytes(bytes);
            using var memory=new MemoryStream(bytes);
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=memory;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
        catch{return null;}
    }
    private static string AppName(string aumid)
    {
        var parts=aumid.Split('!');var id=parts[^1];
        if(string.Equals(id,"ru.yandex.desktop.music",StringComparison.OrdinalIgnoreCase))return "Яндекс Музыка";
        if(string.Equals(id,"chrome",StringComparison.OrdinalIgnoreCase))return "Google Chrome";
        if(string.Equals(id,"msedge",StringComparison.OrdinalIgnoreCase))return "Microsoft Edge";
        if(string.Equals(id,"spotify",StringComparison.OrdinalIgnoreCase))return "Spotify";
        return id.Length>24?id[..24]:id;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MouseInput{public int X,Y;public uint Data,Flags,Time;public nuint ExtraInfo;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KeyboardInput{public ushort VirtualKey,Scan;public uint Flags,Time;public nuint ExtraInfo;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct HardwareInput{public uint Message;public ushort ParamLow,ParamHigh;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct InputUnion
    {
        [System.Runtime.InteropServices.FieldOffset(0)]public MouseInput Mouse;
        [System.Runtime.InteropServices.FieldOffset(0)]public KeyboardInput Keyboard;
        [System.Runtime.InteropServices.FieldOffset(0)]public HardwareInput Hardware;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Input{public uint Type;public InputUnion Data;}
    [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]
    private static extern uint SendInput(uint count,[System.Runtime.InteropServices.In] Input[] inputs,int size);
    private static void SendMediaKey(byte key)
    {
        var inputSize=System.Runtime.InteropServices.Marshal.SizeOf<Input>();
        var down=new Input{Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{VirtualKey=key}}};
        var up=new Input{Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{VirtualKey=key,Flags=KeyUp}}};
        SendInput(2,[down,up],inputSize);
    }
    public async Task ToggleAsync()
    {
        var current=session;var handled=false;
        if(current is not null)try
        {
            var controls=current.GetPlaybackInfo()?.Controls;
            if(Snapshot.IsPlaying&&(controls?.IsPauseEnabled??false))handled=await current.TryPauseAsync();
            else if(!Snapshot.IsPlaying&&(controls?.IsPlayEnabled??false))handled=await current.TryPlayAsync();
            else if(controls?.IsPlayPauseToggleEnabled??false)handled=await current.TryTogglePlayPauseAsync();
        }
        catch{ }
        if(!handled)SendMediaKey(VkMediaPlayPause);await RefreshAsync();
    }
    public async Task NextAsync()
    {
        var current=session;var handled=false;
        if(current is not null)try{handled=await current.TrySkipNextAsync();}catch{ }
        if(!handled)SendMediaKey(VkMediaNextTrack);await RefreshAsync();
    }
    public async Task PreviousAsync()
    {
        var current=session;var handled=false;
        if(current is not null)try{handled=await current.TrySkipPreviousAsync();}catch{ }
        if(!handled)SendMediaKey(VkMediaPreviousTrack);await RefreshAsync();
    }
    public async Task SeekAsync(TimeSpan position)
    {
        var current=session;if(current is null)return;
        var target=position<TimeSpan.Zero?TimeSpan.Zero:Snapshot.Duration>TimeSpan.Zero&&position>Snapshot.Duration?Snapshot.Duration:position;
        try
        {
            if(await current.TryChangePlaybackPositionAsync((target+Snapshot.StartTime).Ticks))
            {
                var now=DateTimeOffset.UtcNow;SetPositionAnchor(target,now,Snapshot.IsPlaying);
                lastTimelineUpdatedAt=now;lastTimelineElapsed=target;
                Snapshot=Snapshot with{Elapsed=target};Updated?.Invoke(Snapshot);
            }
        }
        catch{ }
        await RefreshAsync();
    }
}

internal sealed record MediaSnapshot(string Title,string Artist,string Album,bool IsPlaying,TimeSpan Elapsed,TimeSpan Duration,BitmapSource? Artwork,string Status,bool CanPrevious=true,bool CanToggle=true,bool CanNext=true,bool CanSeek=true,TimeSpan StartTime=default)
{
    public string TimeLabel=>$"{FormatTime(Elapsed)} / {FormatTime(Duration)}";
    private static string FormatTime(TimeSpan value){var seconds=Math.Max(0,(long)value.TotalSeconds);return $"{seconds/60}:{seconds%60:D2}";}
}
