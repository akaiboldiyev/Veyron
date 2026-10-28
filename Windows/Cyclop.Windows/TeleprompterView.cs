using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cyclop.Windows;

internal sealed class TeleprompterView : UserControl
{
    private readonly TextBox editor;
    private readonly TextBlock placeholder;
    private readonly TextBlock script;
    private readonly ScrollViewer reader;
    private readonly Grid editorLayer;
    private readonly Grid readerLayers;
    private readonly Slider speed;
    private readonly TextBlock speedReadout;
    private readonly Button playButton;
    private readonly Button doneButton;
    private readonly Grid readingControls=new(){VerticalAlignment=VerticalAlignment.Center};
    private readonly StackPanel editingControls=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly string ownerPanelKey;
    private bool syncing;
    private bool editing;

    public TeleprompterView(string ownerPanelKey)
    {
        this.ownerPanelKey=ownerPanelKey;
        Focusable = true;
        editing=string.IsNullOrWhiteSpace(DataStore.Current.Teleprompter);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor = new TextBox { Text = DataStore.Current.Teleprompter, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, MinHeight = 100,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0) };
        placeholder=new TextBlock{Text="Вставьте сюда сценарий",Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),FontSize=13,IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(24,12,24,0),Visibility=string.IsNullOrWhiteSpace(editor.Text)?Visibility.Visible:Visibility.Collapsed};
        script = new TextBlock { Text = editor.Text, Foreground = Brushes.White, FontSize = DataStore.Current.TeleprompterFontSize, FontWeight=FontWeights.Medium, TextWrapping = TextWrapping.Wrap, LineHeight=DataStore.Current.TeleprompterFontSize*1.34, Padding = new Thickness(18,115,18,115) };
        reader = new ScrollViewer { Content = script, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
        speed = new Slider { Minimum = 0.3, Maximum = 3, Value = DataStore.Current.TeleprompterSpeed, Width = 96, VerticalAlignment = VerticalAlignment.Center,Style=(Style)Application.Current.FindResource("MediaSeekSlider") };
        speedReadout=new TextBlock{Text=$"{DataStore.Current.TeleprompterSpeed:0.0}×",Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=11,FontFamily=new FontFamily("Consolas"),VerticalAlignment=VerticalAlignment.Center,Width=30,Margin=new Thickness(5,0,0,0)};
        editor.TextChanged += (_,_) => { if(syncing)return;DataStore.Current.SetTeleprompterScript(editor.Text);script.Text=editor.Text;reader.ScrollToTop();placeholder.Visibility=string.IsNullOrWhiteSpace(editor.Text)?Visibility.Visible:Visibility.Collapsed;if(doneButton is not null)doneButton.IsEnabled=!string.IsNullOrWhiteSpace(editor.Text); };
        var controls=new Grid{Margin=new Thickness(18,8,18,0),Height=44};
        readingControls.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});readingControls.ColumnDefinitions.Add(new ColumnDefinition());readingControls.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var transport=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var rewind=MakeButton("\uE72C",Rewind);rewind.ToolTip="Сначала";rewind.FontFamily=new FontFamily("Segoe MDL2 Assets");transport.Children.Add(rewind);
        playButton=MakeButton("\uE768",TogglePlayback);playButton.ToolTip="Читать / пауза";playButton.Width=32;playButton.Height=32;playButton.Style=(Style)Application.Current.FindResource("MediaPrimaryButton");playButton.FontFamily=new FontFamily("Segoe MDL2 Assets");transport.Children.Add(playButton);
        transport.Children.Add(speed);transport.Children.Add(speedReadout);Grid.SetColumn(transport,0);readingControls.Children.Add(transport);
        var sizeControls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var smaller=MakeButton("A−",(_,_)=>SetFontSize(DataStore.Current.TeleprompterFontSize-2));smaller.ToolTip="Уменьшить шрифт";sizeControls.Children.Add(smaller);
        var larger=MakeButton("A+",(_,_)=>SetFontSize(DataStore.Current.TeleprompterFontSize+2));larger.ToolTip="Увеличить шрифт";sizeControls.Children.Add(larger);
        var edit=MakeButton("\uE70F",Edit);edit.ToolTip="Править";edit.FontFamily=new FontFamily("Segoe MDL2 Assets");sizeControls.Children.Add(edit);
        Grid.SetColumn(sizeControls,2);readingControls.Children.Add(sizeControls);
        doneButton=MakeButton("Готово",(_,_)=>FinishEditing());doneButton.Style=(Style)Application.Current.Resources[typeof(Button)];doneButton.Width=74;doneButton.Height=28;doneButton.Padding=new Thickness(10,5,10,5);doneButton.FontSize=12;doneButton.IsEnabled=!string.IsNullOrWhiteSpace(editor.Text);editingControls.Children.Add(doneButton);
        controls.Children.Add(readingControls);controls.Children.Add(editingControls);
        speed.ValueChanged+=(_,_)=>{speedReadout.Text=$"{speed.Value:0.0}×";DataStore.Current.SetTeleprompter(speed.Value,DataStore.Current.TeleprompterFontSize);};
        var editorSurface=new Border{Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(26,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(7,10,7,10),Margin=new Thickness(14,0,14,0),Child=editor};
        editorLayer=new Grid();editorLayer.Children.Add(editorSurface);editorLayer.Children.Add(placeholder);
        readerLayers=new Grid();readerLayers.Children.Add(reader);readerLayers.Children.Add(Fade(true));readerLayers.Children.Add(Fade(false));
        Grid.SetRow(editorLayer,0);Grid.SetRow(readerLayers,0);Grid.SetRow(controls,1);
        root.Children.Add(editorLayer);root.Children.Add(readerLayers);root.Children.Add(controls);Content=root;ApplyMode();
        reader.PreviewMouseWheel+=(_,e)=>e.Handled=true;
        reader.SizeChanged+=(_,_)=>script.Padding=new Thickness(18,Math.Max(0,reader.ActualHeight/2),18,Math.Max(0,reader.ActualHeight/2));
        reader.ScrollChanged+=(_,_)=>DataStore.Current.UpdateTeleprompterExtent(ownerPanelKey,reader.ScrollableHeight);
        Loaded+=(_,_)=>
        {
            DataStore.Current.TeleprompterStateChanged+=OnSharedStateChanged;
            // Opening a second display must not rewind an active take on the
            // display that owns playback. A stopped take starts at the top.
            if(!DataStore.Current.TeleprompterIsRunning)DataStore.Current.RewindTeleprompter();
            OnSharedStateChanged();
            Dispatcher.BeginInvoke(new Action(()=>{DataStore.Current.UpdateTeleprompterExtent(ownerPanelKey,reader.ScrollableHeight);if(editing)editor.Focus();}));
        };
        PreviewKeyDown += (_,e) => { if(e.Key==Key.Escape){if(editing&&!string.IsNullOrWhiteSpace(editor.Text))FinishEditing();else{DataStore.Current.PauseTeleprompter(true);reader.ScrollToTop();}e.Handled=true;} };
        Unloaded+=(_,_)=>{DataStore.Current.TeleprompterStateChanged-=OnSharedStateChanged;DataStore.Current.PauseTeleprompterOwnedBy(ownerPanelKey);};
    }

    private static Button MakeButton(string label, RoutedEventHandler action) { var button=new Button { Content=label,Style=(Style)Application.Current.FindResource("MediaTransportButton"),Margin=new Thickness(0,0,6,0),Width=26,Height=26,Padding=new Thickness(0) }; button.Click+=action; return button; }
    private static Border Fade(bool top)
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(.5,0),EndPoint=new Point(.5,1)};
        var surface=Colors.Black;
        brush.GradientStops.Add(new GradientStop(top?surface:Colors.Transparent,0));
        brush.GradientStops.Add(new GradientStop(top?Colors.Transparent:surface,1));
        return new Border{Height=34,VerticalAlignment=top?VerticalAlignment.Top:VerticalAlignment.Bottom,Background=brush,IsHitTestVisible=false};
    }
    private void ApplyMode(){editorLayer.Visibility=editing?Visibility.Visible:Visibility.Collapsed;editor.Visibility=editing?Visibility.Visible:Visibility.Collapsed;readerLayers.Visibility=editing?Visibility.Collapsed:Visibility.Visible;reader.Visibility=editing?Visibility.Collapsed:Visibility.Visible;readingControls.Visibility=editing?Visibility.Collapsed:Visibility.Visible;editingControls.Visibility=editing?Visibility.Visible:Visibility.Collapsed;}
    private void SetFontSize(double value){var next=Math.Clamp(value,18,64);script.FontSize=next;script.LineHeight=next*1.34;DataStore.Current.SetTeleprompter(speed.Value,next);}
    private void TogglePlayback(object? sender,RoutedEventArgs e){if(DataStore.Current.TeleprompterIsRunning)Pause(sender,e);else Start(sender,e);}
    private void Start(object? sender, RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(editor.Text))return;
        script.Text=editor.Text;editing=false;ApplyMode();
        DataStore.Current.UpdateTeleprompterExtent(ownerPanelKey,reader.ScrollableHeight);
        DataStore.Current.StartTeleprompter(ownerPanelKey,reader.ScrollableHeight);Focus();
    }
    private void Pause(object? sender, RoutedEventArgs e)=>DataStore.Current.PauseTeleprompter();
    internal void Suspend()=>DataStore.Current.PauseTeleprompter(true);
    private void Rewind(object? sender, RoutedEventArgs e)=>DataStore.Current.RewindTeleprompter();
    private void Edit(object? sender,RoutedEventArgs e){DataStore.Current.PauseTeleprompter();editing=true;ApplyMode();editor.Focus();}
    private void FinishEditing(){if(string.IsNullOrWhiteSpace(editor.Text))return;DataStore.Current.PauseTeleprompter(true);script.Text=editor.Text;editing=false;ApplyMode();Focus();}
    private void OnSharedStateChanged()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(OnSharedStateChanged));return;}
        var store=DataStore.Current;
        syncing=true;
        if(editor.Text!=store.Teleprompter)editor.Text=store.Teleprompter;
        placeholder.Visibility=string.IsNullOrWhiteSpace(store.Teleprompter)?Visibility.Visible:Visibility.Collapsed;
        if(script.Text!=store.Teleprompter)script.Text=store.Teleprompter;
        if(Math.Abs(speed.Value-store.TeleprompterSpeed)>.0001)speed.Value=store.TeleprompterSpeed;
        script.FontSize=store.TeleprompterFontSize;
        script.LineHeight=store.TeleprompterFontSize*1.34;
        speedReadout.Text=$"{store.TeleprompterSpeed:0.0}×";
        playButton.Content=store.TeleprompterIsRunning?"\uE769":"\uE768";
        if(doneButton is not null)doneButton.IsEnabled=!string.IsNullOrWhiteSpace(store.Teleprompter);
        syncing=false;
        var target=Math.Clamp(store.TeleprompterOffset,0,reader.ScrollableHeight);
        if(Math.Abs(reader.VerticalOffset-target)>.5)reader.ScrollToVerticalOffset(target);
    }
}
