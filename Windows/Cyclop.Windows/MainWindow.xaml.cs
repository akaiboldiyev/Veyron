using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace Cyclop.Windows;

public partial class MainWindow : Window
{
    private const double CollapsedShapeWidth=192;
    private const double ExpandedShapeWidth=644;
    private const double CollapsedTopRadius=6;
    private const double OpenTopRadius=12;
    private const double CollapsedBottomRadius=9;
    private const double OpenBottomRadius=22;
    private const int WmNcHitTest=0x0084;
    private const int WmClipboardUpdate=0x031D;
    private const int HtClient=1;
    private const int HtTransparent=-1;
    private static readonly List<MainWindow> panels=new();
    private static bool sessionAvailable=true;
    private static bool screenshotEventAttached;
    private static readonly string[] leftRailIds=["media","shelf","clipboard","snippets","calendar","translate"];
    private static readonly string[] rightRailIds=["notes","currency","teleprompter","utilities","settings"];
    private readonly DisplayInfo? displayInfo;
    private readonly bool primaryPanel;
    private readonly string panelKey;
    private readonly NotifyCollectionChangedEventHandler headerCollectionChanged;
    private readonly Action headerTick;
    private readonly Action<MediaSnapshot> mediaUpdated;
    private readonly Action currencyUpdated;
    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer closeHitRegionTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer openTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer pointerTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private HwndSource? hwndSource;
    private int transitionVersion;
    private int paneTransitionVersion;
    private bool panelExpanded;
    private bool holdOpen;
    private bool suppressHoverOpenUntilPointerLeaves;
    private bool closingHitRegion;
    private bool isReceivingDrag;
    private bool mediaEqualizerRunning;
    private double closingHitHeight;
    private string currentTabId="media";
    private readonly Dictionary<string, (string Icon, string Title)> tabs = new()
    {
        ["media"]=("\uE8D6","Музыка"),["shelf"]=("\uE8F1","Полка"),["clipboard"]=("\uE8C8","Буфер"),
        ["snippets"]=("\uE840","Заготовки"),["calendar"]=("\uE787","Календарь"),["translate"]=("\uE774","Перевод"),
        ["currency"]=("\uE8C7","Валюта"),["notes"]=("\uE70F","Заметки"),["teleprompter"]=("\uE736","Телесуфлёр"),
        ["utilities"]=("\uE90F","Утилиты"),["settings"]=("\uE713","Настройки")
    };

    public MainWindow():this(null,true){}

    private MainWindow(DisplayInfo? display,bool primary)
    {
        displayInfo=display;primaryPanel=primary;panelKey=display?.DeviceName??"primary";
        InitializeComponent();
#if DEBUG
        // Debug builds can expose a normal taskbar window only for live QA;
        // production keeps the notch as a floating tool window.
        ShowActivated=true;
        if(Environment.GetEnvironmentVariable("CYCLOP_QA_VISIBLE_WINDOW")=="1")ShowInTaskbar=true;
#endif
        Panel.SizeChanged+=(_,_)=>{UpdatePanelClip();CenterPanelVisuals();};
        Panel.Effect=new DropShadowEffect{Color=Colors.Black,Opacity=0,BlurRadius=18,ShadowDepth=8,Direction=270,RenderingBias=RenderingBias.Performance};
        SourceInitialized+=(_,_)=>AttachInputRegion();
        Panel.PreviewMouseLeftButtonDown+=(_,_)=>
        {
            if(!NeedsKeyboard(currentTabId)||HasTextInputUnderPointer(Pane))return;
            var tab=currentTabId;
            Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>
            {
                if(currentTabId!=tab||!IsVisible||PanelContent.Visibility!=Visibility.Visible)return;
                var input=FindInput(Pane);if(input is null)return;
                Activate();input.Focus();Keyboard.Focus(input);
            }));
        };
        headerCollectionChanged=(_,_)=>Dispatcher.BeginInvoke(new Action(UpdateHeader));
        headerTick=()=>Dispatcher.BeginInvoke(new Action(UpdateHeader));
        mediaUpdated=_=>Dispatcher.BeginInvoke(new Action(UpdateHeader));
        currencyUpdated=()=>Dispatcher.BeginInvoke(new Action(UpdateHeader));
#if !DEBUG
        ShowActivated=false;
