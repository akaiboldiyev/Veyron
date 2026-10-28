using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Cyclop.Windows;

internal sealed class UtilitiesView:UserControl
{
    private readonly TextBlock state=new(){Text="Клавиатура свободна",Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    public UtilitiesView()
    {
        var button=new Button{Content="Заблокировать клавиатуру и экран",HorizontalAlignment=HorizontalAlignment.Center,Padding=new Thickness(16,12,16,12)};
        button.Click+=(_,_)=>KeyboardLockService.Current.Start();
        var root=new StackPanel{VerticalAlignment=VerticalAlignment.Center};root.Children.Add(new TextBlock{Text="Утилиты",Foreground=Brushes.White,FontSize=20,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});root.Children.Add(new TextBlock{Text="Временно блокирует клавиатуру и мышь, чтобы вытереть клавиши.",Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,8,0,16)});root.Children.Add(button);root.Children.Add(state);Content=root;
        Loaded+=(_,_)=>{KeyboardLockService.Current.StatusChanged+=OnStatus;};
        Unloaded+=(_,_)=>{KeyboardLockService.Current.StatusChanged-=OnStatus;};
    }
    private void OnStatus(string message)=>Dispatcher.Invoke(()=>state.Text=message);
}

