using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Cyclop.Windows;

internal sealed class MediaView:UserControl
{
    private readonly Image artwork=new(){Width=118,Height=118,Stretch=Stretch.Uniform};
    private readonly TextBlock artworkFallback=new(){Text="\uE8D6",Foreground=new SolidColorBrush(Color.FromRgb(114,116,125)),FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=25,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock title=new(){Foreground=Brushes.White,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock artist=new(){Foreground=new SolidColorBrush(Color.FromRgb(140,140,146)),FontSize=11.5,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock elapsed=new(){Foreground=new SolidColorBrush(Color.FromRgb(92,92,98)),FontSize=10,FontFamily=new FontFamily("Segoe UI"),VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock duration=new(){Foreground=new SolidColorBrush(Color.FromRgb(92,92,98)),FontSize=10,FontFamily=new FontFamily("Segoe UI"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
    private readonly Slider seek=new(){Minimum=0,Maximum=1,IsMoveToPointEnabled=true,Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Button previousButton;
    private readonly Button toggleButton;
    private readonly Button nextButton;
    private readonly Grid player;
    private readonly StackPanel empty;
    private bool seeking;
    private bool renderingSeek;
    private string renderedTrackKey="";
    private BitmapSource? renderedArtwork;

    public MediaView()
    {
        var details=new Grid{Height=122,VerticalAlignment=VerticalAlignment.Center};
        seek.Style=(Style)Application.Current.FindResource("MediaSeekSlider");
        details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});details.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(title,0);Grid.SetRow(artist,1);details.Children.Add(title);details.Children.Add(artist);
        previousButton=Button("\uE892",async()=>await MediaSessionService.Current.PreviousAsync(),30);
        toggleButton=Button("\uE768",async()=>await MediaSessionService.Current.ToggleAsync(),40);
        nextButton=Button("\uE893",async()=>await MediaSessionService.Current.NextAsync(),30);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        controls.Children.Add(previousButton);controls.Children.Add(toggleButton);controls.Children.Add(nextButton);
        Grid.SetRow(controls,2);details.Children.Add(controls);
        var scrubber=new Grid{Margin=new Thickness(0,3,0,0)};scrubber.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});scrubber.ColumnDefinitions.Add(new ColumnDefinition());scrubber.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        Grid.SetColumn(elapsed,0);Grid.SetColumn(seek,1);Grid.SetColumn(duration,2);scrubber.Children.Add(elapsed);scrubber.Children.Add(seek);scrubber.Children.Add(duration);Grid.SetRow(scrubber,3);details.Children.Add(scrubber);
        var coverContent=new Grid{Width=118,Height=118,Background=(Brush)Application.Current.FindResource("SurfaceBrush")};coverContent.Children.Add(artworkFallback);coverContent.Children.Add(artwork);
        var cover=new Border{Width=118,Height=118,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=(Brush)Application.Current.FindResource("HairlineBrush"),ClipToBounds=true,Margin=new Thickness(0,0,18,0),Child=coverContent,Effect=new DropShadowEffect{Color=Colors.Black,Opacity=.5,BlurRadius=12,ShadowDepth=5,Direction=270,RenderingBias=RenderingBias.Performance}};
        player=new Grid{VerticalAlignment=VerticalAlignment.Center};player.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(116)});player.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(details,1);player.Children.Add(cover);player.Children.Add(details);
        empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
        empty.Children.Add(new TextBlock{Text="\uE8D6",Foreground=new SolidColorBrush(Color.FromRgb(92,92,98)),FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=22,HorizontalAlignment=HorizontalAlignment.Center});empty.Children.Add(new TextBlock{Text="Ничего не воспроизводится",Foreground=new SolidColorBrush(Color.FromRgb(140,140,146)),FontSize=12,Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Center});
        var root=new Grid();root.Children.Add(player);root.Children.Add(empty);Content=root;
        seek.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            seeking=true;
            if(e.OriginalSource is not Thumb&&seek.ActualWidth>0)
            {
                var x=e.GetPosition(seek).X-seek.Padding.Left;
                var width=Math.Max(1,seek.ActualWidth-seek.Padding.Left-seek.Padding.Right);
                seek.Value=seek.Minimum+Math.Clamp(x/width,0,1)*(seek.Maximum-seek.Minimum);
            }
        };
        seek.PreviewMouseLeftButtonUp+=async(_,_)=>{seeking=false;await MediaSessionService.Current.SeekAsync(TimeSpan.FromSeconds(seek.Value));};
        seek.ValueChanged+=async(_,_)=>{if(!renderingSeek&&!seeking)await MediaSessionService.Current.SeekAsync(TimeSpan.FromSeconds(seek.Value));};
        Loaded+=(_,_)=>{MediaSessionService.Current.Updated+=OnUpdated;Render(MediaSessionService.Current.Snapshot);};
        Unloaded+=(_,_)=>MediaSessionService.Current.Updated-=OnUpdated;
    }
    private static Button Button(string text,Func<Task> action,double size){var primary=size>36;var b=new Button{Content=text,Style=(Style)Application.Current.FindResource(primary?"MediaPrimaryButton":"MediaTransportButton"),Margin=new Thickness(primary?0:10,0,primary?0:10,0),Width=size,Height=size};b.Click+=async(_,_)=>await action();return b;}
    private void OnUpdated(MediaSnapshot snapshot)=>Dispatcher.Invoke(()=>Render(snapshot));
    private void Render(MediaSnapshot snapshot)
    {
        var hasTrack=!string.IsNullOrWhiteSpace(snapshot.Title);
        player.Visibility=hasTrack?Visibility.Visible:Visibility.Collapsed;empty.Visibility=hasTrack?Visibility.Collapsed:Visibility.Visible;
        var trackKey=string.Join("\0",snapshot.Title,snapshot.Artist,snapshot.Album);
        if(hasTrack&&renderedTrackKey.Length>0&&trackKey!=renderedTrackKey)
        {
            FadeTrackText(title);FadeTrackText(artist);
        }
        renderedTrackKey=trackKey;
        title.Text=snapshot.Title;
        artist.Text=string.Join(" — ",new[]{snapshot.Artist,string.Equals(snapshot.Album,snapshot.Title,StringComparison.CurrentCultureIgnoreCase)?"":snapshot.Album}.Where(x=>!string.IsNullOrWhiteSpace(x)));
        player.ToolTip=snapshot.Status;empty.ToolTip=snapshot.Status;
        if(!ReferenceEquals(renderedArtwork,snapshot.Artwork))
        {
            renderedArtwork=snapshot.Artwork;
            artwork.BeginAnimation(OpacityProperty,null);
            artwork.Opacity=snapshot.Artwork is null?0:0;
            artwork.Source=snapshot.Artwork;
            if(snapshot.Artwork is not null)
            {
                var fade=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(280)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
                artwork.BeginAnimation(OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
            }
        }
        artworkFallback.Visibility=snapshot.Artwork is null?Visibility.Visible:Visibility.Collapsed;
        if(snapshot.Artwork is {PixelWidth:>0,PixelHeight:>0} cover)artwork.Stretch=Math.Abs((double)cover.PixelWidth/cover.PixelHeight-1)<.02?Stretch.UniformToFill:Stretch.Uniform;
        elapsed.Text=FormatTime(snapshot.Elapsed);duration.Text=FormatTime(snapshot.Duration);
        renderingSeek=true;
        try{seek.Maximum=Math.Max(1,snapshot.Duration.TotalSeconds);if(!seeking)seek.Value=Math.Min(seek.Maximum,snapshot.Elapsed.TotalSeconds);}
        finally{renderingSeek=false;}
        var canSkip=snapshot.CanPrevious&&snapshot.CanNext;
        previousButton.IsEnabled=canSkip;toggleButton.IsEnabled=snapshot.CanToggle;nextButton.IsEnabled=canSkip;seek.IsEnabled=snapshot.CanSeek;
        toggleButton.Content=snapshot.IsPlaying?"\uE769":"\uE768";
    }
    private static void FadeTrackText(UIElement element)
    {
        element.BeginAnimation(OpacityProperty,null);element.Opacity=0;
        element.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(280)){BeginTime=TimeSpan.FromMilliseconds(35),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd},HandoffBehavior.SnapshotAndReplace);
    }
    private static string FormatTime(TimeSpan value){var seconds=Math.Max(0,(long)value.TotalSeconds);return $"{seconds/60}:{seconds%60:D2}";}
}