#endif
        Width=700;
        Height=444;
        Panel.Width=CollapsedShapeWidth;
        Panel.Height=DataStore.Current.FullSizeDrawnNotch?24:8;
        PanelContent.Visibility=Visibility.Collapsed;
        PanelContent.Opacity=0;
        SizeChanged+=(_,_)=>{CenterPanelVisuals();if(IsLoaded)PositionAtTop(ActualWidth);};
        CenterPanelVisuals();
        // Escape folds the notch once controls have had a chance to handle it
        // first (search fields use it to clear their query). This mirrors the
        // Mac panel's explicit dismiss behavior without stealing Escape from
        // an editor that already consumed the key.
        AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>
        {
            if(e.Key!=Key.Escape||!panelExpanded||e.Handled||e.OriginalSource is TextBoxBase)return;
            suppressHoverOpenUntilPointerLeaves=true;
            Collapse();
            e.Handled=true;
        }),true);
        if(!screenshotEventAttached){DataStore.Current.ScreenshotReceived+=OnScreenshotReceived;screenshotEventAttached=true;}
        AllowDrop=true;
        DragEnter+=Window_DragEnter;DragLeave+=Window_DragLeave;Drop+=Window_Drop;
        BuildQuickMenu();
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); Collapse(); };
        closeHitRegionTimer.Tick+=(_,_)=>{closeHitRegionTimer.Stop();closingHitRegion=false;};
        openTimer.Tick += (_, _) => { openTimer.Stop(); OpenPanel(); };
        pointerTimer.Tick+=(_,_)=>UpdatePointerState();
        BuildRail();
        SelectTabCore(currentSharedTab);
        panels.Add(this);
        DataStore.Current.TeleprompterStateChanged+=OnTeleprompterStateChanged;
        Deactivated+=(_,_)=>Keyboard.ClearFocus();
        DataStore.Current.Shelf.CollectionChanged+=headerCollectionChanged;
        DataStore.Current.ClipboardHistory.CollectionChanged+=headerCollectionChanged;
        DataStore.Current.Snippets.CollectionChanged+=headerCollectionChanged;
        DataStore.Current.Notes.CollectionChanged+=headerCollectionChanged;
        DataStore.Current.CalendarEvents.CollectionChanged+=headerCollectionChanged;
        DataStore.Current.CalendarClockTick+=headerTick;
        MediaSessionService.Current.Updated+=mediaUpdated;
        CurrencyService.Current.RatesUpdated+=currencyUpdated;
        Closed+=(_,_)=>
        {
            pointerTimer.Stop();closeTimer.Stop();openTimer.Stop();closeHitRegionTimer.Stop();
            hwndSource?.RemoveHook(WindowHook);
            if(primaryPanel&&hwndSource is not null)RemoveClipboardFormatListener(hwndSource.Handle);
            hwndSource=null;
            DataStore.Current.TeleprompterStateChanged-=OnTeleprompterStateChanged;
            DataStore.Current.Shelf.CollectionChanged-=headerCollectionChanged;
            DataStore.Current.ClipboardHistory.CollectionChanged-=headerCollectionChanged;
            DataStore.Current.Snippets.CollectionChanged-=headerCollectionChanged;
            DataStore.Current.Notes.CollectionChanged-=headerCollectionChanged;
            DataStore.Current.CalendarEvents.CollectionChanged-=headerCollectionChanged;
            DataStore.Current.CalendarClockTick-=headerTick;
            MediaSessionService.Current.Updated-=mediaUpdated;
            CurrencyService.Current.RatesUpdated-=currencyUpdated;
            DataStore.Current.PauseTeleprompterOwnedBy(panelKey,true);panels.Remove(this);DataStore.Current.SetPanelExpanded(panelKey,false);
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTop();
        Collapse(immediate:true);
#if DEBUG
        if(Environment.GetEnvironmentVariable("CYCLOP_QA_OPEN_PANEL")=="1")
        {
            holdOpen=true;
            Dispatcher.BeginInvoke(new Action(OpenPanel));
        }
#endif
        var handle = new WindowInteropHelper(this).Handle;
#if DEBUG
        if(Environment.GetEnvironmentVariable("CYCLOP_QA_VISIBLE_WINDOW")!="1")NativeWindow.MakeToolWindow(handle);
#else
        NativeWindow.MakeToolWindow(handle);
#endif
        if(!sessionAvailable)Hide();
        if(primaryPanel)RefreshDisplays();
    }

    private void PositionAtTop(double? actualWidth=null)
    {
        var centeredWidth=actualWidth??Width;
        var display=displayInfo??DisplayInfoProvider.GetDisplays().FirstOrDefault(x=>x.IsPrimary);
        if(display is not null)
        {
            Left=display.Left+(display.Width-centeredWidth)/2;Top=display.Top;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - centeredWidth) / 2;
            Top = SystemParameters.VirtualScreenTop;
        }
    }

    private void CenterPanelVisuals()
    {
        Canvas.SetLeft(Panel,(ActualWidth-Panel.Width)/2);
        Canvas.SetLeft(PanelContent,(ActualWidth-PanelContent.Width)/2);
    }

    private void AttachInputRegion()
    {
        hwndSource=PresentationSource.FromVisual(this) as HwndSource;
        hwndSource?.AddHook(WindowHook);
        if(primaryPanel&&hwndSource is not null)AddClipboardFormatListener(hwndSource.Handle);
        pointerTimer.Start();
    }

    private IntPtr WindowHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==WmClipboardUpdate&&primaryPanel)
        {
            DataStore.Current.CaptureClipboard();
            return IntPtr.Zero;
        }
        if(message!=WmNcHitTest)return IntPtr.Zero;
        var packed=lParam.ToInt64();
        var screenPoint=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
        var point=PointFromScreen(screenPoint);
        handled=true;
        return IsInteractivePoint(point)?new IntPtr(HtClient):new IntPtr(HtTransparent);
    }

    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private bool IsInteractivePoint(Point point)
    {
        if(isReceivingDrag)return true;
        if(closingHitRegion)
            return point.X>=ActualWidth/2-ExpandedShapeWidth/2&&point.X<=ActualWidth/2+ExpandedShapeWidth/2&&point.Y>=0&&point.Y<=closingHitHeight;
        var width=Panel.Width;
        var height=Panel.Height;
        var progress=Math.Clamp((width-CollapsedShapeWidth)/(ExpandedShapeWidth-CollapsedShapeWidth),0,1);
        if(Panel.Effect is DropShadowEffect shadow)shadow.Opacity=progress*.5;
        var shoulder=CollapsedTopRadius+(OpenTopRadius-CollapsedTopRadius)*progress;
        var bodyWidth=Math.Max(0,width-2*shoulder);
        var left=(ActualWidth-width)/2+shoulder;
        return point.X>=left&&point.X<=left+bodyWidth&&point.Y>=0&&point.Y<=height;
    }

    private void UpdatePointerState()
    {
        // The panel is click-through almost everywhere. Once the pointer leaves
        // its visible shape Windows routes WM_MOUSEMOVE to the window below it,
        // so WPF's cached Mouse.GetPosition can stay parked on the notch and
        // never start the close timer. The Mac watcher reads a global pointer
        // location; poll the equivalent here and convert it back to panel space.
        var point=GetGlobalPointerPosition();
        var nearTop=point.Y>=-4&&point.Y<180;
        pointerTimer.Interval=TimeSpan.FromMilliseconds(nearTop?16.667:250);
        if(panelExpanded)
        {
            var bodyWidth=Math.Max(0,Panel.Width-2*OpenTopRadius);
            var left=(ActualWidth-bodyWidth)/2;
            var height=Panel.Height+12;
            var inside=point.X>=left-12&&point.X<=left+bodyWidth+12&&point.Y>=-1&&point.Y<=height;
            if(inside)closeTimer.Stop();
            else if(!holdOpen&&!closeTimer.IsEnabled){openTimer.Stop();closeTimer.Start();}
        }
        else if(isReceivingDrag)
        {
            closeTimer.Stop();openTimer.Stop();
        }
        else
        {
            var targetWidth=Panel.Width;
            var left=(ActualWidth-targetWidth)/2;
            var inside=point.X>=left&&point.X<=left+targetWidth&&point.Y>=-1&&point.Y<=Panel.Height;
            // After Escape the pointer can still sit inside the panel's former
            // expanded footprint, just beyond the collapsed notch's 8px edge.
            // Keep hover reopening suppressed until it has left that footprint.
            if(suppressHoverOpenUntilPointerLeaves)
            {
                var expandedLeft=(ActualWidth-ExpandedShapeWidth)/2;
                var insideExpandedEnvelope=point.X>=expandedLeft&&point.X<=expandedLeft+ExpandedShapeWidth&&point.Y>=-1&&point.Y<=ExpandedHeight(currentTabId);
                if(insideExpandedEnvelope){openTimer.Stop();return;}
                suppressHoverOpenUntilPointerLeaves=false;
            }
            if(inside)
            {
                closeTimer.Stop();
                if(!openTimer.IsEnabled)openTimer.Start();
            }
            else
            {
                openTimer.Stop();
            }
        }
    }

    private Point GetGlobalPointerPosition()
    {
        if(GetCursorPos(out var cursor))return PointFromScreen(new Point(cursor.X,cursor.Y));
        return Mouse.GetPosition(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool GetCursorPos(out NativePoint point);

    private void BuildRail()
    {
        LeftRail.Children.Clear();RightRail.Children.Clear();
        AddRailButtons(LeftRail,leftRailIds);AddRailButtons(RightRail,rightRailIds);
        UpdateRailSelection();
        if(DataStore.Current.HiddenTabs.Contains(currentTabId))SelectTab("settings");
    }

    private void UpdatePanelClip()
    {
        var width=Panel.ActualWidth;var height=Panel.ActualHeight;
        if(width<=0||height<=0)return;
        var progress=Math.Clamp((width-CollapsedShapeWidth)/(ExpandedShapeWidth-CollapsedShapeWidth),0,1);
        var shoulder=CollapsedTopRadius+(OpenTopRadius-CollapsedTopRadius)*progress;
        var bottom=CollapsedBottomRadius+(OpenBottomRadius-CollapsedBottomRadius)*progress;
        var left=shoulder;var right=width-shoulder;var radius=Math.Min(bottom,Math.Min((right-left)/2,height));
        var geometry=new StreamGeometry();
        using(var path=geometry.Open())
        {
            path.BeginFigure(new Point(0,0),true,true);
            path.QuadraticBezierTo(new Point(left,0),new Point(left,shoulder),true,false);
            path.LineTo(new Point(left,height-radius),true,false);
            path.QuadraticBezierTo(new Point(left,height),new Point(left+radius,height),true,false);
            path.LineTo(new Point(right-radius,height),true,false);
            path.QuadraticBezierTo(new Point(right,height),new Point(right,height-radius),true,false);
            path.LineTo(new Point(right,shoulder),true,false);
            path.QuadraticBezierTo(new Point(right,0),new Point(width,0),true,false);
        }
        geometry.Freeze();Panel.Clip=geometry;
        PanelContent.Clip=new RectangleGeometry(new Rect(0,0,PanelContent.Width,height));
        CenterPanelVisuals();
    }

    private void AddRailButtons(System.Windows.Controls.Panel rail,IEnumerable<string> ids)
    {
        var visibleIds=ids.Where(id=>!DataStore.Current.HiddenTabs.Contains(id)&&tabs.ContainsKey(id)).ToArray();
        var railHeight=visibleIds.Length*24+Math.Max(0,visibleIds.Length-1)*4;
        rail.Margin=new Thickness(0,Math.Max(0,(170-railHeight)/2),0,0);
        for(var index=0;index<visibleIds.Length;index++)
        {
            var id=visibleIds[index];var item=tabs[id];
            var button=new Button{Content=item.Icon,Tag=id,ToolTip=Localization.Text(item.Title),Style=(Style)Application.Current.FindResource("RailButton"),Margin=new Thickness(0,0,0,index<visibleIds.Length-1?4:0),Padding=new Thickness(0),Width=30,Height=24,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1,1)};
            button.Click+=(_,_)=>SelectTab(id);
            var dwell=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
            dwell.Tick+=(_,_)=>{dwell.Stop();SelectTab(id);};
            button.MouseEnter+=(_,_)=>{button.Background=Equals(button.Tag,currentTabId)?(Brush)Application.Current.FindResource("SurfaceHoverBrush"):(Brush)Application.Current.FindResource("SurfaceBrush");AnimateRailHover(button,1.15);dwell.Start();};
            button.MouseLeave+=(_,_)=>{dwell.Stop();AnimateRailHover(button,1);UpdateRailButton(button);};
            rail.Children.Add(button);
        }
    }

    private void UpdateRailSelection()
    {
        foreach(var button in LeftRail.Children.OfType<Button>().Concat(RightRail.Children.OfType<Button>()))UpdateRailButton(button);
    }

    private void UpdateRailButton(Button button)
    {
        var selected=Equals(button.Tag,currentTabId);
        button.Background=selected?(Brush)Application.Current.FindResource("SurfaceHoverBrush"):Brushes.Transparent;
        button.Foreground=selected?Brushes.White:(Brush)Application.Current.FindResource("TertiaryBrush");
    }

    private static void AnimateRailHover(Button button,double scale)
    {
        if(button.RenderTransform is not ScaleTransform transform)return;
        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        transform.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(transform.ScaleX,scale,TimeSpan.FromMilliseconds(160)){EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(transform.ScaleY,scale,TimeSpan.FromMilliseconds(160)){EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
    }

    private void BuildQuickMenu()
    {
        var menu=new ContextMenu();
        var open=new MenuItem{Header=Localization.Text("Открыть панель")};open.Click+=(_,_)=>OpenPanel();menu.Items.Add(open);
        var hide=new MenuItem{Header=Localization.Text("Скрыть содержимое"),IsCheckable=true,IsChecked=DataStore.Current.HideSensitiveContents};
        hide.Click+=(_,_)=>{DataStore.Current.SetHideSensitiveContents(hide.IsChecked);RefreshPrivacy();};
        menu.Opened+=(_,_)=>hide.IsChecked=DataStore.Current.HideSensitiveContents;menu.Items.Add(hide);
        menu.Items.Add(new Separator());var quit=new MenuItem{Header=Localization.Text("Выход из Veyron")};quit.Click+=(_,_)=>Application.Current.Shutdown();menu.Items.Add(quit);
        Panel.ContextMenu=menu;
    }

    private static void RefreshRails(){foreach(var panel in panels.ToArray())panel.BuildRail();}
    private static void RefreshPrivacy(){foreach(var panel in panels.ToArray())panel.RefreshPanePreservingInput();}
    internal static void ApplyPrivacyNow()=>RefreshPrivacy();
    private static void OnScreenshotReceived()
    {
        var panel=panels.FirstOrDefault(x=>x.primaryPanel);
        if(panel is null||DataStore.Current.HiddenTabs.Contains("shelf"))return;
        if(panel.Pane.IsKeyboardFocusWithin&&Keyboard.FocusedElement is TextBoxBase)return;
        panel.SelectTab("shelf");
    }

    internal static void RefreshDisplayTopology()
    {
        RefreshDisplays();
        foreach(var panel in panels.Where(x=>x.primaryPanel).ToArray())
        {
            if(!panel.panelExpanded)panel.Collapse(immediate:true);else panel.PositionAtTop();
        }
    }

    private static void RefreshDisplays()
    {
        var targets=DataStore.Current.ShowOnAllDisplays?DisplayInfoProvider.GetDisplays().Where(x=>!x.IsPrimary).ToList():new List<DisplayInfo>();
        foreach(var old in panels.Where(x=>!x.primaryPanel&&x.displayInfo is not null).ToArray())
        {
            var target=targets.FirstOrDefault(x=>string.Equals(x.DeviceName,old.displayInfo!.DeviceName,StringComparison.OrdinalIgnoreCase));
            if(target is null||target!=old.displayInfo)old.Close();
        }
        foreach(var target in targets)
        {
            if(panels.Any(x=>!x.primaryPanel&&x.displayInfo==target))continue;
            var panel=new MainWindow(target,false);if(sessionAvailable)panel.Show();panel.SelectTabCore(currentSharedTab);
        }
    }
    private static string currentSharedTab="media";

    internal static void OpenFromTray()
    {
        if(!sessionAvailable)return;
        var panel=panels.FirstOrDefault(x=>x.primaryPanel);
        if(panel is null)return;
        panel.Dispatcher.Invoke(panel.OpenPanel);
    }

    internal static void SetSessionAvailable(bool available)
    {
        if(sessionAvailable==available)return;
        sessionAvailable=available;
        foreach(var panel in panels.ToArray())
        {
            if(!available)
            {
                if(panel.Pane.Content is TeleprompterView prompter)prompter.Suspend();
                panel.closeTimer.Stop();
                panel.Collapse();
                panel.Hide();
            }
            else
            {
                panel.Show();
                panel.PositionAtTop();
                panel.Collapse();
            }
        }
    }

    internal static void SetCleaningMode(bool active)
    {
        foreach(var panel in panels.ToArray())
        {
            panel.Dispatcher.Invoke(() =>
            {
                if(active)
                {
                    panel.closeTimer.Stop();
                    panel.Collapse(true);
                    if(panel.Pane.Content is TeleprompterView prompter)prompter.Suspend();
                    panel.Hide();
                }
                else if(sessionAvailable&&Application.Current?.Dispatcher.HasShutdownStarted!=true)
                {
                    panel.Show();
                    panel.PositionAtTop();
                    panel.Collapse(true);
                }
            });
        }
    }

    private void SelectTab(string id)
    {
        currentSharedTab=id;
        foreach(var panel in panels.ToArray())if(panel!=this)panel.SelectTabCore(id);
        SelectTabCore(id,true);
    }

    private void SelectTabCore(string id,bool takeKeyboard=false)
    {
        if(!tabs.ContainsKey(id)||DataStore.Current.HiddenTabs.Contains(id))id="settings";
        var animatePane=panelExpanded&&currentTabId!=id;
        if(currentTabId!=id&&NeedsKeyboard(currentTabId))Keyboard.ClearFocus();
        if(currentTabId=="notes"&&id!="notes")DataStore.Current.LeaveNotes();
        if(id=="shelf")DataStore.Current.RefreshShelfFromDisk();
        currentTabId=id;
        UpdateRailSelection();
        DataStore.Current.SetActiveTab(id);
        if(id=="snippets")DataStore.Current.ReloadSnippets();
        SetHeaderText(ActiveTitle,Localization.Text(tabs[id].Title).ToUpperInvariant(),animatePane);
        var paneTransition=++paneTransitionVersion;
        if(animatePane)
        {
            var previousContent=Pane.Content;
            var previousOpacity=Pane.Opacity;
            var previousTransform=Pane.RenderTransform;
            Pane.Content=null;
            PreviousPane.Content=previousContent;
            PreviousPane.Visibility=Visibility.Visible;
            PreviousPane.Opacity=previousOpacity;
            PreviousPane.RenderTransform=previousTransform;
            Pane.BeginAnimation(OpacityProperty,null);
            Pane.Opacity=0;
            Pane.RenderTransform=new ScaleTransform(.97,.97);
        }
        else
        {
            PreviousPane.Content=null;
            PreviousPane.Visibility=Visibility.Collapsed;
        }
        Pane.Content = PaneFactory.Create(id,panelKey,SelectTab,RefreshRails,RefreshDisplayTopology,RefreshPrivacy);
        if(animatePane){AnimatePaneIn();AnimatePreviousPaneOut(paneTransition);}
        UpdateHeader();
        if(takeKeyboard&&NeedsKeyboard(id))
            Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>
            {
                if(currentTabId!=id||!IsVisible)return;
                Activate();
                var input=FindInput(Pane);
                if(input is null)return;
                input.Focus();input.CaretIndex=input.Text.Length;
                Keyboard.Focus(input);
            }));
        var contentHeight=ExpandedHeight(id);
        PanelContent.Height=contentHeight;
        if(panelExpanded)
        {
            var transition=++transitionVersion;
            AnimatePanelShape(ExpandedShapeWidth,contentHeight,null,transition);
        }
        OnTeleprompterStateChanged();
    }

    private static TextBox? FindInput(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is TextBox input)return input;
            var nested=FindInput(child);if(nested is not null)return nested;
        }
        return null;
    }

    private static void SetHeaderText(TextBlock target,string value,bool animate)
    {
        if(target.Text==value)return;
        target.BeginAnimation(OpacityProperty,null);
        target.Text=value;
        if(!animate){target.Opacity=1;return;}
        target.Opacity=0;
        target.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160))
        {
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},
            FillBehavior=FillBehavior.HoldEnd
        },HandoffBehavior.SnapshotAndReplace);
    }

    private void RefreshPanePreservingInput()
    {
        var keepKeyboardFocus=NeedsKeyboard(currentTabId)&&Pane.IsKeyboardFocusWithin;
        var oldInput=FindInput(Pane);
        var caret=oldInput?.CaretIndex??0;var selectionStart=oldInput?.SelectionStart??0;var selectionLength=oldInput?.SelectionLength??0;
        var oldLists=FindListBoxes(Pane);
        var selectedListIndex=Array.FindIndex(oldLists,x=>x.SelectedItem is not null);
        var selectedKey=selectedListIndex>=0?ListItemKey(oldLists[selectedListIndex].SelectedItem):null;
        SelectTabCore(currentTabId);
        Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>
        {
            var lists=FindListBoxes(Pane);
            if(selectedKey is not null&&selectedListIndex>=0&&selectedListIndex<lists.Length)
                lists[selectedListIndex].SelectedItem=lists[selectedListIndex].Items.Cast<object>().FirstOrDefault(x=>ListItemKey(x)==selectedKey);
            if(!keepKeyboardFocus)return;
            var input=FindInput(Pane);if(input is null)return;
            input.Focus();input.CaretIndex=Math.Min(caret,input.Text.Length);
            var start=Math.Min(selectionStart,input.Text.Length);input.Select(start,Math.Min(selectionLength,input.Text.Length-start));
            Keyboard.Focus(input);
        }));
    }

    private static ListBox[] FindListBoxes(DependencyObject root)
    {
        var result=new List<ListBox>();
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is ListBox list)result.Add(list);
            result.AddRange(FindListBoxes(child));
        }
        return result.ToArray();
    }

    private static string? ListItemKey(object? item)=>item switch
    {
        NoteItem note=>"note:"+note.Id,
        ClipboardEntry clip=>"clip:"+clip.Id,
        SnippetItem snippet=>"snippet:"+snippet.Id,
        CalendarEvent meeting=>"event:"+meeting.CalendarId+":"+meeting.StartsAt.Ticks+":"+meeting.Title,
        WindowsCalendarOption calendar=>"calendar:"+calendar.Id,
        string path=>"path:"+path,
        _=>null
    };

    private static bool NeedsKeyboard(string id)=>id is "translate" or "currency" or "snippets" or "notes";

    private static bool HasTextInputUnderPointer(DependencyObject root)
    {
        if(root is TextBoxBase input&&input.IsMouseOver)return true;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(HasTextInputUnderPointer(VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }

    private void UpdateHeader()
    {
        var media=MediaSessionService.Current.Snapshot;
        var hasMediaSource=currentTabId=="media"
            &&!string.IsNullOrWhiteSpace(media.Status)
            &&media.Status!="Нет активного медиаплеера"
            &&!media.Status.StartsWith("Медиадоступ Windows недоступен",StringComparison.Ordinal)
            &&!media.Status.StartsWith("Не удалось прочитать медиасессию",StringComparison.Ordinal);
        var hasTrack=!string.IsNullOrWhiteSpace(media.Title);
        MediaEqualizer.Visibility=currentTabId=="media"&&hasTrack?Visibility.Visible:Visibility.Collapsed;
        SetMediaEqualizer(currentTabId=="media"&&hasTrack&&media.IsPlaying);
        StatusText.Text=Localization.Text(currentTabId switch
        {
            "media" when hasMediaSource=>media.Status,
            "shelf"=>CountLabel(DataStore.Current.Shelf.Count),
            "clipboard"=>CountLabel(DataStore.Current.ClipboardHistory.Count),
            "snippets"=>CountLabel(DataStore.Current.Snippets.Count),
            "calendar"=>CalendarCountdown(),
            "currency"=>CurrencyService.Current.RateDateFor(DataStore.Current.SourceCurrency)??"",
            "notes"=>CountLabel(DataStore.Current.Notes.Count),
            _=>""
        });

        string CountLabel(int count)=>count>0?count.ToString(System.Globalization.CultureInfo.CurrentCulture):"";
    }

    private void SetMediaEqualizer(bool active)
    {
        if(mediaEqualizerRunning==active)return;
        mediaEqualizerRunning=active;
        var bars=new (System.Windows.Shapes.Rectangle Bar,double Low,double High,double DelayMs)[]
        {
            (MediaBar1,4,10,0),(MediaBar2,7,3,120),(MediaBar3,5,8,240)
        };
        foreach(var item in bars)
        {
            if(!active)
            {
                item.Bar.BeginAnimation(FrameworkElement.HeightProperty,null);
                item.Bar.Height=item.Low;
                continue;
            }
            item.Bar.Height=item.Low;
            var animation=new DoubleAnimation(item.Low,item.High,TimeSpan.FromMilliseconds(440))
            {
                AutoReverse=true,
                RepeatBehavior=RepeatBehavior.Forever,
                BeginTime=TimeSpan.FromMilliseconds(item.DelayMs),
                EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}
            };
            item.Bar.BeginAnimation(FrameworkElement.HeightProperty,animation,HandoffBehavior.SnapshotAndReplace);
        }
    }

    private string CalendarCountdown()
    {
        var now=DateTime.Now;
        var meeting=DataStore.Current.CalendarEvents.Where(x=>(x.EndsAt??x.StartsAt)>now).OrderBy(x=>x.StartsAt).FirstOrDefault();
        if(meeting is null)return "";
        if(meeting.StartsAt<=now)return "Сейчас";
        var minutes=Math.Max(0,(int)Math.Ceiling((meeting.StartsAt-now).TotalMinutes));
        if(minutes==0)return "В любую минуту";
        if(minutes<60)return $"Через {minutes} мин.";
        var hours=minutes/60;var remainder=minutes%60;
        if(hours<24)return remainder==0?$"Через {hours} ч.":$"Через {hours} ч. {remainder} мин.";
        var days=hours/24;return days==1?"Завтра":$"Через {days} дн.";
    }

    private void OnTeleprompterStateChanged()
    {
        var wasHolding=holdOpen;
        holdOpen=DataStore.Current.TeleprompterIsRunning&&DataStore.Current.TeleprompterOwnerPanelKey==panelKey;
        if(wasHolding&&!holdOpen&&IsLoaded&&IsVisible)
        {
            if(!Panel.IsMouseOver&&!PanelContent.IsMouseOver){closeTimer.Stop();closeTimer.Start();}
        }
    }

    private void OpenPanel()
    {
        openTimer.Stop();
        closeTimer.Stop();
        closeHitRegionTimer.Stop();closingHitRegion=false;
        if(panelExpanded)return;
        var transition=++transitionVersion;
        panelExpanded=true;
        var contentHeight=ExpandedHeight(currentTabId);
        PanelContent.Height=contentHeight;
        PanelContent.BeginAnimation(OpacityProperty,null);
        PanelContent.Visibility=Visibility.Visible;
        AnimatePanelContentOpacity(1,transition,TimeSpan.FromMilliseconds(270),new DampedSpringEase{EasingMode=EasingMode.EaseOut});
        AnimatePanelShape(ExpandedShapeWidth,contentHeight,()=>
        {
            if(transition==transitionVersion){Panel.Width=ExpandedShapeWidth;Panel.Height=contentHeight;}
        },transition);
        DataStore.Current.SetPanelExpanded(panelKey,true);
    }

    private void Window_DragEnter(object sender,DragEventArgs e)
    {
        if(e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            isReceivingDrag=true;
            closeTimer.Stop();
            e.Effects=DragDropEffects.Copy;
            if(!DataStore.Current.HiddenTabs.Contains("shelf")){OpenPanel();SelectTab("shelf");}
        }
        else e.Effects=DragDropEffects.None;
        e.Handled=true;
    }
    private void Window_DragLeave(object sender,DragEventArgs e)
    {
        isReceivingDrag=false;
        if(!holdOpen&&panelExpanded&&!Panel.IsMouseOver&&!PanelContent.IsMouseOver){closeTimer.Stop();closeTimer.Start();}
    }
    private void Window_Drop(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(DataFormats.FileDrop) is string[] files)foreach(var path in files)DataStore.Current.AddShelf(path);
        isReceivingDrag=false;
        e.Handled=true;
    }

    private void Collapse(bool immediate=false)
    {
        Keyboard.ClearFocus();
        DataStore.Current.PauseTeleprompterOwnedBy(panelKey,true);
        DataStore.Current.SetPanelExpanded(panelKey,false);
        if(DataStore.Current.CoverRevealedPrivateItems())RefreshPrivacy();
        var collapsedHeight=DataStore.Current.FullSizeDrawnNotch?24:8;
        if(immediate)
        {
            closeTimer.Stop();openTimer.Stop();
            closeHitRegionTimer.Stop();closingHitRegion=false;
            panelExpanded=false;
            transitionVersion++;
            Panel.BeginAnimation(WidthProperty,null);Panel.BeginAnimation(HeightProperty,null);
            Panel.Width=CollapsedShapeWidth;Panel.Height=collapsedHeight;
            PanelContent.BeginAnimation(OpacityProperty,null);PanelContent.Opacity=0;PanelContent.Visibility=Visibility.Collapsed;
            CenterPanelVisuals();UpdatePanelClip();
            return;
        }
        if(!panelExpanded)return;
        closingHitHeight=Panel.Height;
        closingHitRegion=true;
        closeHitRegionTimer.Stop();closeHitRegionTimer.Start();
        panelExpanded=false;
        var transition=++transitionVersion;
        AnimatePanelContentOpacity(0,transition,TimeSpan.FromMilliseconds(270),new DampedSpringEase{EasingMode=EasingMode.EaseOut});
        AnimatePanelShape(CollapsedShapeWidth,collapsedHeight,()=>
        {
            if(panelExpanded||transition!=transitionVersion)return;
            Panel.Width=CollapsedShapeWidth;Panel.Height=collapsedHeight;
            PanelContent.BeginAnimation(OpacityProperty,null);PanelContent.Opacity=0;PanelContent.Visibility=Visibility.Collapsed;
            CenterPanelVisuals();UpdatePanelClip();
        },transition);
    }

    private double ExpandedHeight(string id)=>id is "teleprompter" or "settings"?400:208;

    private void AnimatePanelShape(double width,double height,Action? completed,int transition)
    {
        var duration=TimeSpan.FromMilliseconds(270);
        var easing=new DampedSpringEase{EasingMode=EasingMode.EaseOut};
        var widthAnimation=new DoubleAnimation(Panel.ActualWidth,width,duration){EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd};
        var heightAnimation=new DoubleAnimation(Panel.ActualHeight,height,duration){EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd};
        var widthDone=false;var heightDone=false;
        void CompleteIfReady()
        {
            if(!widthDone||!heightDone||transition!=transitionVersion)return;
            Panel.Width=width;Panel.Height=height;
            Panel.BeginAnimation(WidthProperty,null);Panel.BeginAnimation(HeightProperty,null);
            CenterPanelVisuals();UpdatePanelClip();
            completed?.Invoke();
        }
        widthAnimation.Completed+=(_,_)=>{widthDone=true;CompleteIfReady();};
        heightAnimation.Completed+=(_,_)=>{heightDone=true;CompleteIfReady();};
        Panel.BeginAnimation(WidthProperty,widthAnimation,HandoffBehavior.SnapshotAndReplace);
        Panel.BeginAnimation(HeightProperty,heightAnimation,HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimatePanelContentOpacity(double opacity,int transition,TimeSpan duration,IEasingFunction easing)
    {
        var from=PanelContent.Opacity;
        PanelContent.BeginAnimation(OpacityProperty,new DoubleAnimation(from,opacity,duration){EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimatePaneIn()
    {
        var scale=(ScaleTransform)Pane.RenderTransform;
        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        Pane.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)){BeginTime=TimeSpan.FromMilliseconds(40),EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.97,1,TimeSpan.FromMilliseconds(200)){BeginTime=TimeSpan.FromMilliseconds(40),EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.97,1,TimeSpan.FromMilliseconds(200)){BeginTime=TimeSpan.FromMilliseconds(40),EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimatePreviousPaneOut(int transition)
    {
        var easing=new CubicEase{EasingMode=EasingMode.EaseIn};
        PreviousPane.BeginAnimation(OpacityProperty,new DoubleAnimation(PreviousPane.Opacity,0,TimeSpan.FromMilliseconds(120)){EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
        if(PreviousPane.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scale.ScaleX,1.02,TimeSpan.FromMilliseconds(120)){EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scale.ScaleY,1.02,TimeSpan.FromMilliseconds(120)){EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        }
        var cleanup=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(125)};
        cleanup.Tick+=(_,_)=>
        {
            cleanup.Stop();
            if(transition!=paneTransitionVersion)return;
            PreviousPane.BeginAnimation(OpacityProperty,null);
            PreviousPane.Opacity=0;PreviousPane.Content=null;PreviousPane.Visibility=Visibility.Collapsed;
        };
        cleanup.Start();
    }

    private sealed class DampedSpringEase: EasingFunctionBase
    {
        private const double Response=.27;
        private const double Damping=.82;
        protected override double EaseInCore(double progress)
        {
            var omega=2*Math.PI/Response;
            var damped=omega*Math.Sqrt(1-Damping*Damping);
            static double Curve(double time,double natural,double damping,double frequency)
            {
                var coefficient=damping/Math.Sqrt(1-damping*damping);
                return 1-Math.Exp(-damping*natural*time)*(Math.Cos(frequency*time)+coefficient*Math.Sin(frequency*time));
            }
            var endpoint=Curve(Response,omega,Damping,damped);
            return Curve(Math.Clamp(progress,0,1)*Response,omega,Damping,damped)/endpoint;
        }
        protected override Freezable CreateInstanceCore()=>new DampedSpringEase();
    }

}

internal static class NativeWindow
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    internal static void MakeToolWindow(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        // Keep the window out of the task switcher; allow activation on click
        // so text fields in the scratch tabs can receive keyboard input.
        SetWindowLongPtr(hwnd, GwlExStyle, (nint)(style | WsExToolWindow));
    }
}
