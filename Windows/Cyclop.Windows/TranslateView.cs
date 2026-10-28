using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cyclop.Windows;

internal sealed class TranslateView:UserControl
{
    private readonly TextBox input=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=100,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Foreground=Brushes.White,CaretBrush=new SolidColorBrush(Color.FromRgb(154,157,167))};
    private readonly TextBox output=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=100,IsReadOnly=true,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Foreground=Brushes.White};
    private readonly Grid columns=new();
    private readonly TextBlock status=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
    private readonly TextBlock sourceTitle=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=11,Margin=new Thickness(0,0,0,5)};
    private readonly TextBlock targetTitle=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=11,Margin=new Thickness(0,0,0,5)};
    private readonly Button clearButton=new(){Content="×",Visibility=Visibility.Collapsed,Padding=new Thickness(4,0,4,0),Margin=new Thickness(4,0,0,4)};
    private readonly Button copyButton=new(){Content="Копировать",Visibility=Visibility.Collapsed,Padding=new Thickness(4,0,4,0),Margin=new Thickness(4,0,0,4)};
    private readonly Button retryButton=new(){Content="Повторить",Visibility=Visibility.Collapsed,Padding=new Thickness(6,2,6,2),Margin=new Thickness(8,4,0,0)};
    private readonly Button installButton=new(){Content="Установить языки…",Visibility=Visibility.Collapsed,Padding=new Thickness(7,3,7,3),Margin=new Thickness(8,0,0,0),FontSize=10};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(450)};
    private int generation;
    private bool installing;
    private bool syncingState;
    public TranslateView()
    {
        input.Text=DataStore.Current.TranslationInput;output.Text=DataStore.Current.TranslationOutput;
        installButton.Click+=async(_,_)=>await InstallAsync();
        columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left=BuildColumn(sourceTitle,input,clearButton);
        var right=BuildColumn(targetTitle,output,copyButton);
        left.Margin=new Thickness(0,0,5,0);right.Margin=new Thickness(5,0,0,0);
        left.Background=new SolidColorBrush(Color.FromArgb(20,255,255,255));left.BorderBrush=new SolidColorBrush(Color.FromArgb(26,255,255,255));left.BorderThickness=new Thickness(1);left.CornerRadius=new CornerRadius(10);left.Padding=new Thickness(10);
        Grid.SetColumn(left,0);Grid.SetColumn(right,1);columns.Children.Add(left);columns.Children.Add(right);
        clearButton.Click+=(_,_)=>{DataStore.Current.SetTranslationInput("");DataStore.Current.SetTranslationOutput("");};
        copyButton.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(output.Text))Clipboard.SetText(output.Text);};
        retryButton.Click+=(_,_)=>QueueTranslation();
        var statusRow=new DockPanel{LastChildFill=true,Margin=new Thickness(2,5,2,0)};DockPanel.SetDock(installButton,Dock.Right);DockPanel.SetDock(retryButton,Dock.Right);statusRow.Children.Add(installButton);statusRow.Children.Add(retryButton);statusRow.Children.Add(status);
        var root=new Grid{Margin=new Thickness(0,2,0,0)};root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(columns,0);Grid.SetRow(statusRow,1);root.Children.Add(columns);root.Children.Add(statusRow);Content=root;
        input.TextChanged+=(_,_)=>{if(syncingState)return;DataStore.Current.SetTranslationInput(input.Text);UpdateRoute();UpdateFontSize(columns);QueueTranslation();};
        output.TextChanged+=(_,_)=>UpdateFontSize(columns);columns.SizeChanged+=(_,_)=>UpdateFontSize(columns);
        debounce.Tick+=async(_,_)=>{debounce.Stop();await TranslateAsync(generation);};
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){DataStore.Current.SetTranslationInput("");DataStore.Current.SetTranslationOutput("");e.Handled=true;}};
        Loaded+=(_,_)=>{DataStore.Current.TranslationStateChanged+=OnTranslationStateChanged;TranslationService.Current.StatusChanged+=OnStatus;OnTranslationStateChanged();UpdateRoute();UpdateInstall();status.Text=Localization.Text(TranslationService.Current.IsReady?"":"Офлайн‑перевод · языковые модели устанавливаются один раз");if(!string.IsNullOrWhiteSpace(input.Text))QueueTranslation();else if(TranslationService.Current.IsReady)_=TranslationService.Current.WarmAsync();};
        Unloaded+=(_,_)=>{DataStore.Current.TranslationStateChanged-=OnTranslationStateChanged;TranslationService.Current.StatusChanged-=OnStatus;debounce.Stop();generation++;};
    }
    private static Border BuildColumn(TextBlock title,TextBox text,Button accessory)
    {
        var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        title.FontSize=9;title.FontWeight=FontWeights.SemiBold;title.Foreground=new SolidColorBrush(Color.FromRgb(118,120,130));
        var header=new DockPanel{LastChildFill=true};DockPanel.SetDock(accessory,Dock.Right);header.Children.Add(accessory);header.Children.Add(title);Grid.SetRow(text,1);grid.Children.Add(header);grid.Children.Add(text);
        return new Border{Child=grid,Padding=new Thickness(10)};
    }
    private static Button Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>action();return b;}
    private static Button Button(string text,Func<Task> action){var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};b.Click+=async(_,_)=>await action();return b;}
    private async Task InstallAsync()
    {
        if(installing)return;installing=true;
        try{installButton.IsEnabled=false;await TranslationService.Current.InstallAsync();}
        finally{installing=false;installButton.IsEnabled=true;UpdateInstall();}
    }
    private void UpdateInstall()=>installButton.Visibility=TranslationService.Current.IsReady?Visibility.Collapsed:Visibility.Visible;
    private void OnStatus(string value)=>Dispatcher.Invoke(()=>{status.Text=Localization.Text(value);retryButton.Visibility=value.StartsWith("Ошибка",StringComparison.Ordinal)?Visibility.Visible:Visibility.Collapsed;UpdateInstall();});
    private void UpdateRoute()
    {
        var cyrillic=input.Text.Any(c=>c is >= '\u0400' and <= '\u04FF');
        sourceTitle.Text=Localization.Text((cyrillic?"РУССКИЙ":"ENGLISH")+" · исходный текст");
        targetTitle.Text=Localization.Text(cyrillic?"ENGLISH · перевод":"РУССКИЙ · перевод");
        clearButton.Visibility=string.IsNullOrEmpty(input.Text)?Visibility.Collapsed:Visibility.Visible;
        copyButton.Visibility=string.IsNullOrEmpty(output.Text)?Visibility.Collapsed:Visibility.Visible;
    }
    private void UpdateFontSize(FrameworkElement area)
    {
        var count=Math.Max(input.Text.Trim().Length,output.Text.Length);if(count==0||area.ActualWidth<=0||area.ActualHeight<=0)return;
        var width=Math.Max(40,(area.ActualWidth-10)/2-20);var height=Math.Max(40,area.ActualHeight-40);var usable=width*height*.95;
        var font=new[]{27d,20d,15d,11d}.FirstOrDefault(size=>count<=usable/(.76*size*size));if(font==0)font=11;
        input.FontSize=font;output.FontSize=font;
    }
    private void QueueTranslation(){generation++;debounce.Stop();debounce.Start();}
    private void OnTranslationStateChanged()=>Dispatcher.Invoke(()=>
    {
        var newInput=DataStore.Current.TranslationInput;
        var inputChanged=input.Text!=newInput;
        syncingState=true;
        if(inputChanged)input.Text=newInput;
        output.Text=DataStore.Current.TranslationOutput;
        syncingState=false;
        UpdateRoute();
        if(inputChanged&&IsLoaded)QueueTranslation();
    });
    private async Task TranslateAsync(int request)
    {
        var text=input.Text.Trim();if(text.Length==0){DataStore.Current.SetTranslationOutput("");output.Clear();status.Text="";retryButton.Visibility=Visibility.Collapsed;return;}
        if(!TranslationService.Current.IsReady){DataStore.Current.SetTranslationOutput("");output.Clear();status.Text=Localization.Text("Установите языковые модели для офлайн‑перевода.");return;}
        var cyrillic=text.Any(c=>c is >= '\u0400' and <= '\u04FF');var from=cyrillic?"ru":"en";var to=cyrillic?"en":"ru";
        status.Text=$"{(Localization.UseEnglish?"Translate":"Перевод")} {from.ToUpperInvariant()} → {to.ToUpperInvariant()} · {(Localization.UseEnglish?"on device":"на устройстве")}";
        try{var result=await TranslationService.Current.TranslateAsync(text,from,to);if(request==generation){DataStore.Current.SetTranslationOutput(result);output.Text=result;status.Text="";retryButton.Visibility=Visibility.Collapsed;UpdateRoute();UpdateFontSize(columns);}}
        catch(Exception ex){if(request==generation){DataStore.Current.SetTranslationOutput("");output.Clear();status.Text=Localization.Text("Ошибка перевода: ")+ex.Message;}}
    }
}
