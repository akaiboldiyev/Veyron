using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace Cyclop.Windows;

internal static class Localization
{
    private static readonly Lazy<IReadOnlyDictionary<string,string>> russianToEnglish=new(LoadTranslations);
    internal static bool UseEnglish=>!string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,"ru",StringComparison.OrdinalIgnoreCase);

    public static string Text(string value)
    {
        if(!UseEnglish)return value;
        var translations=russianToEnglish.Value;
        if(translations.TryGetValue(value,out var translated))return translated;
        foreach(var pair in translations.OrderByDescending(x=>x.Key.Length))
            if(pair.Key.EndsWith(": ",StringComparison.Ordinal)&&value.StartsWith(pair.Key,StringComparison.Ordinal))
                return pair.Value+value[pair.Key.Length..];
        var countdown=Regex.Match(value,@"^Через (\d+) (мин\.|ч\.|дн\.)(?: (\d+) мин\.)?$");
        if(countdown.Success)
        {
            var count=countdown.Groups[1].Value;
            var unit=countdown.Groups[2].Value switch {"мин."=>"min","ч."=>"hr","дн."=>"day",_=>""};
            var suffix=countdown.Groups[3].Success?$" {countdown.Groups[3].Value} min":"";
            return $"In {count} {unit}{(count=="1"?"":"s")}{suffix}";
        }
        if(value.StartsWith("Идёт сейчас · ещё ",StringComparison.Ordinal))
            return "In progress · " + Text(value["Идёт сейчас · ещё ".Length..]) + " left";
        if(Regex.IsMatch(value,@"^\d+ ч\.$"))return value[..^3]+" hr"+(value[..^3]=="1"?"":"s");
        if(Regex.IsMatch(value,@"^\d+ мин\.$"))return value[..^4]+" min";
        if(value=="меньше минуты")return "less than a minute";
        var pairMatch=Regex.Match(value,@"^Пара ([A-Z]{3}) → ([A-Z]{3}) недоступна\.$");
        if(pairMatch.Success)return $"{pairMatch.Groups[1].Value} → {pairMatch.Groups[2].Value} is unavailable.";
        var itemCount=Regex.Match(value,@"^Элементов: (\d+) · ([\d,]+) байт$");
        if(itemCount.Success)return $"{itemCount.Groups[1].Value} items · {itemCount.Groups[2].Value} bytes";
        return value;
    }

    public static void Apply(FrameworkElement root)
    {
        if(!UseEnglish)return;
        var visited=new HashSet<DependencyObject>();ApplyNode(root,visited);
    }

    private static void ApplyNode(DependencyObject node,HashSet<DependencyObject> visited)
    {
        if(!visited.Add(node))return;
        if(node is TextBlock text&&!BindingOperations.IsDataBound(text,TextBlock.TextProperty))
            text.Text=Text(text.Text);
        if(node is ContentControl content&&!BindingOperations.IsDataBound(content,ContentControl.ContentProperty)&&content.Content is string value)
            content.Content=Text(value);
        if(node is HeaderedItemsControl header&&!BindingOperations.IsDataBound(header,HeaderedItemsControl.HeaderProperty)&&header.Header is string title)
            header.Header=Text(title);
        if(node is FrameworkElement element&&!BindingOperations.IsDataBound(element,FrameworkElement.ToolTipProperty)&&element.ToolTip is string tip)
            element.ToolTip=Text(tip);

        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())ApplyNode(child,visited);
        if(node is Visual or System.Windows.Media.Media3D.Visual3D)
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ApplyNode(VisualTreeHelper.GetChild(node,i),visited);
    }

    private static IReadOnlyDictionary<string,string> LoadTranslations()
    {
        try
        {
            var folder=Path.Combine(AppContext.BaseDirectory,"Localization");
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var name in new[]{"ru-to-en.json","windows-ru-to-en.json"})
            {
                var path=Path.Combine(folder,name);
                if(!File.Exists(path))continue;
                var values=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path));
                if(values is not null)foreach(var pair in values)result[pair.Key]=pair.Value;
            }
            return result;
        }
        catch{return new Dictionary<string,string>();}
    }
}
