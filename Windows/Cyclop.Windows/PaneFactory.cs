using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cyclop.Windows;

internal static class PaneFactory
{
    private static readonly Brush White = new SolidColorBrush(Color.FromRgb(244,244,245));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(154,157,167));

    public static FrameworkElement Create(string id, string panelKey, Action<string> select, Action refreshRail, Action refreshDisplays,Action refreshPrivacy)
    {
        var view=id switch
        {
            "media" => MediaPane(), "shelf" => ShelfPane(), "clipboard" => ClipboardPane(refreshPrivacy),
            "snippets" => SnippetsPane(refreshPrivacy), "calendar" => CalendarPane(refreshPrivacy,()=>select("settings")), "translate" => new TranslateView(),
            "currency" => new CurrencyView(), "notes" => NotesPane(refreshPrivacy), "teleprompter" => TeleprompterPane(panelKey),
            "utilities" => new UtilitiesView(), "settings" => SettingsPane(refreshRail,refreshDisplays,refreshPrivacy), _ => Message("Cyclop", "")
        };
        Localization.Apply(view);
        return view;
    }

    private static FrameworkElement Message(string title, string detail) => new StackPanel
    {
        VerticalAlignment = VerticalAlignment.Center,
        Children = { new TextBlock { Text=title, Foreground=White, FontSize=22, FontWeight=FontWeights.SemiBold }, new TextBlock { Text=detail, Foreground=Muted, FontSize=13, Margin=new Thickness(0,8,0,0), TextWrapping=TextWrapping.Wrap } }
    };

    private static FrameworkElement ShelfPane()
    {
        var store = DataStore.Current;
        var list = new ListBox { ItemsSource=store.Shelf, SelectionMode=SelectionMode.Extended, Background=Brushes.Transparent, Foreground=White, BorderThickness=new Thickness(0), AllowDrop=true, Height=112, HorizontalContentAlignment=HorizontalAlignment.Stretch, ItemTemplate=ShelfTemplate() };
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Hidden);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var shelfItemsPanel=new FrameworkElementFactory(typeof(StackPanel));shelfItemsPanel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);list.ItemsPanel=new ItemsPanelTemplate(shelfItemsPanel);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3)));itemStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty,92d));itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,102d));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Stretch));list.ItemContainerStyle=itemStyle;
        var allowSelectionCopy=false;
        void ArmSelectionCopy(){allowSelectionCopy=true;list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>allowSelectionCopy=false));}
        var dragStart=new System.Windows.Point();string? dragPath=null;
        list.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            ArmSelectionCopy();
            dragStart=e.GetPosition(list);
            var item=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;
            dragPath=item?.DataContext as string;
            var modifiers=System.Windows.Input.Keyboard.Modifiers;
            if(item is null)return;
            if(modifiers==System.Windows.Input.ModifierKeys.None&&item.IsSelected&&list.SelectedItems.Count==1)
            {list.SelectedItem=null;e.Handled=true;return;}
            if((modifiers&System.Windows.Input.ModifierKeys.Shift)!=0)
            {
                var path=item.DataContext as string;
                if(path is not null)
                {
                    if(item.IsSelected)list.SelectedItems.Remove(path);else list.SelectedItems.Add(path);
                    e.Handled=true;
                }
            }
        };
        list.PreviewKeyDown+=(_,_)=>ArmSelectionCopy();
        list.PreviewMouseLeftButtonUp+=(_,_)=>dragPath=null;
        list.PreviewMouseMove+=(_,e)=>{
            if(e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed||dragPath is null)return;
            var p=e.GetPosition(list);if(Math.Abs(p.X-dragStart.X)<8&&Math.Abs(p.Y-dragStart.Y)<8)return;
            var paths=list.SelectedItems.Cast<string>().ToArray();if(!paths.Contains(dragPath,StringComparer.OrdinalIgnoreCase))paths=[dragPath];if(paths.Length==0)return;
            var data=new DataObject(DataFormats.FileDrop,paths);DragDrop.DoDragDrop(list,data,DragDropEffects.Copy|DragDropEffects.Move);
            dragPath=null;
        };
        list.SelectionChanged+=(_,_)=>{if(allowSelectionCopy)store.CopyShelfSelection(list.SelectedItems.Cast<string>());};
        list.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) store.AddShelf(file); };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is string path && (File.Exists(path)||Directory.Exists(path))) Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); };
        var addFiles=PlainButton("＋ Добавить файлы",(_,_)=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,CheckFileExists=true,Title="Добавить файлы на полку"};
            if(dialog.ShowDialog()==true)foreach(var path in dialog.FileNames)store.AddShelf(path);
        });
        var addFolder=PlainButton("＋ Добавить папку",(_,_)=>
        {
            var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Добавить папку на полку",Multiselect=false};
            if(dialog.ShowDialog()==true)store.AddShelf(dialog.FolderName);
        });
        var clearShelf=PlainButton("Очистить",(_,_)=>store.ClearShelf());
        var clearSelection=PlainButton("Снять выделение",(_,_)=>list.SelectedItems.Clear());
        clearSelection.Visibility=Visibility.Collapsed;
        list.SelectionChanged+=(_,_)=>clearSelection.Visibility=list.SelectedItems.Count>0?Visibility.Visible:Visibility.Collapsed;
        var footer=new DockPanel{LastChildFill=false,Margin=new Thickness(0,2,2,0)};
        var rightActions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var selectedCount=new TextBlock{Foreground=Muted,FontSize=9,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
        list.SelectionChanged+=(_,_)=>selectedCount.Text=list.SelectedItems.Count>0?$"Выбрано: {list.SelectedItems.Count}":"";
        rightActions.Children.Add(selectedCount);rightActions.Children.Add(clearSelection);rightActions.Children.Add(addFiles);rightActions.Children.Add(addFolder);rightActions.Children.Add(clearShelf);DockPanel.SetDock(rightActions,Dock.Right);footer.Children.Add(rightActions);
        var surface=new Grid{MinHeight=112};surface.Children.Add(list);
        var dropHint=new Grid{IsHitTestVisible=false,Visibility=store.Shelf.Count==0?Visibility.Visible:Visibility.Collapsed};
        var dashed=new System.Windows.Shapes.Rectangle{Stroke=new SolidColorBrush(Color.FromArgb(42,255,255,255)),StrokeThickness=1,StrokeDashArray=new DoubleCollection{5,4},RadiusX=12,RadiusY=12,Margin=new Thickness(2)};
        var dropIcon=new TextBlock{Text="↓",Foreground=Muted,FontSize=21,FontWeight=FontWeights.Light,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        dropHint.Children.Add(dashed);dropHint.Children.Add(dropIcon);surface.Children.Add(dropHint);
        Action updateDropHint=()=>dropHint.Visibility=store.Shelf.Count==0?Visibility.Visible:Visibility.Collapsed;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler shelfChanged=(_,_)=>updateDropHint();
        surface.Loaded+=(_,_)=>{store.Shelf.CollectionChanged+=shelfChanged;updateDropHint();};
        surface.Unloaded+=(_,_)=>store.Shelf.CollectionChanged-=shelfChanged;
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(surface);Grid.SetRow(surface,0);root.Children.Add(footer);Grid.SetRow(footer,1);
        string? contextPath=null;
        var contextMenu=new ContextMenu();
        var copyItem=new MenuItem{Header="Копировать"};copyItem.Click+=(_,_)=>{if(contextPath is { } path)store.CopyShelfSelection([path]);};contextMenu.Items.Add(copyItem);
        var openItem=new MenuItem{Header="Открыть"};openItem.Click+=(_,_)=>{if(contextPath is { } path&&(File.Exists(path)||Directory.Exists(path)))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});};contextMenu.Items.Add(openItem);
        var revealItem=new MenuItem{Header="Показать в проводнике"};revealItem.Click+=(_,_)=>{if(contextPath is not { } path)return;if(Directory.Exists(path))Process.Start(new ProcessStartInfo("explorer.exe",path){UseShellExecute=true});else if(File.Exists(path))Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{path}\""){UseShellExecute=true});};contextMenu.Items.Add(revealItem);
        contextMenu.Items.Add(new Separator());
        var removeItem=new MenuItem{Header="Убрать с полки"};removeItem.Click+=(_,_)=>{if(contextPath is { } path)store.RemoveShelf(path);};contextMenu.Items.Add(removeItem);
        list.ContextMenu=contextMenu;
        list.PreviewMouseRightButtonDown+=(_,e)=>{var item=ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) as ListBoxItem;contextPath=item?.DataContext as string;};
        list.ContextMenuOpening+=(_,e)=>{if(contextPath is null)e.Handled=true;};
        return root;
    }

    private static DataTemplate ShelfTemplate()
    {
        var card=new FrameworkElementFactory(typeof(Border));card.SetValue(Border.WidthProperty,86d);card.SetValue(Border.HeightProperty,92d);card.SetValue(Border.PaddingProperty,new Thickness(6,7,6,5));card.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));
        var cardState=new System.Windows.Data.MultiBinding{Converter=ShelfCardStateConverter.Instance};
        cardState.Bindings.Add(new System.Windows.Data.Binding("IsSelected"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)});
        cardState.Bindings.Add(new System.Windows.Data.Binding("IsMouseOver"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)});
        card.SetBinding(Border.BackgroundProperty,cardState);
        var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(StackPanel.VerticalAlignmentProperty,VerticalAlignment.Center);
        var previewBox=new FrameworkElementFactory(typeof(Grid));previewBox.SetValue(FrameworkElement.WidthProperty,68d);previewBox.SetValue(FrameworkElement.HeightProperty,40d);previewBox.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        var icon=new FrameworkElementFactory(typeof(TextBlock));icon.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."){Converter=ShelfIconConverter.Instance});icon.SetValue(TextBlock.ForegroundProperty,White);icon.SetValue(TextBlock.FontSizeProperty,16d);icon.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);icon.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        var image=new FrameworkElementFactory(typeof(ShelfPreviewImage));image.SetBinding(ShelfPreviewImage.FilePathProperty,new System.Windows.Data.Binding("."));image.SetValue(Image.WidthProperty,68d);image.SetValue(Image.HeightProperty,40d);image.SetValue(Image.StretchProperty,Stretch.Uniform);image.SetValue(UIElement.IsHitTestVisibleProperty,false);
        previewBox.AppendChild(icon);previewBox.AppendChild(image);stack.AppendChild(previewBox);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."){Converter=ShelfNameConverter.Instance});name.SetValue(TextBlock.ForegroundProperty,Muted);name.SetValue(TextBlock.FontSizeProperty,9d);name.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);name.SetValue(FrameworkElement.HeightProperty,28d);name.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));stack.AppendChild(name);card.AppendChild(stack);
        return new DataTemplate(typeof(string)){VisualTree=card};
    }

    private static FrameworkElement ClipboardPane(Action refreshPrivacy)
    {
        var store=DataStore.Current;
        var privacyEnabled=store.PrivacySections.Contains("clipboard");
        var list=new ListBox{ItemsSource=store.ClipboardHistory,ItemTemplate=ClipboardTemplate(privacyEnabled),Background=Brushes.Transparent,Foreground=White,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var itemStyle=new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,1,0,1)));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem));
        var surface=new FrameworkElementFactory(typeof(Border));surface.Name="ClipRow";surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentSourceProperty,"Content");content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);surface.AppendChild(content);itemTemplate.VisualTree=surface;
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(30,255,255,255)),"ClipRow"));itemTemplate.Triggers.Add(hover);itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));list.ItemContainerStyle=itemStyle;
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(e.OriginalSource is not DependencyObject source||ItemsControl.ContainerFromElement(list,source) is not ListBoxItem item||item.DataContext is not ClipboardEntry entry)return;
            for(var parent=source;parent is not null&&parent!=item;parent=VisualTreeHelper.GetParent(parent))
            {
                if(parent is not Button action)continue;
                e.Handled=true;
                if(Equals(action.Tag,"remove"))store.RemoveClipboardEntry(entry);
                else if(Equals(action.Tag,"reveal")&&store.PrivacySections.Contains("clipboard")){store.TogglePrivateReveal("clipboard",entry.Id.ToString());list.Items.Refresh();refreshPrivacy();}
                return;
            }
            store.RestoreClipboard(entry);
        };
        var empty=new TextBlock{Text="\uE8C8",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=20,Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        Action updateEmpty=()=>empty.Visibility=store.ClipboardHistory.Count==0?Visibility.Visible:Visibility.Collapsed;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,_)=>updateEmpty();
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.Children.Add(list);Grid.SetRow(list,0);root.Children.Add(empty);Grid.SetRow(empty,0);
        var footer=new DockPanel{LastChildFill=false,Margin=new Thickness(0,3,2,0)};
        Button? privacy=null;
        privacy=IconButton(store.PrivacySections.Contains("clipboard")?"\uE72E":"\uE890",store.PrivacySections.Contains("clipboard")?"Показать содержимое":"Скрыть содержимое",()=>
        {
            var enabled=store.PrivacySections.Contains("clipboard");store.SetPrivacySection("clipboard",!enabled);privacy!.Content=enabled?"\uE890":"\uE72E";privacy.ToolTip=Localization.Text(enabled?"Скрыть содержимое":"Показать содержимое");refreshPrivacy();list.Items.Refresh();
        });
        privacy.IsEnabled=true;DockPanel.SetDock(privacy,Dock.Left);footer.Children.Add(privacy);
        var clear=new Button{Content="Очистить",Padding=new Thickness(3,1,3,1),Margin=new Thickness(0),Background=Brushes.Transparent,Foreground=Muted,FontSize=10,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};clear.Click+=(_,_)=>store.ClearClipboardHistory();DockPanel.SetDock(clear,Dock.Right);footer.Children.Add(clear);
        root.Children.Add(footer);Grid.SetRow(footer,1);
        root.Loaded+=(_,_)=>{store.ClipboardHistory.CollectionChanged+=changed;updateEmpty();};root.Unloaded+=(_,_)=>store.ClipboardHistory.CollectionChanged-=changed;
        return root;
    }

    private static DataTemplate ClipboardTemplate(bool privacyEnabled)
    {
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));border.SetValue(Border.PaddingProperty,new Thickness(9,3,7,3));border.SetValue(Border.HeightProperty,26d);border.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(18,255,255,255)));
        var columns=new FrameworkElementFactory(typeof(DockPanel));columns.SetValue(DockPanel.LastChildFillProperty,true);
        var actions=new FrameworkElementFactory(typeof(StackPanel));actions.Name="ClipActions";actions.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);actions.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);actions.SetValue(DockPanel.DockProperty,Dock.Right);
        var actionsStyle=new Style(typeof(StackPanel));actionsStyle.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));var isHovering=new DataTrigger{Binding=new System.Windows.Data.Binding("IsMouseOver"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true};isHovering.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible));actionsStyle.Triggers.Add(isHovering);actions.SetValue(FrameworkElement.StyleProperty,actionsStyle);
        var reveal=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));reveal.SetValue(ContentControl.ContentProperty,"\uE890");reveal.SetValue(FrameworkElement.TagProperty,"reveal");reveal.SetValue(FrameworkElement.ToolTipProperty,"Показать / скрыть");reveal.SetValue(FrameworkElement.WidthProperty,20d);reveal.SetValue(FrameworkElement.HeightProperty,20d);reveal.SetValue(Control.PaddingProperty,new Thickness(0));reveal.SetValue(Control.MarginProperty,new Thickness(0,0,3,0));reveal.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));reveal.SetValue(Control.FontSizeProperty,10d);reveal.SetValue(Control.BackgroundProperty,Brushes.Transparent);reveal.SetValue(Control.BorderThicknessProperty,new Thickness(0));reveal.SetValue(Control.StyleProperty,Application.Current.FindResource("MediaTransportButton"));reveal.SetValue(UIElement.VisibilityProperty,privacyEnabled?Visibility.Visible:Visibility.Collapsed);
        reveal.AddHandler(System.Windows.Controls.Button.ClickEvent,new RoutedEventHandler((_,e)=>e.Handled=true));actions.AppendChild(reveal);
        var remove=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));remove.SetValue(ContentControl.ContentProperty,"\uE711");remove.SetValue(FrameworkElement.TagProperty,"remove");remove.SetValue(FrameworkElement.ToolTipProperty,"Удалить");remove.SetValue(FrameworkElement.WidthProperty,20d);remove.SetValue(FrameworkElement.HeightProperty,20d);remove.SetValue(Control.PaddingProperty,new Thickness(0));remove.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));remove.SetValue(Control.FontSizeProperty,9d);remove.SetValue(Control.BackgroundProperty,Brushes.Transparent);remove.SetValue(Control.BorderThicknessProperty,new Thickness(0));remove.SetValue(Control.StyleProperty,Application.Current.FindResource("MediaTransportButton"));remove.AddHandler(System.Windows.Controls.Button.ClickEvent,new RoutedEventHandler((_,e)=>e.Handled=true));actions.AppendChild(remove);columns.AppendChild(actions);
        var icon=new FrameworkElementFactory(typeof(TextBlock));icon.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."){Converter=ClipboardGlyphConverter.Instance});icon.SetValue(TextBlock.ForegroundProperty,Muted);icon.SetValue(TextBlock.FontSizeProperty,10d);icon.SetValue(FrameworkElement.WidthProperty,16d);icon.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);icon.SetValue(DockPanel.DockProperty,Dock.Left);columns.AppendChild(icon);
        var preview=new FrameworkElementFactory(typeof(TextBlock));preview.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(ClipboardEntry.Preview)));preview.SetValue(TextBlock.ForegroundProperty,White);preview.SetValue(TextBlock.FontSizeProperty,10.5d);preview.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);preview.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);preview.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);columns.AppendChild(preview);
        border.AppendChild(columns);return new DataTemplate(typeof(ClipboardEntry)){VisualTree=border};
    }

    private static FrameworkElement MediaPane()
    {
        return new MediaView();
    }

    private static FrameworkElement CalendarPane(Action refreshPrivacy,Action openSettings)
    {
        var store=DataStore.Current;
        var agenda=new StackPanel();
        var calendarChoices=new StackPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,4)};
        var nextTitle=new TextBlock{Foreground=White,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
        var nextTime=new TextBlock{Foreground=Muted,FontSize=11.5,Margin=new Thickness(14,4,0,8)};
        var details=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
        var lead=new StackPanel{MinWidth=180,MaxWidth=240};
        lead.Children.Add(nextTitle);lead.Children.Add(nextTime);
        var joinNext=Button("◉  Подключиться",(_,_)=>
        {
            if(store.CalendarEvents.FirstOrDefault(x=>(x.EndsAt??x.StartsAt)>DateTime.Now) is { } item&&ICalendarParser.IsJoinableMeetingLink(item.MeetingUrl))
                Process.Start(new ProcessStartInfo(item.MeetingUrl!){UseShellExecute=true});
        });
        joinNext.Visibility=Visibility.Collapsed;lead.Children.Add(joinNext);details.Children.Add(lead);
        var following=new StackPanel{Margin=new Thickness(14,0,0,0),Width=220};details.Children.Add(following);
        var heading=new TextBlock{Text="Ближайшая встреча",Foreground=Muted,FontSize=10,Margin=new Thickness(0,0,0,4)};
        var showingCalendars=false;
        Button toggleCalendars=null!;
        toggleCalendars=IconButton("\uE713","Календари",()=>
        {
            showingCalendars=!showingCalendars;calendarChoices.Visibility=showingCalendars?Visibility.Visible:Visibility.Collapsed;details.Visibility=showingCalendars?Visibility.Collapsed:Visibility.Visible;
            heading.Text=showingCalendars?"Календари":"Ближайшая встреча";toggleCalendars.Content=showingCalendars?"Готово":"\uE713";toggleCalendars.FontFamily=showingCalendars?SystemFonts.MessageFontFamily:new FontFamily("Segoe MDL2 Assets");toggleCalendars.Width=showingCalendars?62:24;toggleCalendars.Padding=showingCalendars?new Thickness(8,2,8,2):new Thickness(0);toggleCalendars.ToolTip=Localization.Text(showingCalendars?"Готово":"Календари");
        });
        toggleCalendars.Width=24;toggleCalendars.Height=22;toggleCalendars.Margin=new Thickness(2,0,0,0);toggleCalendars.FontSize=11;
        var topActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var revealPrivacy=IconButton("\uE72E","Показать / скрыть встречи",()=>{store.TogglePrivateReveal("calendar","calendar");refreshPrivacy();});revealPrivacy.Visibility=store.PrivacySections.Contains("calendar")?Visibility.Visible:Visibility.Collapsed;DockPanel.SetDock(revealPrivacy,Dock.Right);topActions.Children.Add(revealPrivacy);topActions.Children.Add(toggleCalendars);
        var top=new DockPanel();DockPanel.SetDock(topActions,Dock.Right);top.Children.Add(topActions);top.Children.Add(heading);
        agenda.Children.Add(top);agenda.Children.Add(details);
        void UpdateNext()
        {
            var now=DateTime.Now;
            var meetings=store.CalendarEvents.Where(x=>(x.EndsAt??x.StartsAt)>now).OrderBy(x=>x.StartsAt).ToArray();
            var upcoming=meetings.FirstOrDefault();
            nextTitle.Text=upcoming?.DisplayTitle??Localization.Text("Нет предстоящих встреч");
            joinNext.Visibility=upcoming is not null&&ICalendarParser.IsJoinableMeetingLink(upcoming.MeetingUrl)?Visibility.Visible:Visibility.Collapsed;
            following.Children.Clear();
            foreach(var later in meetings.Skip(1).Take(4))
            {
                var row=new DockPanel{LastChildFill=true,Margin=new Thickness(0,2,0,2)};
                var time=new TextBlock{Text=later.StartsAt.ToString("HH:mm"),Foreground=Muted,FontSize=10,Width=38,VerticalAlignment=VerticalAlignment.Center};
                row.Children.Add(time);
                var title=new TextBlock{Text=later.DisplayTitle,Foreground=Muted,FontSize=10.5,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
                row.Children.Add(title);
                if(upcoming is not null&&later.StartsAt<(upcoming.EndsAt??upcoming.StartsAt)&&ICalendarParser.IsJoinableMeetingLink(later.MeetingUrl))
                {
                    var join=Button("◉",(_,_)=>Process.Start(new ProcessStartInfo(later.MeetingUrl!){UseShellExecute=true}));
                    join.ToolTip="Подключиться";join.Padding=new Thickness(4,0,4,0);join.Margin=new Thickness(3,0,0,0);DockPanel.SetDock(join,Dock.Right);row.Children.Insert(0,join);
                }
                following.Children.Add(row);
            }
            if(meetings.Length<=1)following.Children.Add(new TextBlock{Text=Localization.Text("Других встреч на этой неделе нет"),Foreground=Muted,FontSize=10,TextWrapping=TextWrapping.Wrap});
            if(upcoming is null){nextTime.Text="";return;}
            var meetingDay=upcoming.StartsAt.Date==now.Date?"":upcoming.StartsAt.Date==now.Date.AddDays(1)?"Завтра":upcoming.StartsAt.ToString("dddd, d MMMM",System.Globalization.CultureInfo.CurrentUICulture);
            var timeRange=upcoming.StartsAt.ToString("HH:mm–")+(upcoming.EndsAt??upcoming.StartsAt).ToString("HH:mm");
            nextTime.Text=(meetingDay.Length==0?"":Localization.Text(meetingDay)+" · ")+timeRange;
        }
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,_)=>UpdateNext();
        System.Collections.Specialized.NotifyCollectionChangedEventHandler calendarsChanged=(_,_)=>RefreshCalendarChoices();
        Action clockTick=UpdateNext;
        var subscribed=false;
        agenda.Loaded+=(_,_)=>{if(!subscribed){store.CalendarEvents.CollectionChanged+=changed;store.CalendarClockTick+=clockTick;store.WindowsCalendars.CollectionChanged+=calendarsChanged;subscribed=true;}UpdateNext();RefreshCalendarChoices();};
        agenda.Unloaded+=(_,_)=>{if(subscribed){store.CalendarEvents.CollectionChanged-=changed;store.CalendarClockTick-=clockTick;store.WindowsCalendars.CollectionChanged-=calendarsChanged;subscribed=false;}};
        void RefreshCalendarChoices()
        {
            calendarChoices.Children.Clear();
            foreach(var option in store.WindowsCalendars)
            {
                var check=new CheckBox{Content=option.Name,IsChecked=option.IsShown,Foreground=White,Margin=new Thickness(0,2,0,2)};
                check.Checked+=async(_,_)=>{if(!option.IsShown)await store.ToggleWindowsCalendarAsync(option);};
                check.Unchecked+=async(_,_)=>{if(option.IsShown)await store.ToggleWindowsCalendarAsync(option);};
                calendarChoices.Children.Add(check);
            }
            if(store.WindowsCalendars.Count==0)
            {
                calendarChoices.Children.Add(new TextBlock{Text="Сначала подключите календари в настройках Cyclop.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,6)});
                var configure=Button("Открыть настройки",(_,_)=>openSettings());configure.Padding=new Thickness(9,4,9,4);configure.FontSize=11;calendarChoices.Children.Add(configure);
            }
        }
        RefreshCalendarChoices();
        var root=new StackPanel();root.Children.Add(agenda);root.Children.Add(calendarChoices);
        return new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }

    private static FrameworkElement CalendarSourcesSettings()
    {
        var store=DataStore.Current;
        var root=new StackPanel{Margin=new Thickness(0,4,0,8)};
        var status=new TextBlock{Foreground=Muted,FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,4)};
        var choices=new StackPanel{Margin=new Thickness(12,2,0,4)};
        var sources=new ListBox{ItemsSource=store.CalendarSources,Background=Brushes.Transparent,Foreground=White,BorderThickness=new Thickness(0),MaxHeight=58};
        void RefreshChoices()
        {
            choices.Children.Clear();
            foreach(var option in store.WindowsCalendars)
            {
                var check=new CheckBox{Content=option.Name,IsChecked=option.IsShown,Foreground=White,Margin=new Thickness(0,2,0,2)};
                check.Checked+=async(_,_)=>{if(!option.IsShown)await store.ToggleWindowsCalendarAsync(option);};
                check.Unchecked+=async(_,_)=>{if(option.IsShown)await store.ToggleWindowsCalendarAsync(option);};
                choices.Children.Add(check);
            }
            if(store.WindowsCalendars.Count==0)choices.Children.Add(new TextBlock{Text="Календари Windows пока не подключены.",Foreground=Muted,FontSize=10,TextWrapping=TextWrapping.Wrap});
        }
        var connect=Button("Подключить календари Windows",async(_,_)=>
        {
            status.Text=Localization.Text("Запрашиваю доступ к календарям Windows…");
            var connected=await store.ConnectWindowsCalendarAsync();status.Text=Localization.Text(connected?"Календарь Windows подключён.":"Не удалось подключить календарь Windows.");RefreshChoices();
        });
        var url=new TextBox{ToolTip="URL подписки календаря iCalendar",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,3,0,3)};
        var addUrl=Button("Добавить URL‑календарь",(_,_)=>
        {
            var value=url.Text.Trim();
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")){status.Text=Localization.Text("Введите действительный HTTPS‑адрес календаря.");return;}
            store.AddCalendarUrl(value);url.Clear();status.Text=Localization.Text("Календарь добавлен.");
        });
        var import=Button("Добавить файл .ics",(_,_)=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="iCalendar (*.ics)|*.ics",Title="Выберите файл календаря"};
            if(dialog.ShowDialog()==true){store.ImportCalendar(dialog.FileName);status.Text=Localization.Text("Файл календаря добавлен.");}
        });
        var removeSource=Button("Удалить выбранный источник",(_,_)=>{if(sources.SelectedItem is string item)store.RemoveCalendarSource(item);});
        root.Children.Add(new TextBlock{Text="Источники календаря",Foreground=Muted,FontSize=11,Margin=new Thickness(0,5,0,2)});
        root.Children.Add(connect);root.Children.Add(status);root.Children.Add(url);root.Children.Add(addUrl);root.Children.Add(import);root.Children.Add(sources);root.Children.Add(removeSource);
        root.Children.Add(new TextBlock{Text="Календари Windows",Foreground=Muted,FontSize=11,Margin=new Thickness(0,7,0,2)});root.Children.Add(choices);
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,_)=>RefreshChoices();
        root.Loaded+=(_,_)=>{store.WindowsCalendars.CollectionChanged+=changed;RefreshChoices();};root.Unloaded+=(_,_)=>store.WindowsCalendars.CollectionChanged-=changed;
        return root;
    }

    private static FrameworkElementFactory CalendarTemplate()
    {
        var panel=new FrameworkElementFactory(typeof(StackPanel));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("DisplayTitle"));title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);panel.AppendChild(title);
        var time=new FrameworkElementFactory(typeof(TextBlock));time.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("DisplayTime"));time.SetValue(TextBlock.ForegroundProperty,Muted);panel.AppendChild(time);
        var source=new FrameworkElementFactory(typeof(TextBlock));source.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("DisplayCalendarName"));source.SetValue(TextBlock.ForegroundProperty,Muted);source.SetValue(TextBlock.FontSizeProperty,10d);panel.AppendChild(source);
        return panel;
    }

    private static FrameworkElement SnippetsPane(Action refreshPrivacy)
    {
        var store=DataStore.Current;
        SnippetItem? editing=null;
        Border? searchBar=null;Border? editorBar=null;TextBlock? empty=null;
        var search=new TextBox{ToolTip="Поиск по названию и содержимому",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),FontSize=11};
        var label=new TextBox{ToolTip="Название (необязательно)",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(6,0,6,0),FontSize=11};
        var value=new TextBox{ToolTip="Текст заготовки",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(6,0,6,0),FontSize=11};
        var list=new ListBox{ItemsSource=store.Snippets,Background=Brushes.Transparent,Foreground=White,BorderThickness=new Thickness(0),ItemTemplate=SnippetTemplate()};
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,1,0,2)));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem));var itemSurface=new FrameworkElementFactory(typeof(Border));itemSurface.Name="ItemSurface";itemSurface.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));itemSurface.SetValue(Border.BackgroundProperty,Brushes.Transparent);var itemContent=new FrameworkElementFactory(typeof(ContentPresenter));itemContent.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch);itemSurface.AppendChild(itemContent);itemTemplate.VisualTree=itemSurface;
        var selectedStyle=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selectedStyle.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(46,255,255,255)),"ItemSurface"));itemTemplate.Triggers.Add(selectedStyle);
        var hoverStyle=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hoverStyle.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(20,255,255,255)),"ItemSurface"));itemTemplate.Triggers.Add(hoverStyle);itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));list.ItemContainerStyle=itemStyle;
        var collection=System.Windows.Data.CollectionViewSource.GetDefaultView(store.Snippets);
        collection.Filter=item=>item is SnippetItem snippet&&(string.IsNullOrWhiteSpace(search.Text)||MatchesSnippetSearch(snippet.Label??"",search.Text)||MatchesSnippetSearch(snippet.Text,search.Text));
        search.TextChanged+=(_,_)=>collection.Refresh();search.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){search.Clear();e.Handled=true;}};
        var searchIcon=new TextBlock{Text="\uE721",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Foreground=Muted,Width=14,VerticalAlignment=VerticalAlignment.Center};
        var clear=IconButton("\uE711","Очистить поиск",()=>search.Clear());clear.Visibility=Visibility.Collapsed;
        search.TextChanged+=(_,_)=>clear.Visibility=string.IsNullOrEmpty(search.Text)?Visibility.Collapsed:Visibility.Visible;
        var privacyButton=IconButton(store.PrivacySections.Contains("snippets")?"\uE72E":"\uE890","Скрыть или показать заготовки",()=>{store.SetPrivacySection("snippets",!store.PrivacySections.Contains("snippets"));refreshPrivacy();});
        var addButton=IconButton("\uE710","Добавить заготовку",BeginAdding);
        var searchContent=new DockPanel{LastChildFill=true,Margin=new Thickness(9,0,5,0)};
        var searchActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};searchActions.Children.Add(clear);searchActions.Children.Add(privacyButton);searchActions.Children.Add(addButton);DockPanel.SetDock(searchActions,Dock.Right);searchContent.Children.Add(searchActions);searchContent.Children.Add(searchIcon);searchContent.Children.Add(search);
        searchBar=new Border{Height=26,CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),Child=searchContent};
        var saveButton=IconButton("\uE73E","Сохранить заготовку",Commit);saveButton.Foreground=Brushes.LightGreen;
        var cancelButton=IconButton("\uE711","Отменить",Cancel);
        var editorGrid=new Grid{Margin=new Thickness(6,0,4,0)};editorGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(104)});editorGrid.ColumnDefinitions.Add(new ColumnDefinition());editorGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});editorGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});
        label.Margin=new Thickness(0,0,6,0);Grid.SetColumn(label,0);Grid.SetColumn(value,1);Grid.SetColumn(saveButton,2);Grid.SetColumn(cancelButton,3);editorGrid.Children.Add(label);editorGrid.Children.Add(value);editorGrid.Children.Add(saveButton);editorGrid.Children.Add(cancelButton);
        editorBar=new Border{Height=28,CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(26,255,255,255)),BorderThickness=new Thickness(1),Child=editorGrid,Visibility=Visibility.Collapsed};
        var header=new Grid();header.Children.Add(searchBar);header.Children.Add(editorBar);
        empty=new TextBlock{Text=store.SnippetsFileBroken?"snippets.json повреждён — файл не перезаписывается.":store.Snippets.Count==0?"Здесь пока пусто — добавьте заготовку через +":"Совпадений нет",Foreground=Muted,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,Visibility=Visibility.Collapsed};
        void UpdateEmpty()=>empty!.Visibility=collection.IsEmpty?Visibility.Visible:Visibility.Collapsed;
        search.TextChanged+=(_,_)=>UpdateEmpty();store.Snippets.CollectionChanged+=(_,_)=>UpdateEmpty();
        var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(28)});body.RowDefinitions.Add(new RowDefinition());Grid.SetRow(header,0);Grid.SetRow(list,1);Grid.SetRow(empty,1);body.Children.Add(header);body.Children.Add(list);body.Children.Add(empty);
        void BeginAdding(){if(store.SnippetsFileBroken)return;search.Clear();editing=null;label.Clear();value.Clear();searchBar!.Visibility=Visibility.Collapsed;editorBar!.Visibility=Visibility.Visible;value.Focus();Keyboard.Focus(value);}
        void BeginEdit(SnippetItem item){if(store.SnippetsFileBroken)return;search.Clear();editing=item;label.Text=item.Label??"";value.Text=item.Text;searchBar!.Visibility=Visibility.Collapsed;editorBar!.Visibility=Visibility.Visible;value.Focus();Keyboard.Focus(value);}
        void Commit(){if(store.SnippetsFileBroken||string.IsNullOrWhiteSpace(value.Text))return;if(editing is null)store.AddSnippet(label.Text,value.Text);else store.UpdateSnippet(editing,label.Text,value.Text);editing=null;label.Clear();value.Clear();collection.Refresh();UpdateEmpty();value.Focus();Keyboard.Focus(value);}
        void Cancel(){editing=null;label.Clear();value.Clear();editorBar!.Visibility=Visibility.Collapsed;searchBar!.Visibility=Visibility.Visible;search.Focus();Keyboard.Focus(search);}
        saveButton.Click+=(_,_)=>Commit();cancelButton.Click+=(_,_)=>Cancel();
        value.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}else if(e.Key==Key.Escape){Cancel();e.Handled=true;}};
        label.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Cancel();e.Handled=true;}else if(e.Key==Key.Enter){value.Focus();e.Handled=true;}};
        list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is SnippetItem item)BeginEdit(item);};
        var context=new ContextMenu();var editMenu=new MenuItem{Header="Изменить"};editMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item)BeginEdit(item);};context.Items.Add(editMenu);
        var copyMenu=new MenuItem{Header="Копировать"};copyMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item)Clipboard.SetText(item.Text);};context.Items.Add(copyMenu);
        var upMenu=new MenuItem{Header="Переместить выше"};upMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item)store.MoveSnippet(item,-1);};context.Items.Add(upMenu);
        var downMenu=new MenuItem{Header="Переместить ниже"};downMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item)store.MoveSnippet(item,1);};context.Items.Add(downMenu);
        var revealMenu=new MenuItem{Header="Показать / скрыть"};revealMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item){store.TogglePrivateReveal("snippets","snippet."+item.Id);list.Items.Refresh();}};context.Items.Add(revealMenu);
        var deleteMenu=new MenuItem{Header="Удалить"};deleteMenu.Click+=(_,_)=>{if(list.SelectedItem is SnippetItem item)store.RemoveSnippet(item);};context.Items.Add(deleteMenu);
        list.ContextMenu=context;list.ContextMenuOpening+=(_,e)=>{if(list.SelectedItem is not SnippetItem){e.Handled=true;return;}editMenu.IsEnabled=!store.SnippetsFileBroken;deleteMenu.IsEnabled=!store.SnippetsFileBroken;upMenu.IsEnabled=!store.SnippetsFileBroken&&store.Snippets.IndexOf((SnippetItem)list.SelectedItem)>0;downMenu.IsEnabled=!store.SnippetsFileBroken&&store.Snippets.IndexOf((SnippetItem)list.SelectedItem)<store.Snippets.Count-1;};
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(list,source) is ListBoxItem&&list.SelectedItem is SnippetItem item)
                Clipboard.SetText(item.Text);
        };
        UpdateEmpty();return body;
    }

    private static Button IconButton(string glyph,string tip,Action action)
    {
        var button=new Button{Content=glyph,ToolTip=Localization.Text(tip),Width=24,Height=24,Padding=new Thickness(0),FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
        button.Click+=(_,_)=>action();return button;
    }

    private static Button PlainButton(string title,RoutedEventHandler action)
    {
        var button=new Button{Content=Localization.Text(title),ToolTip=Localization.Text(title),Padding=new Thickness(4,1,4,1),Margin=new Thickness(2,0,0,0),FontSize=10,Foreground=Muted,Background=Brushes.Transparent,BorderThickness=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
        button.Click+=action;return button;
    }

    private static DataTemplate SnippetTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(DockPanel.LastChildFillProperty,true);row.SetValue(FrameworkElement.HeightProperty,26d);row.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,5,0));
        var pin=new FrameworkElementFactory(typeof(TextBlock));pin.SetValue(TextBlock.TextProperty,"\uE840");pin.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));pin.SetValue(TextBlock.FontSizeProperty,10d);pin.SetValue(TextBlock.ForegroundProperty,Muted);pin.SetValue(FrameworkElement.WidthProperty,14d);pin.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);pin.SetValue(DockPanel.DockProperty,Dock.Left);row.AppendChild(pin);
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("."));text.SetValue(TextBlock.ForegroundProperty,White);text.SetValue(TextBlock.FontSizeProperty,11d);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(text);
        return new DataTemplate(typeof(SnippetItem)){VisualTree=row};
    }

    private static FrameworkElement NotesPane(Action refreshPrivacy)
    {
        var store=DataStore.Current;
        if(store.Notes.Count==0)
        {
            store.AddNote();
            if(store.PrivacySections.Contains("notes"))store.TogglePrivateReveal("notes",store.Notes[0].Id.ToString());
        }
        var list=new ListBox { ItemsSource=store.Notes, Background=Brushes.Transparent, Foreground=White, BorderThickness=new Thickness(0), Width=170, Padding=new Thickness(0) };ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Hidden);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,0,5,0)));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,White));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,26d));
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem));var rowBorder=new FrameworkElementFactory(typeof(Border));rowBorder.Name="RowBackground";rowBorder.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));rowBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.ContentSourceProperty,"Content");presenter.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);rowBorder.AppendChild(presenter);itemTemplate.VisualTree=rowBorder;
        var selectedTrigger=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(36,255,255,255)),"RowBackground"));itemTemplate.Triggers.Add(selectedTrigger);
        var hoverTrigger=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(22,255,255,255)),"RowBackground"));itemTemplate.Triggers.Add(hoverTrigger);itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));list.ItemContainerStyle=itemStyle;
        var editor=new TextBox { AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, MinHeight=130,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),FontSize=12.5 };
        var placeholder=new TextBlock{Text="Начните писать…",Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),FontSize=12.5,Margin=new Thickness(2,0,0,0),IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Top};
        Button? copyButton=null;
        var syncing=false;
        var syncingSelection=false;
        NoteItem? boundNote=null;
        PropertyChangedEventHandler noteChanged=(_,args)=>
        {
            if(args.PropertyName!=nameof(NoteItem.Text)||syncing||editor.IsReadOnly||boundNote is null||editor.Text==boundNote.Text)return;
            syncing=true;editor.Text=boundNote.Text;syncing=false;
        };
        void UpdateEditor()
        {
            if(boundNote is not null)boundNote.PropertyChanged-=noteChanged;
            boundNote=null;
            if(list.SelectedItem is not NoteItem note)return;
            boundNote=note;boundNote.PropertyChanged+=noteChanged;
            var hidden=store.IsPrivateHidden("notes",note.Id.ToString());
            syncing=true;editor.IsReadOnly=hidden;editor.Text=hidden?"••••••••••":note.Text;syncing=false;
            placeholder.Visibility=!hidden&&note.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            if(copyButton is not null)copyButton.Visibility=string.IsNullOrWhiteSpace(note.Text)?Visibility.Collapsed:Visibility.Visible;
            list.Items.Refresh();
        }
        list.SelectionChanged += (_,_) => {if(!syncingSelection&&list.SelectedItem is NoteItem note)store.SelectedNoteId=note.Id;UpdateEditor();};
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            var source=e.OriginalSource as DependencyObject;
            for(var current=source;current is not null&&current!=list;current=VisualTreeHelper.GetParent(current))if(current is Button)return;
            if(ItemsControl.ContainerFromElement(list,source) is not ListBoxItem||list.SelectedItem is not NoteItem)return;
            list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>{if(list.IsLoaded&&!editor.IsReadOnly){editor.Focus();Keyboard.Focus(editor);}}));
        };
        void SyncSelection()
        {
            if(!list.IsLoaded)return;
            var note=store.Notes.FirstOrDefault(x=>x.Id==store.SelectedNoteId)??store.Notes.FirstOrDefault();
            if(note is not null&&store.SelectedNoteId!=note.Id)store.SelectedNoteId=note.Id;
            if(ReferenceEquals(list.SelectedItem,note))return;
            syncingSelection=true;list.SelectedItem=note;syncingSelection=false;UpdateEditor();
        }
        Action selectedChanged=SyncSelection;
        list.Loaded+=(_,_)=>{store.SelectedNoteChanged+=selectedChanged;SyncSelection();};
        list.Unloaded+=(_,_)=>{store.SelectedNoteChanged-=selectedChanged;if(boundNote is not null){boundNote.PropertyChanged-=noteChanged;boundNote=null;}};
        editor.TextChanged += (_,_) => { if(!syncing&&!editor.IsReadOnly&&list.SelectedItem is NoteItem n){store.UpdateNote(n,editor.Text);placeholder.Visibility=editor.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(copyButton is not null)copyButton.Visibility=string.IsNullOrWhiteSpace(editor.Text)?Visibility.Collapsed:Visibility.Visible;} };
        void AddNote()
        {
            store.AddNote();var note=store.Notes[0];
            if(store.PrivacySections.Contains("notes"))store.TogglePrivateReveal("notes",note.Id.ToString());
            list.SelectedItem=note;UpdateEditor();editor.CaretIndex=0;editor.Focus();Keyboard.Focus(editor);
        }
        var actionStyle=new Style(typeof(System.Windows.Controls.Button),Application.Current.Resources[typeof(System.Windows.Controls.Button)] as Style);actionStyle.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));actionStyle.Setters.Add(new Setter(Control.FocusableProperty,false));actionStyle.Setters.Add(new Setter(Control.IsTabStopProperty,false));actionStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty,null));var hoveredAction=new DataTrigger{Binding=new System.Windows.Data.Binding(nameof(ListBoxItem.IsMouseOver)){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true};hoveredAction.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible));actionStyle.Triggers.Add(hoveredAction);
        var rowTemplate=new DataTemplate(typeof(NoteItem));var rowRoot=new FrameworkElementFactory(typeof(DockPanel));rowRoot.SetValue(DockPanel.LastChildFillProperty,true);
        var actions=new FrameworkElementFactory(typeof(StackPanel));actions.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);actions.SetValue(DockPanel.DockProperty,Dock.Right);
        var revealItem=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));revealItem.SetValue(FrameworkElement.StyleProperty,actionStyle);revealItem.SetValue(ContentControl.ContentProperty,"");revealItem.SetValue(FrameworkElement.WidthProperty,20d);revealItem.SetValue(FrameworkElement.HeightProperty,22d);revealItem.SetValue(Control.PaddingProperty,new Thickness(0));revealItem.SetValue(Control.BackgroundProperty,Brushes.Transparent);revealItem.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe MDL2 Assets"));revealItem.SetValue(Control.FontSizeProperty,10d);revealItem.SetValue(FrameworkElement.ToolTipProperty,"Показать / скрыть");revealItem.AddHandler(System.Windows.Controls.Button.ClickEvent,new RoutedEventHandler((sender,_)=>{if(sender is System.Windows.Controls.Button b&&b.DataContext is NoteItem n){store.TogglePrivateReveal("notes",n.Id.ToString());refreshPrivacy();list.Items.Refresh();if(ReferenceEquals(list.SelectedItem,n))UpdateEditor();}}));actions.AppendChild(revealItem);
        var deleteItem=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));deleteItem.SetValue(FrameworkElement.StyleProperty,actionStyle);deleteItem.SetValue(ContentControl.ContentProperty,"×");deleteItem.SetValue(FrameworkElement.WidthProperty,20d);deleteItem.SetValue(FrameworkElement.HeightProperty,22d);deleteItem.SetValue(Control.PaddingProperty,new Thickness(0));deleteItem.SetValue(Control.BackgroundProperty,Brushes.Transparent);deleteItem.SetValue(Control.FontSizeProperty,12d);deleteItem.SetValue(FrameworkElement.ToolTipProperty,"Удалить");deleteItem.AddHandler(System.Windows.Controls.Button.ClickEvent,new RoutedEventHandler((sender,_)=>{if(sender is System.Windows.Controls.Button b&&b.DataContext is NoteItem n){store.RemoveNote(n);if(store.Notes.Count==0)AddNote();else{list.SelectedItem=store.Notes.FirstOrDefault();UpdateEditor();}}}));actions.AppendChild(deleteItem);
        var preview=new FrameworkElementFactory(typeof(TextBlock));preview.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(NoteItem.Title)));preview.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);preview.SetValue(TextBlock.FontSizeProperty,11d);preview.SetValue(TextBlock.ForegroundProperty,White);preview.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        rowRoot.AppendChild(actions);rowRoot.AppendChild(preview);rowTemplate.VisualTree=rowRoot;list.ItemTemplate=rowTemplate;
        var add=Button("＋ Новая заметка",(_,_)=>AddNote());add.HorizontalAlignment=HorizontalAlignment.Stretch;add.Height=25;add.Padding=new Thickness(6,2,6,2);add.FontSize=11;
        var left=new Grid{Margin=new Thickness(0,0,5,0)};left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});Grid.SetRow(add,0);Grid.SetRow(list,1);left.Children.Add(add);left.Children.Add(list);
        var editorGrid=new Grid();editorGrid.Children.Add(editor);editorGrid.Children.Add(placeholder);var copyTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1100)};copyTimer.Tick+=(_,_)=>{copyTimer.Stop();copyButton!.Content="\uE8C8";copyButton.Foreground=White;};
        copyButton=Button("\uE8C8",(_,_)=>{if(list.SelectedItem is NoteItem note&&!string.IsNullOrWhiteSpace(note.Text)){Clipboard.SetText(note.Text);copyButton!.Content="\uE73E";copyButton.Foreground=new SolidColorBrush(Color.FromRgb(112,214,146));copyTimer.Stop();copyTimer.Start();}});copyButton.FontFamily=new FontFamily("Segoe MDL2 Assets");copyButton.FontSize=11;copyButton.Width=26;copyButton.Height=26;copyButton.Padding=new Thickness(0);copyButton.Background=Brushes.Transparent;copyButton.HorizontalAlignment=HorizontalAlignment.Right;copyButton.VerticalAlignment=VerticalAlignment.Top;copyButton.ToolTip="Копировать заметку";copyButton.Visibility=Visibility.Collapsed;Grid.SetZIndex(copyButton,1);editorGrid.Children.Add(copyButton);
        var editorSurface=new Border{Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(26,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10),Child=editorGrid};
        var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(170)}); grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(left,0); Grid.SetColumn(editorSurface,1); grid.Children.Add(left); grid.Children.Add(editorSurface);
        list.SelectedItem=store.Notes.FirstOrDefault(x=>x.Id==store.SelectedNoteId)??store.Notes.FirstOrDefault();
        UpdateEditor();
        return grid;
    }

    private sealed class ShelfNameConverter:System.Windows.Data.IValueConverter
    {
        internal static readonly ShelfNameConverter Instance=new();
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)
        {
            if(value is not string path)return "";
            var name=Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));return string.IsNullOrEmpty(name)?path:name;
        }
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ShelfIconConverter:System.Windows.Data.IValueConverter
    {
        internal static readonly ShelfIconConverter Instance=new();
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)
        {
            if(value is not string path)return "";
            if(IsImage(path))return "";
            if(Directory.Exists(path))return "▰";
            var extension=Path.GetExtension(path).TrimStart('.');return extension.Length is >0 and <=4?extension.ToUpperInvariant():"▧";
        }
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ShelfCardStateConverter:System.Windows.Data.IMultiValueConverter
    {
        internal static readonly ShelfCardStateConverter Instance=new();
        private static readonly Brush normal=new SolidColorBrush(Color.FromArgb(20,255,255,255));
        private static readonly Brush selected=new SolidColorBrush(Color.FromArgb(46,255,255,255));
        private static readonly Brush hovered=new SolidColorBrush(Color.FromArgb(36,255,255,255));
        public object Convert(object[] values,Type targetType,object parameter,System.Globalization.CultureInfo culture)
            =>values.Length>1&&values[0] is true?selected:values.Length>1&&values[1] is true?hovered:normal;
        public object[] ConvertBack(object value,Type[] targetTypes,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ClipboardGlyphConverter:System.Windows.Data.IValueConverter
    {
        internal static readonly ClipboardGlyphConverter Instance=new();
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is ClipboardEntry entry?entry.Glyph:"≡";
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private static bool IsImage(string path)=>new[]{".png",".jpg",".jpeg",".tif",".tiff",".bmp",".gif",".webp",".heic",".heif"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);

    private static FrameworkElement TeleprompterPane(string panelKey)
    {
        return new TeleprompterView(panelKey);
    }

    private static FrameworkElement SettingsPane(Action refreshRail,Action refreshDisplays,Action refreshPrivacy)
    {
        var content=new StackPanel();
        content.Children.Add(new TextBlock{Text="Настройки Veyron · локальное хранение",Foreground=White,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        if(DataStore.Current.ConfigFileBroken)
        {
            content.Children.Add(new TextBlock{Text="config.json повреждён: настройки не перезаписываются. Исправьте файл вручную.",Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4)});
            content.Children.Add(Button("Открыть повреждённый config.json",(_,_)=>
            {
                var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cyclop","config.json");
                Process.Start(new ProcessStartInfo("notepad.exe",path){UseShellExecute=true});
            }));
        }
        var startup=new CheckBox{Content="Запускать при входе в Windows",IsChecked=DataStore.Current.LaunchAtLogin,Foreground=White,Margin=new Thickness(0,4,0,4)};
        startup.Checked+=async(_,_)=>{await DataStore.Current.SetLaunchAtLoginAsync(true);startup.IsChecked=DataStore.Current.LaunchAtLogin;};startup.Unchecked+=async(_,_)=>{await DataStore.Current.SetLaunchAtLoginAsync(false);startup.IsChecked=DataStore.Current.LaunchAtLogin;};content.Children.Add(startup);
        var tray=new CheckBox{Content="Показывать значок Veyron в области уведомлений",IsChecked=DataStore.Current.ShowTrayIcon,Foreground=White,Margin=new Thickness(0,4,0,4)};
        tray.Checked+=(_,_)=>DataStore.Current.SetShowTrayIcon(true);tray.Unchecked+=(_,_)=>DataStore.Current.SetShowTrayIcon(false);content.Children.Add(tray);
        content.Children.Add(new TextBlock{Text="Скрывать содержимое при демонстрации экрана",Foreground=Muted,Margin=new Thickness(0,6,0,4)});
        var syncingPrivacy=false;
        var privacyAll=new CheckBox{Content="Все разделы",IsChecked=DataStore.Current.HideSensitiveContents,Foreground=White,Margin=new Thickness(0,2,0,4)};
        privacyAll.Checked+=(_,_)=>{if(syncingPrivacy)return;DataStore.Current.SetHideSensitiveContents(true);refreshPrivacy();};privacyAll.Unchecked+=(_,_)=>{if(syncingPrivacy)return;DataStore.Current.SetHideSensitiveContents(false);refreshPrivacy();};content.Children.Add(privacyAll);
        foreach(var section in new[]{("clipboard","Буфер обмена"),("snippets","Заготовки"),("calendar","Календарь"),("notes","Заметки")})
        {
            var check=new CheckBox{Content=section.Item2,IsChecked=DataStore.Current.PrivacySections.Contains(section.Item1),Foreground=White,Margin=new Thickness(14,2,0,2)};
            check.Checked+=(_,_)=>{DataStore.Current.SetPrivacySection(section.Item1,true);syncingPrivacy=true;privacyAll.IsChecked=DataStore.Current.HideSensitiveContents;syncingPrivacy=false;refreshPrivacy();};
            check.Unchecked+=(_,_)=>{DataStore.Current.SetPrivacySection(section.Item1,false);syncingPrivacy=true;privacyAll.IsChecked=DataStore.Current.HideSensitiveContents;syncingPrivacy=false;refreshPrivacy();};
            content.Children.Add(check);
        }
        var displays=new CheckBox{Content="Показывать панель на всех дисплеях",IsChecked=DataStore.Current.ShowOnAllDisplays,Foreground=White,Margin=new Thickness(0,4,0,4)};
        displays.Checked+=(_,_)=>{DataStore.Current.SetDisplayOptions(true,DataStore.Current.FullSizeDrawnNotch);refreshDisplays();};displays.Unchecked+=(_,_)=>{DataStore.Current.SetDisplayOptions(false,DataStore.Current.FullSizeDrawnNotch);refreshDisplays();};content.Children.Add(displays);
        var fullNotch=new CheckBox{Content="Полоска на всю высоту строки меню",IsChecked=DataStore.Current.FullSizeDrawnNotch,Foreground=White,Margin=new Thickness(0,4,0,4)};
        fullNotch.Checked+=(_,_)=>DataStore.Current.SetDisplayOptions(DataStore.Current.ShowOnAllDisplays,true);fullNotch.Unchecked+=(_,_)=>DataStore.Current.SetDisplayOptions(DataStore.Current.ShowOnAllDisplays,false);content.Children.Add(fullNotch);
        content.Children.Add(new TextBlock{Text="Показывать вкладки",Foreground=Muted,Margin=new Thickness(0,8,0,4)});
        foreach(var pair in new[]{("media","Музыка"),("shelf","Полка"),("clipboard","Буфер"),("snippets","Заготовки"),("calendar","Календарь"),("translate","Перевод"),("currency","Валюта"),("notes","Заметки"),("teleprompter","Телесуфлёр"),("utilities","Утилиты")})
        {
            var check=new CheckBox{Content=pair.Item2,IsChecked=!DataStore.Current.HiddenTabs.Contains(pair.Item1),Foreground=White,Margin=new Thickness(0,2,0,2)};
            check.Checked+=(_,_)=>{DataStore.Current.SetTabVisible(pair.Item1,true);refreshRail();};
            check.Unchecked+=(_,_)=>{DataStore.Current.SetTabVisible(pair.Item1,false);refreshRail();};content.Children.Add(check);
        }
        content.Children.Add(new TextBlock{Text="Календарь",Foreground=Muted,Margin=new Thickness(0,8,0,4)});
        var calendarSources=CalendarSourcesSettings();calendarSources.Visibility=Visibility.Collapsed;
        var calendarSourcesButton=Button("Источники календаря ▾",(_,_)=>calendarSources.Visibility=calendarSources.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible);
        content.Children.Add(calendarSourcesButton);content.Children.Add(calendarSources);
        content.Children.Add(new TextBlock{Text="Скриншоты и файлы",Foreground=Muted,Margin=new Thickness(0,8,0,4)});
        var saveImages=new CheckBox{Content="Сохранять изображения из буфера",IsChecked=DataStore.Current.SaveClipboardImages,Foreground=White,Margin=new Thickness(0,3,0,3)};
        saveImages.Checked+=(_,_)=>DataStore.Current.SetScreenshotOptions(true,DataStore.Current.WatchScreenshotFolder);saveImages.Unchecked+=(_,_)=>DataStore.Current.SetScreenshotOptions(false,DataStore.Current.WatchScreenshotFolder);content.Children.Add(saveImages);
        var watchScreenshots=new CheckBox{Content="Добавлять новые снимки из выбранной папки",IsChecked=DataStore.Current.WatchScreenshotFolder,Foreground=White,Margin=new Thickness(0,3,0,3)};
        var watchFolderLabel=new TextBlock{Text=DataStore.Current.WatchScreenshotFolder?DataStore.Current.ScreenshotWatchFolderPath:"Отслеживание выключено",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(16,0,0,4)};
        watchScreenshots.Checked+=(_,_)=>{var path=DataStore.Current.ScreenshotWatchFolderPath;var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Выберите папку для отслеживания снимков",DefaultDirectory=Directory.Exists(path)?path:Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)};if(dialog.ShowDialog()==true){DataStore.Current.SetScreenshotWatchFolder(dialog.FolderName);watchFolderLabel.Text=dialog.FolderName;}else watchScreenshots.IsChecked=false;};
        watchScreenshots.Unchecked+=(_,_)=>{DataStore.Current.SetScreenshotOptions(DataStore.Current.SaveClipboardImages,false);watchFolderLabel.Text="Отслеживание выключено";};content.Children.Add(watchScreenshots);content.Children.Add(watchFolderLabel);
        content.Children.Add(Button("Открыть папку скриншотов",(_,_)=>Process.Start(new ProcessStartInfo(DataStore.Current.ScreenshotFolder){UseShellExecute=true})));
        var usage=new TextBlock{Foreground=Muted,Margin=new Thickness(0,3,0,3)};
        Button clearScreenshots=null!;
        clearScreenshots=Button("Очистить снимки → Корзина",async(_,_)=>{DataStore.Current.ClearScreenshots();await UpdateScreenshotUsage();});
        async Task UpdateScreenshotUsage(){var value=await DataStore.Current.ScreenshotUsageAsync();usage.Text=$"Элементов: {value.Files} · {value.Bytes:N0} байт";clearScreenshots.IsEnabled=value.Files>0;}
        clearScreenshots.Loaded+=async(_,_)=>await UpdateScreenshotUsage();content.Children.Add(usage);content.Children.Add(clearScreenshots);
        content.Children.Add(Button("Открыть файл заготовок",(_,_)=>{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cyclop","snippets.json");if(!File.Exists(path))File.WriteAllText(path,"[]");Process.Start(new ProcessStartInfo("notepad.exe",path){UseShellExecute=true});}));
        content.Children.Add(Button("Открыть config.json",(_,_)=>{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cyclop","config.json");Process.Start(new ProcessStartInfo("notepad.exe",path){UseShellExecute=true});}));
        content.Children.Add(Button("Открыть папку данных",(_,_)=>Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cyclop")){UseShellExecute=true})));
        content.Children.Add(Button("Выход из Veyron",(_,_)=>Application.Current.Shutdown()));
        return new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
    }

    private static FrameworkElement Layout(string help, params UIElement[] elements)
    {
        var stack=new StackPanel(); stack.Children.Add(new TextBlock{Text=Localization.Text(help),Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        foreach(var element in elements) { if(element is FrameworkElement f) f.Margin=new Thickness(0,3,0,3); stack.Children.Add(element); }
        return new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    private static Button Button(string text, RoutedEventHandler action)
    { var button=new Button{Content=Localization.Text(text),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,7,3)}; button.Click+=action; return button; }
    private static bool MatchesSnippetSearch(string text,string query)
    {
        var needle=query.Trim();
        return needle.Length==0||System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(text,needle,System.Globalization.CompareOptions.IgnoreCase|System.Globalization.CompareOptions.IgnoreNonSpace)>=0;
    }
}
