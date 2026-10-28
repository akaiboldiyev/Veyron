using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Cyclop.Windows;

internal sealed class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static bool ClipboardCaptureAllowed
    {
        get
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("CYCLOP_QA_CAPTURE_CLIPBOARD")=="1";
#else
            return true;
#endif
        }
    }
    private static bool ClipboardReadAllowed
    {
        get
        {
#if DEBUG
            // QA can load a synthetic clipboard fixture without enabling
            // capture from the user's live Windows clipboard.
            return ClipboardCaptureAllowed||Environment.GetEnvironmentVariable("CYCLOP_QA_LOAD_CLIPBOARD")=="1";
#else
            return true;
#endif
        }
    }
    private static readonly HashSet<string> ScreenshotImageExtensions=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".tif",".tiff",".bmp",".gif",".webp",".heic",".heif"};
    private static bool IsVisibleFileSystemEntry(string path){try{return(File.GetAttributes(path)&FileAttributes.Hidden)==0;}catch{return false;}}
#if DEBUG
    // Live UI testing must never edit the user's real Cyclop profile.
    private readonly string root = Path.Combine(AppContext.BaseDirectory,"Cyclop-QA");
#else
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyclop");
#endif
    private readonly DispatcherTimer clipboardTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private string activeTabId="";
    private int calendarRefreshGeneration;
    private Guid? selectedNoteId;
    private uint lastClipboardSequence;
    private readonly HashSet<string> revealedPrivateItems=[];
    private readonly HashSet<string> expandedPanels=new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? screenshotWatcher;
    private readonly HashSet<string> pendingScreenshotPaths=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> pendingClipboardImages=[];
    private readonly HashSet<string> brokenDataFiles=new(StringComparer.OrdinalIgnoreCase);
    private string? screenshotWatchFolderPath;
    private readonly DispatcherTimer calendarTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly DispatcherTimer calendarRefreshTimer=new(){Interval=TimeSpan.FromHours(1)};
    private readonly DispatcherTimer notesSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer scriptSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer teleprompterTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch teleprompterClock=new();
    private double teleprompterMaxOffset;
    public static DataStore Current { get; private set; } = null!;
    public ObservableCollection<string> Shelf { get; } = [];
    public ObservableCollection<ClipboardEntry> ClipboardHistory { get; } = [];
    public ObservableCollection<SnippetItem> Snippets { get; } = [];
    public bool SnippetsFileBroken { get; private set; }
    public bool ConfigFileBroken { get; private set; }
    public ObservableCollection<NoteItem> Notes { get; } = [];
    public event Action? SelectedNoteChanged;
    public Guid? SelectedNoteId
    {
        get=>selectedNoteId;
        set{if(selectedNoteId==value)return;selectedNoteId=value;SelectedNoteChanged?.Invoke();}
    }
    public ObservableCollection<CalendarEvent> CalendarEvents { get; } = [];
    public ObservableCollection<string> CalendarSources { get; } = [];
    public ObservableCollection<WindowsCalendarOption> WindowsCalendars { get; } = [];
    public Dictionary<string,bool> WindowsCalendarVisibility { get; } = new(StringComparer.Ordinal);
    public event Action? CalendarClockTick;
    public event Action? ScreenshotReceived;
    public event Action? TranslationStateChanged;
    public event Action? TeleprompterStateChanged;
    public ObservableCollection<string> HiddenTabs { get; } = [];
    public ObservableCollection<string> PrivacySections { get; } = [];
    public bool HideSensitiveContents=>PrivacySections.Count==4;
    public string SourceCurrency { get; private set; }="usd";
    public string TargetCurrency { get; private set; }=CurrencyDefaults.Target;
    public string CurrencySourceAmount { get; private set; }="1";
    public string CurrencyTargetAmount { get; private set; }="";
    public string CurrencyEditingSide { get; private set; }="source";
    public double TeleprompterSpeed { get; private set; }=1;
    public double TeleprompterFontSize { get; private set; }=30;
    public bool SaveClipboardImages { get; private set; }=true;
    public bool WatchScreenshotFolder { get; private set; }=true;
    public string ScreenshotWatchFolderPath=>screenshotWatchFolderPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Screenshots");
    public bool LaunchAtLogin { get; private set; }
    public bool ShowTrayIcon { get; private set; }=true;
    public bool ShowOnAllDisplays { get; private set; }=true;
    public bool FullSizeDrawnNotch { get; private set; }
    public string Teleprompter { get; private set; } = "";
    public double TeleprompterOffset { get; private set; }
    public bool TeleprompterIsRunning { get; private set; }
    public string? TeleprompterOwnerPanelKey { get; private set; }
    public string TranslationInput { get; private set; }="";
    public string TranslationOutput { get; private set; }="";
    public string ScreenshotFolder { get; }

    public static void Initialize() => Current = new DataStore();
    public void StartBackgroundServices()=>SetBackground("media",!HiddenTabs.Contains("media"));
    public void StopBackgroundServices(){clipboardTimer.Stop();calendarTimer.Stop();calendarRefreshTimer.Stop();notesSaveTimer.Stop();scriptSaveTimer.Stop();teleprompterTimer.Stop();screenshotWatcher?.Dispose();Save("notes.json",Notes);WriteScript();MediaSessionService.Current.Stop();}

    private DataStore()
    {
        Directory.CreateDirectory(root);
        ScreenshotFolder = CreateScreenshotFolder(root);
        Read("shelf.json", Shelf);
        if(ClipboardReadAllowed)Read("clipboard.json", ClipboardHistory);
        SnippetsFileBroken=!Read("snippets.json", Snippets); Read("notes.json", Notes); Read("calendar.json", CalendarEvents); Read("calendar-sources.json",CalendarSources);
        lastClipboardSequence=ClipboardSequence();
        try
        {
            var config=File.Exists(PathOf("config.json"))?JsonSerializer.Deserialize<WindowsConfig>(File.ReadAllText(PathOf("config.json")),JsonOptions):new WindowsConfig();
            if(config is not null)
            {
                foreach(var tab in config.HiddenTabs??[])if(tab!="settings")HiddenTabs.Add(tab);
                foreach(var section in (config.Privacy??[]).Distinct())if(PrivateSections.Contains(section))PrivacySections.Add(section);
                if((config.Privacy?.Count??0)==0&&config.HideSensitiveContents)foreach(var section in PrivateSections)PrivacySections.Add(section);
                foreach(var pair in config.WindowsCalendarVisibility??new Dictionary<string,bool>())WindowsCalendarVisibility[pair.Key]=pair.Value;
                SourceCurrency=string.IsNullOrWhiteSpace(config.SourceCurrency)?"usd":config.SourceCurrency;
                TargetCurrency=string.IsNullOrWhiteSpace(config.TargetCurrency)?CurrencyDefaults.Target:config.TargetCurrency;
                TeleprompterSpeed=Math.Clamp(config.TeleprompterSpeed,.3,3);
                TeleprompterFontSize=Math.Clamp(config.TeleprompterFontSize,18,64);
                SaveClipboardImages=config.SaveClipboardImages;WatchScreenshotFolder=config.WatchScreenshotFolder;screenshotWatchFolderPath=string.IsNullOrWhiteSpace(config.ScreenshotWatchFolderPath)?null:config.ScreenshotWatchFolderPath;LaunchAtLogin=config.LaunchAtLogin;ShowTrayIcon=config.ShowTrayIcon;ShowOnAllDisplays=config.ShowOnAllDisplays;FullSizeDrawnNotch=config.FullSizeDrawnNotch;
            }
        }
        catch{ConfigFileBroken=true;}
        Teleprompter = File.Exists(PathOf("teleprompter.txt")) ? File.ReadAllText(PathOf("teleprompter.txt")) : "";
        ConfigureScreenshotWatcher();
        calendarTimer.Tick+=(_,_)=>RemoveFinishedCalendarEvents();
        calendarRefreshTimer.Tick+=async(_,_)=>await RefreshCalendarsAsync();
        clipboardTimer.Tick += (_, _) => CaptureClipboard();
        SetBackground("clipboard",!HiddenTabs.Contains("clipboard"));
        SetBackground("shelf",!HiddenTabs.Contains("shelf"));
        notesSaveTimer.Tick+=(_,_)=>{notesSaveTimer.Stop();Save("notes.json",Notes);};
        scriptSaveTimer.Tick+=(_,_)=>{scriptSaveTimer.Stop();WriteScript();};
        teleprompterTimer.Tick+=(_,_)=>TickTeleprompter();
    }

    private string PathOf(string name) => Path.Combine(root, name);
    private static string CreateScreenshotFolder(string appDataFolder)
    {
        var picturesRoot=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if(!string.IsNullOrWhiteSpace(picturesRoot))
        {
            var pictures=Path.Combine(picturesRoot,"Cyclop");
            try{Directory.CreateDirectory(pictures);return pictures;}
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop Pictures folder unavailable: "+ex.Message);}
        }
        var fallback=Path.Combine(appDataFolder,"Screenshots");Directory.CreateDirectory(fallback);return fallback;
    }
    private bool Read<T>(string name, ObservableCollection<T> target)
    {
        var path=PathOf(name);
        if(!File.Exists(path)){if(TryRecoverBackup(path,target))return true;return true;}
        try
        {
            var loaded=JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path),JsonOptions)??[];
            foreach(var item in loaded)target.Add(item);
            return true;
        }
        catch(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cyclop: cannot read {name}: {ex.Message}");
            if(TryRecoverBackup(path,target))return true;
            brokenDataFiles.Add(name);
            return false;
        }
    }
    private bool TryRecoverBackup<T>(string path,ObservableCollection<T> target)
    {
        var backup=path+".bak";
        if(!File.Exists(backup))return false;
        try
        {
            var recovered=JsonSerializer.Deserialize<List<T>>(File.ReadAllText(backup),JsonOptions)??[];
            if(File.Exists(path))
            {
                var preserved=path+".corrupt-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",System.Globalization.CultureInfo.InvariantCulture);
                File.Copy(path,preserved,false);
            }
            var temporary=path+".restore.tmp";
            File.Copy(backup,temporary,true);
            File.Move(temporary,path,true);
            foreach(var item in recovered)target.Add(item);
            brokenDataFiles.Remove(Path.GetFileName(path));
            System.Diagnostics.Debug.WriteLine($"Cyclop: restored {Path.GetFileName(path)} from its backup.");
            return true;
        }
        catch(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cyclop: backup recovery failed for {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }
    private void Save<T>(string name,IEnumerable<T> items)
    {
        if(brokenDataFiles.Contains(name))
        {
            System.Diagnostics.Debug.WriteLine($"Cyclop: refused to overwrite unreadable {name}; the original file is preserved.");
            return;
        }
        WriteAtomic(PathOf(name),JsonSerializer.Serialize(items,JsonOptions));
    }
    private static void WriteAtomic(string path,string contents)
    {
        var temporary=path+".tmp";
        var backupTemporary=path+".bak.tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
            {
                using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false),1024,true)){writer.Write(contents);writer.Flush();}
                stream.Flush(true);
            }
            if(File.Exists(path))
            {
                File.Copy(path,backupTemporary,true);
                using(var backup=new FileStream(backupTemporary,FileMode.Open,FileAccess.ReadWrite,FileShare.None))backup.Flush(true);
                File.Move(backupTemporary,path+".bak",true);
            }
            File.Move(temporary,path,true);
        }
        finally
        {
            try{if(File.Exists(temporary))File.Delete(temporary);}catch{ }
            try{if(File.Exists(backupTemporary))File.Delete(backupTemporary);}catch{ }
        }
    }
    private static readonly string[] PrivateSections=["clipboard","snippets","calendar","notes"];
    private void SaveConfig(){if(ConfigFileBroken)return;WriteAtomic(PathOf("config.json"),JsonSerializer.Serialize(new WindowsConfig{HiddenTabs=HiddenTabs.ToList(),HideSensitiveContents=HideSensitiveContents,Privacy=PrivacySections.ToList(),WindowsCalendarVisibility=WindowsCalendarVisibility,SourceCurrency=SourceCurrency,TargetCurrency=TargetCurrency,TeleprompterSpeed=TeleprompterSpeed,TeleprompterFontSize=TeleprompterFontSize,SaveClipboardImages=SaveClipboardImages,WatchScreenshotFolder=WatchScreenshotFolder,ScreenshotWatchFolderPath=screenshotWatchFolderPath,LaunchAtLogin=LaunchAtLogin,ShowTrayIcon=ShowTrayIcon,ShowOnAllDisplays=ShowOnAllDisplays,FullSizeDrawnNotch=FullSizeDrawnNotch},JsonOptions));}
    public void SetTabVisible(string id,bool visible){if(id=="settings")return;if(visible)HiddenTabs.Remove(id);else if(!HiddenTabs.Contains(id))HiddenTabs.Add(id);SaveConfig();if(id=="media"){SetBackground(id,visible);SyncBackgroundActivity();}else if(id=="calendar")SyncBackgroundActivity();else if(id=="utilities"&&!visible)KeyboardLockService.Current.Unlock("Блокировка снята: вкладка «Утилиты» скрыта.");else SetBackground(id,visible);}
    public void SetActiveTab(string id)
    {
        activeTabId=id;
        SyncBackgroundActivity();
    }
    public void SetPanelExpanded(string panelKey,bool expanded)
    {
        if(expanded)expandedPanels.Add(panelKey);else expandedPanels.Remove(panelKey);
        SyncBackgroundActivity();
    }
    private void SyncBackgroundActivity()
    {
        // The macOS panel keeps shared, time-sensitive data fresh while any
        // display is open. Restricting this to the selected tab meant a
        // calendar countdown or Now Playing row could be stale after a tab
        // switch. Visibility still controls whether a service may run at all.
        var anyPanelOpen=expandedPanels.Count>0;
        var calendarActive=anyPanelOpen&&!HiddenTabs.Contains("calendar");
        var wasCalendarActive=calendarTimer.IsEnabled;
        SetBackground("calendar",calendarActive);
        if(calendarActive&&!wasCalendarActive)_=RefreshCalendarsAsync();
        var mediaActive=anyPanelOpen&&!HiddenTabs.Contains("media");
        MediaSessionService.Current.SetActive(mediaActive);
    }
    private void SetBackground(string id,bool enabled)
    {
        switch(id)
        {
            case "clipboard":if(enabled&&ClipboardCaptureAllowed)clipboardTimer.Start();else clipboardTimer.Stop();break;
            case "shelf":ConfigureScreenshotWatcher(enabled);break;
            case "calendar":if(enabled){calendarTimer.Start();calendarRefreshTimer.Start();}else{calendarTimer.Stop();calendarRefreshTimer.Stop();}break;
            case "media":if(enabled)_=MediaSessionService.Current.StartAsync();else MediaSessionService.Current.Stop();break;
        }
    }
    private void ConfigureScreenshotWatcher(bool enabled=true)
    {
        if(!enabled||!WatchScreenshotFolder){screenshotWatcher?.Dispose();screenshotWatcher=null;return;}
        var folder=ScreenshotWatchFolderPath;
        if(!Directory.Exists(folder)){screenshotWatcher?.Dispose();screenshotWatcher=null;return;}
        if(screenshotWatcher is null||!string.Equals(screenshotWatcher.Path,folder,StringComparison.OrdinalIgnoreCase))
        {
            screenshotWatcher?.Dispose();
            screenshotWatcher=new FileSystemWatcher(folder){NotifyFilter=NotifyFilters.FileName|NotifyFilters.CreationTime|NotifyFilters.LastWrite|NotifyFilters.Size};
            screenshotWatcher.Created+=(_,e)=>QueueScreenshot(e.FullPath);
            screenshotWatcher.Renamed+=(_,e)=>QueueScreenshot(e.FullPath);
            screenshotWatcher.Changed+=(_,e)=>QueueScreenshot(e.FullPath);
        }
        screenshotWatcher.EnableRaisingEvents=true;
    }
    public bool IsPrivateHidden(string section,string item)=>PrivacySections.Contains(section)&&!revealedPrivateItems.Contains(section+":"+item);
    public void TogglePrivateReveal(string section,string item){var key=section+":"+item;if(!revealedPrivateItems.Add(key))revealedPrivateItems.Remove(key);}
    public bool CoverRevealedPrivateItems(){if(revealedPrivateItems.Count==0)return false;revealedPrivateItems.Clear();return true;}
    public void SetPrivacySection(string section,bool enabled){if(!PrivateSections.Contains(section))return;if(enabled&&!PrivacySections.Contains(section)){PrivacySections.Add(section);revealedPrivateItems.RemoveWhere(x=>x.StartsWith(section+":",StringComparison.Ordinal));}else if(!enabled)PrivacySections.Remove(section);if(PrivacySections.Count==0)revealedPrivateItems.Clear();SaveConfig();}
    public void SetHideSensitiveContents(bool value){PrivacySections.Clear();if(value)foreach(var section in PrivateSections)PrivacySections.Add(section);revealedPrivateItems.Clear();SaveConfig();}
    public void SetScreenshotOptions(bool saveClipboardImages,bool watchFolder){SaveClipboardImages=saveClipboardImages;WatchScreenshotFolder=watchFolder;if(!watchFolder)screenshotWatchFolderPath=null;else screenshotWatchFolderPath??=ScreenshotWatchFolderPath;SaveConfig();SetBackground("shelf",!HiddenTabs.Contains("shelf"));}
    public void SetScreenshotWatchFolder(string? folder){if(string.IsNullOrWhiteSpace(folder)){WatchScreenshotFolder=false;screenshotWatchFolderPath=null;}else{screenshotWatchFolderPath=Path.GetFullPath(folder);WatchScreenshotFolder=true;}SaveConfig();SetBackground("shelf",!HiddenTabs.Contains("shelf"));}
    public async Task SetLaunchAtLoginAsync(bool enabled)
    {
        if(IsPackaged())
        {
            try
            {
                var task=await global::Windows.ApplicationModel.StartupTask.GetAsync("VeyronStartup");
                if(enabled&&task.State==global::Windows.ApplicationModel.StartupTaskState.Disabled)await task.RequestEnableAsync();
                else if(!enabled&&task.State==global::Windows.ApplicationModel.StartupTaskState.Enabled)task.Disable();
                LaunchAtLogin=IsStartupEnabled(task.State);
                SaveConfig();return;
            }
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop startup task: "+ex.Message);}
        }
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)??Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled&&Environment.ProcessPath is { } path)key.SetValue("Veyron",$"\"{path}\"");else key.DeleteValue("Veyron",false);
        LaunchAtLogin=enabled;SaveConfig();
    }
    public async Task RefreshLaunchAtLoginStatusAsync()
    {
        if(IsPackaged())
        {
            try{var task=await global::Windows.ApplicationModel.StartupTask.GetAsync("VeyronStartup");LaunchAtLogin=IsStartupEnabled(task.State);return;}
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop startup state: "+ex.Message);}
        }
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        LaunchAtLogin=key?.GetValue("Veyron") is string command&&Environment.ProcessPath is { } path&&command.Contains(path,StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsStartupEnabled(global::Windows.ApplicationModel.StartupTaskState state)=>state is global::Windows.ApplicationModel.StartupTaskState.Enabled or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    private static bool IsPackaged(){try{_ = global::Windows.ApplicationModel.Package.Current.Id;return true;}catch{return false;}}
    public void SetDisplayOptions(bool allDisplays,bool fullSize){ShowOnAllDisplays=allDisplays;FullSizeDrawnNotch=fullSize;SaveConfig();}
    public void SetShowTrayIcon(bool value){ShowTrayIcon=value;SaveConfig();TrayIconService.Current.SetVisible(value);}
    public void SetCurrencyPair(string source,string target){SourceCurrency=source.ToLowerInvariant();TargetCurrency=target.ToLowerInvariant();SaveConfig();}
    public void SetCurrencyAmounts(string source,string target,string side){CurrencySourceAmount=source;CurrencyTargetAmount=target;CurrencyEditingSide=side;}
    public void SetTeleprompterScript(string value)
    {
        if(Teleprompter==value)return;
        Teleprompter=value;TeleprompterOffset=0;SaveScript();TeleprompterStateChanged?.Invoke();
    }
    public void SetTeleprompter(double speed,double size)
    {
        var nextSpeed=Math.Clamp(speed,.3,3);var nextSize=Math.Clamp(size,18,64);
        if(TeleprompterSpeed==nextSpeed&&TeleprompterFontSize==nextSize)return;
        TeleprompterSpeed=nextSpeed;TeleprompterFontSize=nextSize;SaveConfig();TeleprompterStateChanged?.Invoke();
    }
    public void StartTeleprompter(string ownerPanelKey,double maxOffset)
    {
        if(string.IsNullOrWhiteSpace(Teleprompter)||TeleprompterIsRunning)return;
        teleprompterMaxOffset=Math.Max(0,maxOffset);
        if(TeleprompterOffset>=maxOffset)TeleprompterOffset=0;
        TeleprompterOwnerPanelKey=ownerPanelKey;TeleprompterIsRunning=true;teleprompterClock.Restart();teleprompterTimer.Start();TeleprompterStateChanged?.Invoke();
    }
    public void UpdateTeleprompterExtent(string panelKey,double maxOffset)
    {
        if(TeleprompterIsRunning&&TeleprompterOwnerPanelKey!=panelKey)return;
        teleprompterMaxOffset=Math.Max(0,maxOffset);
        if(TeleprompterOffset>teleprompterMaxOffset)TeleprompterOffset=teleprompterMaxOffset;
    }
    public void PauseTeleprompter(bool rewind=false)
    {
        teleprompterTimer.Stop();teleprompterClock.Stop();
        var changed=TeleprompterIsRunning||TeleprompterOwnerPanelKey is not null||(rewind&&TeleprompterOffset!=0);
        TeleprompterIsRunning=false;TeleprompterOwnerPanelKey=null;
        if(rewind)TeleprompterOffset=0;
        if(changed)TeleprompterStateChanged?.Invoke();
    }
    public void PauseTeleprompterOwnedBy(string panelKey,bool rewind=false)
    {
        if(TeleprompterOwnerPanelKey==panelKey)PauseTeleprompter(rewind);
    }
    public void RewindTeleprompter()=>PauseTeleprompter(true);
    private void TickTeleprompter()
    {
        if(!TeleprompterIsRunning){teleprompterTimer.Stop();return;}
        var elapsed=teleprompterClock.Elapsed.TotalSeconds;teleprompterClock.Restart();
        var end=teleprompterMaxOffset;
        TeleprompterOffset=Math.Min(end,TeleprompterOffset+22*TeleprompterSpeed*elapsed);
        if(TeleprompterOffset>=end){TeleprompterIsRunning=false;TeleprompterOwnerPanelKey=null;teleprompterTimer.Stop();teleprompterClock.Stop();}
        TeleprompterStateChanged?.Invoke();
    }
    public void SetTranslationInput(string value){if(TranslationInput==value)return;TranslationInput=value;TranslationStateChanged?.Invoke();}
    public void SetTranslationOutput(string value){if(TranslationOutput==value)return;TranslationOutput=value;TranslationStateChanged?.Invoke();}

    public void AddShelf(string path)
    {
        if (!(File.Exists(path)||Directory.Exists(path))||Shelf.Contains(path,StringComparer.OrdinalIgnoreCase))return;
        Shelf.Insert(0,path);
        Save("shelf.json",Shelf);
    }
    public void RefreshShelfFromDisk()
    {
        // Keep references even when a source file is temporarily missing,
        // moved, on an offline drive, or on a disconnected network share.
    }
    private void AddScreenshotToShelf(string path){AddShelf(path);ScreenshotReceived?.Invoke();}
    private void QueueScreenshot(string path)
    {
        if(!ScreenshotImageExtensions.Contains(Path.GetExtension(path))||!IsVisibleFileSystemEntry(path))return;
        lock(pendingScreenshotPaths)if(!pendingScreenshotPaths.Add(path))return;
        Application.Current.Dispatcher.InvokeAsync(async()=>
        {
            try
            {
                long lastSize=-1;var stable=0;
                for(var attempt=0;attempt<60;attempt++)
                {
                    await Task.Delay(200);if(!File.Exists(path))return;
                    var size=new FileInfo(path).Length;
                    if(size>0&&size==lastSize){if(++stable>=2){AddScreenshotToShelf(path);return;}}
                    else stable=0;
                    lastSize=size;
                }
            }
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop screenshot watch: "+ex.Message);}
            finally{lock(pendingScreenshotPaths)pendingScreenshotPaths.Remove(path);}
        });
    }
    public void RemoveShelf(string path) { Shelf.Remove(path); Save("shelf.json", Shelf); }
    public void ClearShelf(){Shelf.Clear();Save("shelf.json",Shelf);}
    public void CopyShelfSelection(IEnumerable<string> paths)
    {
        var selected=paths.Where(x=>File.Exists(x)||Directory.Exists(x)).ToArray();if(selected.Length==0)return;
        try
        {
            var data=new DataObject();
            var collection=new System.Collections.Specialized.StringCollection();collection.AddRange(selected);data.SetFileDropList(collection);
            data.SetText(string.Join(Environment.NewLine,selected),TextDataFormat.UnicodeText);
            if(selected.Length==1&&File.Exists(selected[0])&&ScreenshotImageExtensions.Contains(Path.GetExtension(selected[0])))
            {
                try
                {
                    using var stream=File.OpenRead(selected[0]);
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();data.SetImage(image);
                }
                catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop shelf image copy: "+ex.Message);}
            }
            Clipboard.SetDataObject(data,true);lastClipboardSequence=ClipboardSequence();
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop shelf copy: "+ex.Message);}
    }
    public void AddSnippet(string label, string text)
    {
        ReloadSnippets();
        if(SnippetsFileBroken)return;
        var cleanLabel=label.Trim();var value=text.Trim();if(value.Length==0)return;
        var existing=Snippets.FirstOrDefault(x=>string.Equals(x.Label,cleanLabel,StringComparison.Ordinal)&&string.Equals(x.Text,value,StringComparison.Ordinal));
        if(existing is null)Snippets.Insert(0,new SnippetItem(cleanLabel,value));
        else Snippets.Move(Snippets.IndexOf(existing),0);
        Save("snippets.json",Snippets);
    }
    public void UpdateSnippet(SnippetItem item,string label,string text)
    {
        ReloadSnippets();
        if(SnippetsFileBroken)return;var index=Snippets.IndexOf(item);if(index<0)return;var cleanLabel=label.Trim();var value=text.Trim();if(value.Length==0)return;
        foreach(var duplicate in Snippets.Where(x=>x.Id!=item.Id&&string.Equals(x.Label,cleanLabel,StringComparison.Ordinal)&&string.Equals(x.Text,value,StringComparison.Ordinal)).ToArray())Snippets.Remove(duplicate);
        index=Snippets.IndexOf(item);if(index<0)return;
        Snippets[index]=new SnippetItem(cleanLabel,value);Save("snippets.json",Snippets);
    }
    public void RemoveSnippet(SnippetItem item) { ReloadSnippets();if(SnippetsFileBroken)return;Snippets.Remove(item); Save("snippets.json",Snippets); }
    public void MoveSnippet(SnippetItem item,int offset)
    {
        ReloadSnippets();if(SnippetsFileBroken)return;var index=Snippets.IndexOf(item);var next=index+offset;if(index<0||next<0||next>=Snippets.Count)return;
        Snippets.Move(index,next);Save("snippets.json",Snippets);
    }
    public void ReloadSnippets()
    {
        var path=PathOf("snippets.json");if(!File.Exists(path)){Snippets.Clear();SnippetsFileBroken=false;return;}
        try{var loaded=JsonSerializer.Deserialize<List<SnippetItem>>(File.ReadAllText(path),JsonOptions)??[];if(loaded.Any(x=>x.Text is null))throw new JsonException("A snippet is missing its text.");Snippets.Clear();foreach(var item in loaded.Select(x=>x with{Label=string.IsNullOrWhiteSpace(x.Label)?null:x.Label}).Distinct())Snippets.Add(item);SnippetsFileBroken=false;}
        catch(Exception ex){SnippetsFileBroken=true;System.Diagnostics.Debug.WriteLine(ex.Message);}
    }
    public void AddNote(string text="") { var note=new NoteItem(Guid.NewGuid(),text);Notes.Insert(0,note);SelectedNoteId=note.Id;Save("notes.json",Notes); }
    public void UpdateNote(NoteItem item,string text) { item.Text=text; notesSaveTimer.Stop();notesSaveTimer.Start(); }
    public void RemoveNote(NoteItem item) { Notes.Remove(item);if(SelectedNoteId==item.Id)SelectedNoteId=Notes.FirstOrDefault()?.Id;Save("notes.json",Notes); }
    public void LeaveNotes()
    {
        Notes.RemoveAllBlank();
        if(SelectedNoteId is { } selected&&!Notes.Any(note=>note.Id==selected))SelectedNoteId=Notes.FirstOrDefault()?.Id;
        notesSaveTimer.Stop();Save("notes.json",Notes);
    }
    public void ClearClipboardHistory(){ClipboardHistory.Clear();Save("clipboard.json",ClipboardHistory);}
    public void RemoveClipboardEntry(ClipboardEntry entry){ClipboardHistory.Remove(entry);Save("clipboard.json",ClipboardHistory);}
    public void SaveScript(){scriptSaveTimer.Stop();scriptSaveTimer.Start();}
    private void WriteScript()=>WriteAtomic(PathOf("teleprompter.txt"),Teleprompter);

    public void CaptureClipboard()
    {
        if(!ClipboardCaptureAllowed)return;
        try
        {
            var sequence=ClipboardSequence();if(sequence==lastClipboardSequence)return;lastClipboardSequence=sequence;
            if(ClipboardPrivacy.IsSensitive())return;
            if(Clipboard.ContainsFileDropList())
            {
                var files=Clipboard.GetFileDropList().Cast<string>().ToArray();
                if(files.Length>0)AddClipboardEntry(new ClipboardEntry(Path.GetFileName(files[0]),null,[files[0]]));
                return;
            }
            if(Clipboard.ContainsImage()&&SaveClipboardImages)
            {
                BitmapSource? image=null;
                try{image=Clipboard.GetImage();}catch(System.Runtime.InteropServices.ExternalException){ }
                if(image is null)
                {
                    if(pendingClipboardImages.Add(sequence))_ = AwaitClipboardImageAsync(sequence);
                    return;
                }
                SaveClipboardImage(image);return;
            }
            if(!Clipboard.ContainsText())return;
            CaptureClipboardText(sequence);
        }
        catch(System.Runtime.InteropServices.ExternalException) { }
    }
    private async Task AwaitClipboardImageAsync(uint sequence)
    {
        try
        {
            for(var attempt=0;attempt<12;attempt++)
            {
                await Task.Delay(500);
                if(ClipboardSequence()!=sequence)return;
                BitmapSource? image=null;
                try{if(Clipboard.ContainsImage())image=Clipboard.GetImage();}catch(System.Runtime.InteropServices.ExternalException){ }
                if(image is not null){SaveClipboardImage(image);return;}
            }
            if(ClipboardSequence()==sequence)CaptureClipboardText(sequence);
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop delayed clipboard image: "+ex.Message);}
        finally{pendingClipboardImages.Remove(sequence);}
    }
    private void CaptureClipboardText(uint sequence)
    {
        if(ClipboardSequence()!=sequence||ClipboardPrivacy.IsSensitive()||!Clipboard.ContainsText())return;
        var text=Clipboard.GetText();if(string.IsNullOrWhiteSpace(text))return;
        AddClipboardEntry(new ClipboardEntry(text,null));
    }
    private void SaveClipboardImage(BitmapSource image)
    {
        try
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            var stem=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",System.Globalization.CultureInfo.InvariantCulture);
            var path=Path.Combine(ScreenshotFolder,stem+".png");var suffix=2;
            while(File.Exists(path))path=Path.Combine(ScreenshotFolder,stem+" ("+(suffix++)+").png");
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))encoder.Save(stream);
            AddScreenshotToShelf(path);
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop clipboard image: "+ex.Message);}
    }
    private void AddClipboardEntry(ClipboardEntry entry)
    {
        var old=ClipboardHistory.FirstOrDefault(x=>SameClipboardPayload(x,entry)); if(old is not null)ClipboardHistory.Remove(old);
        ClipboardHistory.Insert(0,entry);
        Save("clipboard.json",ClipboardHistory);
    }
    private static bool SameClipboardPayload(ClipboardEntry left,ClipboardEntry right)
    {
        if((left.FilePaths?.Length??0)>0||(right.FilePaths?.Length??0)>0)
        {
            if(left.FilePaths is not {Length:>0}||right.FilePaths is not {Length:>0})return false;
            return string.Equals(left.FilePaths[0],right.FilePaths[0],StringComparison.OrdinalIgnoreCase);
        }
        if(left.ImagePath is not null||right.ImagePath is not null)
            return string.Equals(left.ImagePath,right.ImagePath,StringComparison.OrdinalIgnoreCase);
        return string.Equals(left.Text,right.Text,StringComparison.Ordinal);
    }
    public void RestoreClipboard(ClipboardEntry entry)
    {
        try
        {
            if(entry.FilePaths is {Length:>0} files)
            {
                var valid=files.Where(x=>File.Exists(x)||Directory.Exists(x)).ToArray();
                if(valid.Length==0)return;
                var collection=new System.Collections.Specialized.StringCollection();collection.AddRange(valid);Clipboard.SetFileDropList(collection);
            }
            else if(entry.ImagePath is { } path&&File.Exists(path))
            {
                using var stream=File.OpenRead(path); var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze(); Clipboard.SetImage(image);
            }
            else Clipboard.SetText(entry.Text);
            lastClipboardSequence=ClipboardSequence();
            if(ClipboardHistory.Remove(entry)){ClipboardHistory.Insert(0,entry);Save("clipboard.json",ClipboardHistory);}
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern uint GetClipboardSequenceNumber();
    private static uint ClipboardSequence()=>GetClipboardSequenceNumber();
    public void ImportCalendar(string path)
    {
        if(File.Exists(path)&&!CalendarSources.Contains(path)){CalendarSources.Add(path);Save("calendar-sources.json",CalendarSources);}
        _=RefreshCalendarsAsync();
    }
    public void AddCalendarUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))return;
        if(!CalendarSources.Contains(url)){CalendarSources.Add(url);Save("calendar-sources.json",CalendarSources);}
        _=RefreshCalendarsAsync();
    }
    public void RemoveCalendarSource(string source){CalendarSources.Remove(source);Save("calendar-sources.json",CalendarSources);_=RefreshCalendarsAsync();}
    public async Task RefreshCalendarsAsync()
    {
        var request=Interlocked.Increment(ref calendarRefreshGeneration);
        var events=new List<CalendarEvent>();
        var anySourceLoaded=false;
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        foreach(var source in CalendarSources.ToArray())
        {
            try
            {
                string content;
                if(Uri.TryCreate(source,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")content=await client.GetStringAsync(uri);
                else if(File.Exists(source))content=await File.ReadAllTextAsync(source);
                else continue;
                var sourceName=Uri.TryCreate(source,UriKind.Absolute,out var sourceUri)?sourceUri.Host:Path.GetFileNameWithoutExtension(source);
                events.AddRange(ICalendarParser.Parse(content).Select(item=>item with{CalendarName=sourceName,CalendarId=source}));anySourceLoaded=true;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cyclop calendar source {source}: {ex.Message}");
                var previous=CalendarEvents.Where(item=>string.Equals(item.CalendarId,source,StringComparison.Ordinal)).ToArray();
                events.AddRange(previous);if(previous.Length>0)anySourceLoaded=true;
            }
        }
        if(WindowsCalendarService.Current.IsConnected)
        {
            try
            {
                var snapshot=await WindowsCalendarService.Current.ReadUpcomingAsync(WindowsCalendarVisibility);
                WindowsCalendars.Clear();foreach(var calendar in snapshot.Calendars)WindowsCalendars.Add(calendar);
                events.AddRange(snapshot.Events);anySourceLoaded=true;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Cyclop Windows calendar: "+ex.Message);
                var configuredSources=CalendarSources.ToHashSet(StringComparer.Ordinal);
                var previous=CalendarEvents.Where(item=>!string.IsNullOrEmpty(item.CalendarId)&&!configuredSources.Contains(item.CalendarId)).ToArray();
                events.AddRange(previous);if(previous.Length>0)anySourceLoaded=true;
            }
        }
        if(!anySourceLoaded)
        {
            if(CalendarSources.Count==0&&!WindowsCalendarService.Current.IsConnected){CalendarEvents.Clear();Save("calendar.json",CalendarEvents);}
            return;
        }
        if(request!=Volatile.Read(ref calendarRefreshGeneration))return;
        var now=DateTime.Now;
        var upcoming=events.Where(x=>(x.EndsAt??x.StartsAt)>now&&x.StartsAt<now.AddDays(7)).OrderBy(x=>x.StartsAt).DistinctBy(x=>(x.Title,x.StartsAt,x.CalendarId)).ToList();
        CalendarEvents.Clear();foreach(var item in upcoming)CalendarEvents.Add(item);Save("calendar.json",CalendarEvents);
    }
    private void RemoveFinishedCalendarEvents()
    {
        var now=DateTime.Now;
        var finished=CalendarEvents.Where(x=>(x.EndsAt??x.StartsAt)<=now).ToArray();
        if(finished.Length>0){foreach(var item in finished)CalendarEvents.Remove(item);Save("calendar.json",CalendarEvents);}
        CalendarClockTick?.Invoke();
    }
    public async Task<bool> ConnectWindowsCalendarAsync()
    {
        var granted=await WindowsCalendarService.Current.ConnectAsync();
        if(granted)await RefreshCalendarsAsync();
        return granted;
    }
    public async Task ToggleWindowsCalendarAsync(WindowsCalendarOption option)
    {
        WindowsCalendarVisibility[option.Id]=!option.IsShown;
        SaveConfig();await RefreshCalendarsAsync();
    }
    public void ClearScreenshots()
    {
        string[] entries;
        try{entries=Directory.EnumerateFileSystemEntries(ScreenshotFolder).Where(IsVisibleFileSystemEntry).ToArray();}
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Cyclop screenshot cleanup: "+ex.Message);return;}
        var removed=new List<string>();
        foreach(var path in entries)
            try{if(Directory.Exists(path))Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);removed.Add(path);}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);}
        foreach(var item in Shelf.ToArray())
        {
            var fullItem=Path.GetFullPath(item);
            if(removed.Any(root=>string.Equals(fullItem,Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)||
                (Directory.Exists(root)||removed.Contains(root,StringComparer.OrdinalIgnoreCase))&&fullItem.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
                Shelf.Remove(item);
        }
        if(removed.Count>0)Save("shelf.json",Shelf);
    }
    public (int Files,long Bytes) ScreenshotUsage()
    {
        try{var entries=Directory.EnumerateFileSystemEntries(ScreenshotFolder).Where(IsVisibleFileSystemEntry).ToArray();return(entries.Length,entries.Sum(EntrySize));}
        catch{return(0,0);}
        static long EntrySize(string path)
        {
            try
            {
                if(File.Exists(path))return new FileInfo(path).Length;
                if(!Directory.Exists(path))return 0;
                return Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Sum(file=>{try{return new FileInfo(file).Length;}catch{return 0L;}});
            }
            catch{return 0;}
        }
    }
    public Task<(int Files,long Bytes)> ScreenshotUsageAsync()=>Task.Run(ScreenshotUsage);
}

internal sealed record SnippetItem
{
    public SnippetItem(string? label,string text){Label=string.IsNullOrWhiteSpace(label)?null:label;Text=text;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Label{get;init;}
    public string Text{get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid Id
    {
        get
        {
            var input=System.Text.Encoding.UTF8.GetBytes((Label??"")+"\0"+Text);
            var hash=System.Security.Cryptography.SHA256.HashData(input);
            return new Guid(hash.AsSpan(0,16));
        }
    }
    public override string ToString()
    {
        var hidden=DataStore.Current.IsPrivateHidden("snippets","snippet."+Id);
        return string.IsNullOrWhiteSpace(Label)?(hidden?"••••••••":Text):$"{Label}  ·  {(hidden?"••••••••":Text)}";
    }
}
internal sealed record ClipboardEntry(string Text,string? ImagePath,string[]? FilePaths=null)
{
    public Guid Id{get;init;}=Guid.NewGuid();
    public string Preview
    {
        get
        {
            if(DataStore.Current.IsPrivateHidden("clipboard",Id.ToString()))return "••••••••••";
            if(FilePaths is {Length:>0})return string.Join(", ",FilePaths.Select(path=>Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))));
            if(ImagePath is { } image)return "Изображение · "+Path.GetFileName(image);
            return Text.Replace("\r"," ").Replace("\n"," ").Trim();
        }
    }
    public string Glyph
    {
        get
        {
            if(ImagePath is not null)return "▧";
            if(FilePaths is {Length:>0})return "▤";
            return Uri.TryCreate(Text,UriKind.Absolute,out var uri)&&(uri.Scheme=="http"||uri.Scheme=="https")?"↗":"≡";
        }
    }
    public override string ToString()=>Preview;
}
internal sealed record CalendarEvent(string Title,DateTime StartsAt,string? MeetingUrl,string? Location)
{ public DateTime? EndsAt{get;init;}[System.Text.Json.Serialization.JsonIgnore] public string DisplayTime=>StartsAt.ToString("ddd, d MMM · HH:mm");[System.Text.Json.Serialization.JsonIgnore] public string DisplayTitle=>DataStore.Current.IsPrivateHidden("calendar","calendar")?"••••••••••":Title;[System.Text.Json.Serialization.JsonIgnore] public string? CalendarName{get;init;}[System.Text.Json.Serialization.JsonIgnore] public string? DisplayCalendarName=>DataStore.Current.IsPrivateHidden("calendar","calendar")?"":CalendarName;public string? CalendarId{get;init;}public override string ToString()=>DisplayTitle+"  ·  "+DisplayTime; }
internal sealed record WindowsCalendarOption(string Id,string Name,bool IsShown)
{ public override string ToString()=>(IsShown?"☑ ":"☐ ")+Name; }
internal sealed record WindowsCalendarSnapshot(IReadOnlyList<WindowsCalendarOption> Calendars,IReadOnlyList<CalendarEvent> Events);
internal sealed class WindowsConfig
{
    public List<string> HiddenTabs{get;init;}=[];
    public bool HideSensitiveContents{get;init;}
    public List<string> Privacy{get;init;}=[];
    public Dictionary<string,bool> WindowsCalendarVisibility{get;init;}=new(StringComparer.Ordinal);
    public string SourceCurrency{get;init;}="usd";
    public string TargetCurrency{get;init;}=CurrencyDefaults.Target;
    public double TeleprompterSpeed{get;init;}=1;
    public double TeleprompterFontSize{get;init;}=30;
    public bool SaveClipboardImages{get;init;}=true;
    public bool WatchScreenshotFolder{get;init;}
    public string? ScreenshotWatchFolderPath{get;init;}
    public bool LaunchAtLogin{get;init;}
    public bool ShowTrayIcon{get;init;}=true;
    public bool ShowOnAllDisplays{get;init;}=true;
    public bool FullSizeDrawnNotch{get;init;}
}
internal static class CurrencyDefaults
{
    public static string Target
    {
        get
        {
            try{var code=new System.Globalization.RegionInfo(System.Globalization.CultureInfo.CurrentCulture.Name).ISOCurrencySymbol.ToLowerInvariant();if(!string.IsNullOrWhiteSpace(code))return code=="usd"?"rub":code;}
            catch{ }
            return "rub";
        }
    }
}
internal sealed class NoteItem:INotifyPropertyChanged
{
    private string text;
    public NoteItem(Guid id,string text){Id=id;this.text=text;Edited=DateTime.Now;}
    public Guid Id{get;}
    public DateTime Edited{get;set;}
    public string Text{get=>text;set{if(text==value)return;text=value;Edited=DateTime.Now;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Text)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Title)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Edited)));}}
    [System.Text.Json.Serialization.JsonIgnore] public string Title=>DataStore.Current.IsPrivateHidden("notes",Id.ToString())?Edited.ToString("t",System.Globalization.CultureInfo.CurrentCulture)+"  ••••••••":Text.Split('\n').FirstOrDefault()?.Trim() is {Length:>0} title?title:"Новая заметка";
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class NoteCollectionExtensions
{
    public static void RemoveAllBlank(this ObservableCollection<NoteItem> notes){foreach(var item in notes.Where(x=>string.IsNullOrWhiteSpace(x.Text)).ToArray())notes.Remove(item);}
}
