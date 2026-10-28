using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cyclop.Windows;

internal sealed class KeyboardLockService
{
    private const int KeyboardHook=13,MouseHook=14;
    private const int WmKeyDown=0x0100,WmSysKeyDown=0x0104,WmKeyUp=0x0101,WmSysKeyUp=0x0105;
    private const uint VkEscape=0x1B,VkMinus=0xBD,VkSubtract=0x6D;
    private const uint ScanEscape=0x01,ScanTopRowMinus=0x0C;
    private readonly LowLevelProc keyboardCallback,mouseCallback;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(30)};
    private readonly Stopwatch clock=new();
    private DateTime lockStarted;
    private nint keyboardHandle,mouseHandle;
    private bool unlockKeyDown;
    private uint unlockVirtualKey;
    private bool draining;
    private DateTime drainStarted;
    private OverlayWindow? overlay;
    public static KeyboardLockService Current{get;}=new();
    public event Action<string>? StatusChanged;
    public bool IsLocked{get;private set;}
    public const double HoldSeconds=2.0;
    private KeyboardLockService(){keyboardCallback=KeyboardHookProc;mouseCallback=MouseHookProc;timer.Tick+=(_,_)=>Tick();}

    public bool Start()
    {
        if(IsLocked)return true;
        if(draining){StatusChanged?.Invoke("Идёт завершение блокировки. Повторите через секунду.");return false;}
        keyboardHandle=SetWindowsHookEx(KeyboardHook,keyboardCallback,GetModuleHandle(null),0);
        mouseHandle=SetWindowsHookEx(MouseHook,mouseCallback,GetModuleHandle(null),0);
        if(keyboardHandle==0||mouseHandle==0){StopHooks();StatusChanged?.Invoke("Windows не установила системный перехват ввода.");return false;}
        IsLocked=true;lockStarted=DateTime.UtcNow;unlockKeyDown=false;unlockVirtualKey=0;overlay=new OverlayWindow();overlay.Show();MainWindow.SetCleaningMode(true);timer.Start();StatusChanged?.Invoke("Заблокировано. Удерживайте Esc или − 2 секунды для выхода.");return true;
    }
    public void Unlock(string? reason=null)
    {
        if(!IsLocked&&!draining)return;
        IsLocked=false;draining=false;timer.Stop();StopHooks();overlay?.Close();overlay=null;unlockKeyDown=false;unlockVirtualKey=0;MainWindow.SetCleaningMode(false);StatusChanged?.Invoke(reason??"Клавиатура разблокирована.");
    }
    private void StopHooks(){if(keyboardHandle!=0){UnhookWindowsHookEx(keyboardHandle);keyboardHandle=0;}if(mouseHandle!=0){UnhookWindowsHookEx(mouseHandle);mouseHandle=0;}}
    private nint KeyboardHookProc(int code,nint message,nint data)
    {
        if(code>=0)
        {
            var msg=message.ToInt32();var keyData=Marshal.PtrToStructure<KeyboardData>(data);var key=keyData.VirtualKey;
            var down=msg is WmKeyDown or WmSysKeyDown;var up=msg is WmKeyUp or WmSysKeyUp;
            if(draining)
            {
                if(up&&key==unlockVirtualKey)Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>Unlock()));
                return 1;
            }
            var unlockKey=key==VkEscape||keyData.ScanCode==ScanEscape||key==VkMinus||keyData.ScanCode==ScanTopRowMinus||key==VkSubtract;
            if(unlockKey)
            {
                if(down&&!unlockKeyDown){unlockKeyDown=true;unlockVirtualKey=key;clock.Restart();}
                if(up&&unlockVirtualKey==key){unlockKeyDown=false;unlockVirtualKey=0;clock.Reset();}
            }
            else if((down||up)&&!IsModifierKey(key)){unlockKeyDown=false;unlockVirtualKey=0;clock.Reset();}
            return 1;
        }
        return CallNextHookEx(keyboardHandle,code,message,data);
    }
    private nint MouseHookProc(int code,nint message,nint data)
    {
        // A wipe must not leak movement, button-up, or horizontal-wheel events
        // just because they were not among the original click messages.
        if(code>=0)return 1;
        return CallNextHookEx(mouseHandle,code,message,data);
    }
    private void Tick()
    {
        if(draining){if(DateTime.UtcNow-drainStarted>=TimeSpan.FromSeconds(2))Unlock();return;}
        if(DateTime.UtcNow-lockStarted>=TimeSpan.FromMinutes(1)){Unlock("Клавиатура автоматически разблокирована через минуту.");return;}
        if(unlockKeyDown&&clock.Elapsed.TotalSeconds>=HoldSeconds){BeginDrain();return;}
        if(overlay is not null)overlay.Progress=unlockKeyDown?Math.Min(1,clock.Elapsed.TotalSeconds/HoldSeconds):0;
    }

    private void BeginDrain()
    {
        if(!IsLocked)return;
        IsLocked=false;draining=true;drainStarted=DateTime.UtcNow;unlockKeyDown=false;clock.Reset();
        if(mouseHandle!=0){UnhookWindowsHookEx(mouseHandle);mouseHandle=0;}
        overlay?.Close();overlay=null;MainWindow.SetCleaningMode(false);
        StatusChanged?.Invoke("Разблокировано.");
    }

    private static bool IsModifierKey(uint key)=>key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private delegate nint LowLevelProc(int code,nint message,nint data);
    [StructLayout(LayoutKind.Sequential)]private struct KeyboardData{public uint VirtualKey,ScanCode,Flags,Time;public nint ExtraInfo;}
    [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookEx(int id,LowLevelProc callback,nint module,uint threadId);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? moduleName);

    private sealed class OverlayWindow:Window
    {
        private readonly Border bar;
        public double Progress{set=>bar.Width=actualWidth*value;}
        private readonly double actualWidth;
        public OverlayWindow()
        {
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;Background=new SolidColorBrush(Color.FromArgb(247,8,9,12));
            Left=SystemParameters.VirtualScreenLeft;Top=SystemParameters.VirtualScreenTop;Width=SystemParameters.VirtualScreenWidth;Height=SystemParameters.VirtualScreenHeight;actualWidth=360;
            var text=new TextBlock{Text="ВВОД ЗАБЛОКИРОВАН",Foreground=Brushes.White,FontSize=24,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center};
            var hint=new TextBlock{Text="Удерживайте Esc или −, чтобы разблокировать",Foreground=new SolidColorBrush(Color.FromRgb(164,167,176)),FontSize=14,Margin=new Thickness(0,12,0,18),HorizontalAlignment=HorizontalAlignment.Center};
            bar=new Border{Height=4,Width=0,Background=new SolidColorBrush(Color.FromRgb(139,92,246)),HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(2)};
            var progress=new Border{Width=actualWidth,Height=4,Background=new SolidColorBrush(Color.FromRgb(38,40,47)),CornerRadius=new CornerRadius(2),Child=bar,HorizontalAlignment=HorizontalAlignment.Center};
            var panel=new StackPanel{Width=actualWidth,VerticalAlignment=VerticalAlignment.Center};panel.Children.Add(text);panel.Children.Add(hint);panel.Children.Add(progress);Content=panel;
        }
    }
}

