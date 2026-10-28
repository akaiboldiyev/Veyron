using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Cyclop.Windows;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? reopenSignal;
    private EventHandler? displaySettingsChanged;
    private SessionSwitchEventHandler? sessionSwitchChanged;
    private PowerModeChangedEventHandler? powerModeChanged;
    private bool sessionLocked;
    private bool systemSuspended;
    protected override async void OnStartup(StartupEventArgs e)
    {
        const string mutexName="Local\\Veyron.SingleInstance";
        const string eventName="Local\\Veyron.Reopen";
        reopenSignal=new EventWaitHandle(false,EventResetMode.AutoReset,eventName);
        instanceMutex=new Mutex(true,mutexName,out var firstInstance);
        if(!firstInstance)
        {
            try{reopenSignal.Set();}catch(ObjectDisposedException){}
            Shutdown();
            return;
        }
        DataStore.Initialize();
        await DataStore.Current.RefreshLaunchAtLoginStatusAsync();
        DataStore.Current.StartBackgroundServices();
        TrayIconService.Current.Initialize(DataStore.Current.ShowTrayIcon);
        displaySettingsChanged=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.Normal,new Action(global::Cyclop.Windows.MainWindow.RefreshDisplayTopology));
        SystemEvents.DisplaySettingsChanged+=displaySettingsChanged;
        sessionSwitchChanged=(_,args)=>Dispatcher.BeginInvoke(new Action(()=>
        {
            if(args.Reason==SessionSwitchReason.SessionLock)sessionLocked=true;
            else if(args.Reason==SessionSwitchReason.SessionUnlock)sessionLocked=false;
            UpdateSessionAvailability();
        }));
        powerModeChanged=(_,args)=>Dispatcher.BeginInvoke(new Action(()=>
        {
            if(args.Mode==PowerModes.Suspend)systemSuspended=true;
            else if(args.Mode==PowerModes.Resume)systemSuspended=false;
            UpdateSessionAvailability();
        }));
        SystemEvents.SessionSwitch+=sessionSwitchChanged;
        SystemEvents.PowerModeChanged+=powerModeChanged;
        var signal=reopenSignal!;
        _=Task.Run(() =>
        {
            while(!Dispatcher.HasShutdownStarted)
            {
                try{signal.WaitOne();}
                catch(ObjectDisposedException){return;}
                if(Dispatcher.HasShutdownStarted)return;
                try{Dispatcher.BeginInvoke(DispatcherPriority.Normal,new Action(global::Cyclop.Windows.MainWindow.OpenFromTray));}
                catch(InvalidOperationException){return;}
            }
        });
        base.OnStartup(e);
    }
    private void UpdateSessionAvailability()=>global::Cyclop.Windows.MainWindow.SetSessionAvailable(!sessionLocked&&!systemSuspended);
    protected override void OnExit(ExitEventArgs e)
    {
        if(DataStore.Current is not null)DataStore.Current.StopBackgroundServices();
        TranslationService.Current.StopWorker();
        TrayIconService.Current.Dispose();
        if(displaySettingsChanged is not null)SystemEvents.DisplaySettingsChanged-=displaySettingsChanged;
        if(sessionSwitchChanged is not null)SystemEvents.SessionSwitch-=sessionSwitchChanged;
        if(powerModeChanged is not null)SystemEvents.PowerModeChanged-=powerModeChanged;
        reopenSignal?.Dispose();
        if(instanceMutex is not null){try{instanceMutex.ReleaseMutex();}catch(ApplicationException){}instanceMutex.Dispose();}
        base.OnExit(e);
    }
}
