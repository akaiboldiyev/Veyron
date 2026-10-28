using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;

namespace Cyclop.Windows;

internal sealed class CurrencyView:UserControl
{
    private static readonly IReadOnlyList<CurrencyOption> fallbackCurrencies=BuildCurrencies();
    private readonly ObservableCollection<CurrencyOption> availableCurrencies=new(fallbackCurrencies);
    private readonly TextBox sourceAmount=new(){Text=DataStore.Current.CurrencySourceAmount,FontSize=26};
    private readonly TextBox targetAmount=new(){Text=DataStore.Current.CurrencyTargetAmount,FontSize=26};
    private readonly ComboBox sourceCode;
    private readonly ComboBox targetCode;
    private readonly TextBlock sourceCodeLabel=new(){Foreground=Brushes.White,FontSize=11,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock targetCodeLabel=new(){Foreground=Brushes.White,FontSize=11,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock sourceLabel=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock targetLabel=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBox currencySearch=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),FontSize=11,Foreground=Brushes.White};
    private readonly TextBlock currencySearchPlaceholder=new(){Text="Поиск валюты",Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),FontSize=11,IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button currencySearchClear=new(){Content="×",Width=20,Height=20,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),FontSize=12,Visibility=Visibility.Collapsed,ToolTip="Очистить поиск"};
    private readonly ListBox currencyResults=new(){Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),MinHeight=0,Padding=new Thickness(0)};
    private readonly TextBlock noCurrencies=new(){Text="Нет подходящих валют",Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),FontSize=11,Margin=new Thickness(8),Visibility=Visibility.Collapsed};
    private readonly Grid converter=new();
    private readonly Grid picker=new(){Visibility=Visibility.Collapsed};
    private readonly TextBlock status=new(){Foreground=new SolidColorBrush(Color.FromRgb(154,157,167)),Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap};
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromHours(1)};
    private string editSide=DataStore.Current.CurrencyEditingSide;private string? pickingSide;private int generation;private bool updating;private bool forceRateRefresh;
    public CurrencyView()
    {
        sourceCode=CreateCurrencyPicker(DataStore.Current.SourceCurrency,availableCurrencies);
        targetCode=CreateCurrencyPicker(DataStore.Current.TargetCurrency,availableCurrencies);
        BuildViews();
        sourceAmount.TextChanged+=(_,_)=>{if(!updating){editSide="source";DataStore.Current.SetCurrencyAmounts(sourceAmount.Text,targetAmount.Text,editSide);Queue();}};
        targetAmount.TextChanged+=(_,_)=>{if(!updating){editSide="target";DataStore.Current.SetCurrencyAmounts(sourceAmount.Text,targetAmount.Text,editSide);Queue();}};
        sourceCode.SelectionChanged+=(_,_)=>{if(!updating)forceRateRefresh=true;PairChanged();};targetCode.SelectionChanged+=(_,_)=>PairChanged();
        currencySearch.TextChanged+=(_,_)=>RefreshCurrencyResults();
        currencySearch.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){if(currencySearch.Text.Length==0)ClosePicker();else currencySearch.Clear();e.Handled=true;}};
        currencyResults.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(ItemsControl.ContainerFromElement(currencyResults,e.OriginalSource as DependencyObject) is not ListBoxItem||currencyResults.SelectedItem is not CurrencyOption option||pickingSide is null)return;
            var code=pickingSide=="source"?sourceCode:targetCode;
            if(option.Code.Equals(Code(pickingSide=="source"?targetCode:sourceCode),StringComparison.OrdinalIgnoreCase))
            {
                if(pickingSide=="source")Swap();else Swap();
            }
            else SetCode(code,option.Code);
            PairChanged();UpdateCurrencyLabels();ClosePicker();
        };
        debounce.Tick+=async(_,_)=>{debounce.Stop();await ConvertAsync(generation);};
        refresh.Tick+=async(_,_)=>{try{await CurrencyService.Current.GetRatesAsync(Code(sourceCode),true);await ConvertAsync(++generation);}catch(Exception ex){status.Text=Localization.Text("Курсы недоступны: ")+ex.Message;}};
        Loaded+=async(_,_)=>{UpdateCurrencyLabels();refresh.Start();await LoadCurrenciesAsync();try{await CurrencyService.Current.GetRatesAsync(Code(sourceCode),true);}catch(Exception ex){status.Text=Localization.Text("Курс недоступен: ")+ex.Message;}await ConvertAsync(++generation);};Unloaded+=(_,_)=>refresh.Stop();
    }
    private void BuildViews()
    {
        converter.ColumnDefinitions.Add(new ColumnDefinition());converter.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});converter.ColumnDefinitions.Add(new ColumnDefinition());
        var from=AmountColumn(sourceCode,sourceLabel,sourceAmount,"source");var to=AmountColumn(targetCode,targetLabel,targetAmount,"target");
        var swap=Button("⇄",Swap);swap.Style=(Style)Application.Current.FindResource("MediaTransportButton");swap.Width=28;swap.Height=28;swap.Padding=new Thickness(0);swap.FontSize=12;swap.Background=new SolidColorBrush(Color.FromArgb(20,255,255,255));swap.Margin=new Thickness(8,34,8,0);swap.ToolTip="Поменять валюты местами";
        Grid.SetColumn(from,0);Grid.SetColumn(swap,1);Grid.SetColumn(to,2);converter.Children.Add(from);converter.Children.Add(swap);converter.Children.Add(to);
        converter.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});converter.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(status,1);Grid.SetColumnSpan(status,3);converter.Children.Add(status);
        var root=new Grid();root.Children.Add(converter);
        ScrollViewer.SetVerticalScrollBarVisibility(currencyResults,ScrollBarVisibility.Hidden);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,0,8,0)));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,26d));
        var itemTemplate=new ControlTemplate(typeof(ListBoxItem));var rowBackground=new FrameworkElementFactory(typeof(Border));rowBackground.Name="CurrencyRow";rowBackground.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));rowBackground.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentSourceProperty,"Content");content.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);rowBackground.AppendChild(content);itemTemplate.VisualTree=rowBackground;
        var itemHover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};itemHover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(18,255,255,255)),"CurrencyRow"));itemTemplate.Triggers.Add(itemHover);var itemSelected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};itemSelected.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(36,255,255,255)),"CurrencyRow"));itemTemplate.Triggers.Add(itemSelected);itemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));currencyResults.ItemContainerStyle=itemStyle;
        var pickerRoot=new DockPanel();
        var headerGrid=new Grid{Margin=new Thickness(9,0,5,0)};headerGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});headerGrid.ColumnDefinitions.Add(new ColumnDefinition());headerGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});headerGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var searchIcon=new TextBlock{Text="\uE721",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),Width=15,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(searchIcon,0);headerGrid.Children.Add(searchIcon);
        var searchField=new Grid{VerticalAlignment=VerticalAlignment.Center};searchField.Children.Add(currencySearch);searchField.Children.Add(currencySearchPlaceholder);Grid.SetColumn(searchField,1);headerGrid.Children.Add(searchField);
        currencySearchClear.Click+=(_,_)=>{currencySearch.Clear();currencySearch.Focus();};Grid.SetColumn(currencySearchClear,2);headerGrid.Children.Add(currencySearchClear);
        var done=Button("Готово",()=>ClosePicker());done.Padding=new Thickness(8,2,8,2);done.Margin=new Thickness(5,0,0,0);done.FontSize=11;done.Background=Brushes.Transparent;done.Foreground=Brushes.White;Grid.SetColumn(done,3);headerGrid.Children.Add(done);
        var pickerHeader=new Border{Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),CornerRadius=new CornerRadius(7),Height=24,Margin=new Thickness(0,0,0,8),Child=headerGrid};DockPanel.SetDock(pickerHeader,Dock.Top);pickerRoot.Children.Add(pickerHeader);
        var resultsHost=new Grid();resultsHost.Children.Add(currencyResults);resultsHost.Children.Add(noCurrencies);pickerRoot.Children.Add(resultsHost);
        picker.Children.Add(pickerRoot);root.Children.Add(picker);Content=root;
        currencyResults.ItemTemplate=CurrencyTemplate();
    }
    private FrameworkElement AmountColumn(ComboBox code,TextBlock name,TextBox amount,string side)
    {
        var column=new Grid();column.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});column.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var currencyButton=new Button{Padding=new Thickness(8,3,8,3),HorizontalContentAlignment=HorizontalAlignment.Stretch,Height=22,Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),BorderThickness=new Thickness(0),ToolTip="Выбрать валюту",Margin=new Thickness(0,0,0,6)};
        var heading=new DockPanel{LastChildFill=true};var codeText=ReferenceEquals(code,sourceCode)?sourceCodeLabel:targetCodeLabel;codeText.Text=Code(code).ToUpperInvariant();codeText.Margin=new Thickness(0,0,6,0);
        var chevron=new TextBlock{Text="⌄",Foreground=new SolidColorBrush(Color.FromArgb(82,255,255,255)),FontSize=9,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,0,0)};DockPanel.SetDock(chevron,Dock.Right);DockPanel.SetDock(codeText,Dock.Left);DockPanel.SetDock(name,Dock.Left);name.FontSize=9;name.Margin=new Thickness(0);heading.Children.Add(chevron);heading.Children.Add(codeText);heading.Children.Add(name);currencyButton.Content=heading;
        currencyButton.Click+=(_,_)=>OpenPicker(side);Grid.SetRow(currencyButton,0);column.Children.Add(currencyButton);
        amount.Padding=new Thickness(0);amount.Background=Brushes.Transparent;amount.BorderThickness=new Thickness(0);amount.Foreground=Brushes.White;amount.FontSize=26;amount.FontFamily=new FontFamily("Consolas");amount.MinHeight=100;amount.VerticalContentAlignment=VerticalAlignment.Top;amount.CaretBrush=new SolidColorBrush(Color.FromArgb(140,255,255,255));
        amount.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){amount.Clear();e.Handled=true;}};
        var amountSurface=new Border{Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(26,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10),Child=amount};Grid.SetRow(amountSurface,1);column.Children.Add(amountSurface);return column;
    }
    private void OpenPicker(string side)
    {
        pickingSide=side;currencySearch.Clear();RefreshCurrencyResults();
        var selectedCode=Code(side=="source"?sourceCode:targetCode);
        currencyResults.SelectedItem=availableCurrencies.FirstOrDefault(x=>x.Code.Equals(selectedCode,StringComparison.OrdinalIgnoreCase));
        converter.Visibility=Visibility.Collapsed;picker.Visibility=Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(()=>{currencySearch.Focus();Keyboard.Focus(currencySearch);}));
    }
    private void ClosePicker(){picker.Visibility=Visibility.Collapsed;converter.Visibility=Visibility.Visible;pickingSide=null;}
    private void RefreshCurrencyResults()
    {
        var query=currencySearch.Text.Trim();currencySearchPlaceholder.Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;currencySearchClear.Visibility=query.Length==0?Visibility.Collapsed:Visibility.Visible;
        var matches=availableCurrencies.Where(x=>Matches(x,query)).ToArray();currencyResults.ItemsSource=matches;noCurrencies.Visibility=matches.Length==0?Visibility.Visible:Visibility.Collapsed;
    }
    private void UpdateCurrencyLabels()
    {
        sourceLabel.Text=NameFor(Code(sourceCode));targetLabel.Text=NameFor(Code(targetCode));
        sourceCodeLabel.Text=Code(sourceCode).ToUpperInvariant();targetCodeLabel.Text=Code(targetCode).ToUpperInvariant();
    }
    private string NameFor(string code)=>availableCurrencies.FirstOrDefault(x=>x.Code.Equals(code,StringComparison.OrdinalIgnoreCase))?.Name??code.ToUpperInvariant();
    private static DataTemplate CurrencyTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(DockPanel.LastChildFillProperty,true);row.SetValue(FrameworkElement.HeightProperty,26d);
        var code=new FrameworkElementFactory(typeof(TextBlock));code.SetBinding(TextBlock.TextProperty,new Binding(nameof(CurrencyOption.Code)));code.SetValue(TextBlock.WidthProperty,44d);code.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);code.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Consolas"));code.SetValue(TextBlock.FontSizeProperty,11d);code.SetValue(TextBlock.ForegroundProperty,Brushes.White);code.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);code.SetValue(DockPanel.DockProperty,Dock.Left);row.AppendChild(code);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding(nameof(CurrencyOption.Name)));name.SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color.FromArgb(140,255,255,255)));name.SetValue(TextBlock.FontSizeProperty,11d);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(name);
        return new DataTemplate(typeof(CurrencyOption)){VisualTree=row};
    }
    private static Button Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(6,2,6,2)};b.Click+=(_,_)=>action();return b;}
    private static ComboBox CreateCurrencyPicker(string code,ObservableCollection<CurrencyOption> items)
    {
        var picker=new ComboBox{DisplayMemberPath="Label",SelectedValuePath="Code",Width=0,Height=0,Visibility=Visibility.Collapsed};
        var view=new ListCollectionView(items);
        picker.ItemsSource=view;picker.Tag=items;
        picker.SelectedValue=code.ToUpperInvariant();
        if(picker.SelectedItem is null)picker.Text=code.ToUpperInvariant();
        return picker;
    }
    private async Task LoadCurrenciesAsync()
    {
        try
        {
            var options=await CurrencyService.Current.GetCurrenciesAsync();
            if(!IsLoaded)return;
            if(options.Count==0)throw new InvalidDataException("Список валют пуст.");
            var selectedSource=Code(sourceCode);var selectedTarget=Code(targetCode);
            updating=true;
            availableCurrencies.Clear();foreach(var option in options)availableCurrencies.Add(option);
            if(sourceCode.ItemsSource is ListCollectionView sourceView)sourceView.Refresh();
            if(targetCode.ItemsSource is ListCollectionView targetView)targetView.Refresh();
            SetCode(sourceCode,selectedSource);SetCode(targetCode,selectedTarget);
            if(!options.Any(x=>x.Code.Equals(Code(sourceCode),StringComparison.OrdinalIgnoreCase)))SetCode(sourceCode,options.FirstOrDefault(x=>x.Code=="USD")?.Code??options[0].Code);
            if(!options.Any(x=>x.Code.Equals(Code(targetCode),StringComparison.OrdinalIgnoreCase)))SetCode(targetCode,options.FirstOrDefault(x=>x.Code!=Code(sourceCode))?.Code??"EUR");
            DataStore.Current.SetCurrencyPair(Code(sourceCode),Code(targetCode));
            updating=false;UpdateCurrencyLabels();RefreshCurrencyResults();
        }
        catch(Exception ex){updating=false;status.Text=Localization.Text("Не удалось загрузить список валют: ")+ex.Message;}
    }
    private static bool Matches(CurrencyOption option,string query)
    {
        var value=query.Trim();return value.Length==0||option.Code.Contains(value,StringComparison.CurrentCultureIgnoreCase)||option.Name.Contains(value,StringComparison.CurrentCultureIgnoreCase);
    }
    private static IReadOnlyList<CurrencyOption> BuildCurrencies()
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var culture in System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures))
        {
            try{var region=new System.Globalization.RegionInfo(culture.Name);var code=region.ISOCurrencySymbol;if(code.Length==3&&code!="XXX"&&!values.ContainsKey(code))values[code]=region.CurrencyEnglishName;}catch{ }
        }
        foreach(var entry in new[]{("USD","US Dollar"),("EUR","Euro"),("RUB","Russian Ruble")})values.TryAdd(entry.Item1,entry.Item2);
        return values.Select(x=>new CurrencyOption(x.Key,x.Value)).OrderBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string Code(ComboBox picker)
    {
        if(picker.SelectedItem is CurrencyOption selected)return selected.Code;
        var text=picker.Text.Trim();var separator=text.IndexOf('·');if(separator>=0)text=text[..separator].Trim();
        return text.ToUpperInvariant();
    }
    private static void SetCode(ComboBox picker,string code)
    {
        picker.SelectedValue=code.ToUpperInvariant();
        if(picker.SelectedItem is null)picker.Text=code.ToUpperInvariant();
    }
    private void PairChanged()
    {
        if(updating||sourceCode.SelectedItem is not CurrencyOption source||targetCode.SelectedItem is not CurrencyOption target)return;
        DataStore.Current.SetCurrencyPair(source.Code,target.Code);Queue();
    }
    private void Queue(){generation++;debounce.Stop();debounce.Start();}
    private void Swap()
    {
        updating=true;var source=Code(sourceCode);SetCode(sourceCode,Code(targetCode));SetCode(targetCode,source);updating=false;
        UpdateCurrencyLabels();
        DataStore.Current.SetCurrencyPair(Code(sourceCode),Code(targetCode));editSide="source";forceRateRefresh=true;DataStore.Current.SetCurrencyAmounts(sourceAmount.Text,targetAmount.Text,editSide);Queue();
    }
    private async Task ConvertAsync(int request)
    {
        var input=editSide=="source"?sourceAmount.Text:targetAmount.Text;
        if(string.IsNullOrWhiteSpace(input))
        {
            updating=true;if(editSide=="source")targetAmount.Clear();else sourceAmount.Clear();updating=false;
            DataStore.Current.SetCurrencyAmounts(sourceAmount.Text,targetAmount.Text,editSide);
            return;
        }
        var from=Code(sourceCode).ToLowerInvariant();var to=Code(targetCode).ToLowerInvariant();
        if(from.Length<3||to.Length<3){status.Text=Localization.Text("Введите трёхбуквенные коды валют.");return;}
        var amount=CurrencyService.Parse(editSide=="source"?sourceAmount.Text:targetAmount.Text);
        if(amount is null)return;
        try
        {
            status.Text=Localization.Text("Загружаю дневной курс…");var refreshRates=forceRateRefresh;forceRateRefresh=false;var rates=await CurrencyService.Current.GetRatesAsync(from,refreshRates);
            if(!rates.TryGetValue(to,out var rate)||rate<=0){status.Text=$"Пара {from.ToUpperInvariant()} → {to.ToUpperInvariant()} недоступна.";return;}
            if(request!=generation)return;
            var result=editSide=="source"?amount.Value*rate:amount.Value/rate;
            updating=true;if(editSide=="source")targetAmount.Text=CurrencyService.Format(result);else sourceAmount.Text=CurrencyService.Format(result);updating=false;
            DataStore.Current.SetCurrencyAmounts(sourceAmount.Text,targetAmount.Text,editSide);
            if(request==generation)
            {
                var date=CurrencyService.Current.RateDateFor(from)??"дата неизвестна";
                status.Text=Localization.Text(CurrencyService.Current.IsRateCurrent(from)?$"Курс от {date} · обновление раз в час.":$"Последний доступный курс от {date}.");
            }
        }
        catch(Exception ex){updating=false;if(request==generation)status.Text=Localization.Text("Курс недоступен: ")+ex.Message;}
    }
}

internal sealed record CurrencyOption(string Code,string Name)
{
    public string Label=>$"{Code} · {Name}";
}

