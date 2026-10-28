using System.Net.Http;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Cyclop.Windows;

internal sealed class CurrencyService
{
    private const string Cdn="https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1";
    private const string Mirror="https://latest.currency-api.pages.dev/v1";
    private readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(20)};
    private readonly ConcurrentDictionary<string,(string Date,Dictionary<string,double> Rates)> cache=new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim ratesGate=new(1,1);
    private readonly SemaphoreSlim currenciesGate=new(1,1);
    private IReadOnlyList<CurrencyOption>? currencies;
    private static readonly string[] CurrencyPinboard=["USD","EUR","RUB","GBP","CNY","JPY","CHF","TRY","KZT","UAH","BYN","PLN","AED","BTC","ETH","USDT"];
    public static CurrencyService Current{get;}=new();
    public event Action? RatesUpdated;
    public string? RateDateFor(string baseCode)=>cache.TryGetValue(baseCode.Trim(),out var value)?value.Date:null;
    public bool IsRateCurrent(string baseCode)=>string.Equals(RateDateFor(baseCode),DateTime.UtcNow.ToString("yyyy-MM-dd"),StringComparison.Ordinal);
    public async Task<IReadOnlyList<CurrencyOption>> GetCurrenciesAsync()
    {
        await currenciesGate.WaitAsync();
        try
        {
            if(currencies is not null)return currencies;
            Exception? last=null;
            foreach(var host in new[]{Cdn,Mirror})
            try
            {
                var json=await GetUncachedTextAsync($"{host}/currencies.min.json");
                var names=JsonSerializer.Deserialize<Dictionary<string,string>>(json)??throw new JsonException("Список валют пуст.");
                currencies=names.Select(x=>new CurrencyOption(x.Key.ToUpperInvariant(),string.IsNullOrWhiteSpace(x.Value)?x.Key.ToUpperInvariant():x.Value))
                    .OrderBy(x=>Array.IndexOf(CurrencyPinboard,x.Code)<0?1:0)
                    .ThenBy(x=>{var index=Array.IndexOf(CurrencyPinboard,x.Code);return index<0?int.MaxValue:index;})
                    .ThenBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).ToArray();
                return currencies;
            }
            catch(Exception ex){last=ex;}
            throw last??new HttpRequestException("Список валют недоступен.");
        }
        finally{currenciesGate.Release();}
    }
    public async Task<Dictionary<string,double>> GetRatesAsync(string baseCode,bool force=false)
    {
        baseCode=baseCode.Trim().ToLowerInvariant();
        await ratesGate.WaitAsync();
        try
        {
            if(!force&&cache.TryGetValue(baseCode,out var found)&&found.Date==DateTime.UtcNow.ToString("yyyy-MM-dd")){RatesUpdated?.Invoke();return found.Rates;}
            Exception? last=null;
            foreach(var host in new[]{Cdn,Mirror})
            try
            {
                var json=await GetUncachedTextAsync($"{host}/currencies/{Uri.EscapeDataString(baseCode)}.min.json");
                using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var date=root.GetProperty("date").GetString()??"";
                var rates=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase){{baseCode,1}};
                foreach(var property in root.GetProperty(baseCode).EnumerateObject())if(property.Value.TryGetDouble(out var value))rates[property.Name]=value;
                cache[baseCode]=(date,rates);RatesUpdated?.Invoke();return rates;
            }
            catch(Exception ex){last=ex;}
            if(cache.TryGetValue(baseCode,out var stale)){RatesUpdated?.Invoke();return stale.Rates;}
            throw last??new HttpRequestException("Курсы недоступны.");
        }
        finally{ratesGate.Release();}
    }
    private async Task<string> GetUncachedTextAsync(string url)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.TryAddWithoutValidation("Cache-Control","no-cache");
        request.Headers.TryAddWithoutValidation("Pragma","no-cache");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    public static double? Parse(string value)
    {
        var s=value.Trim().Replace("\u00a0","").Replace(" ","");if(s.Length==0)return null;
        if(s.Contains(',')&&s.Contains('.'))
        {
            var comma=s.LastIndexOf(',');var dot=s.LastIndexOf('.');
            s=comma>dot?s.Replace(".","").Replace(',','.'):s.Replace(",","");
        }
        else if(s.Contains(','))s=s.Replace(',','.');
        return double.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var amount)&&double.IsFinite(amount)?amount:null;
    }
    public static string Format(double amount)
    {
        var abs=Math.Abs(amount);var digits=abs>=1?2:abs>=.01?4:abs>=.0001?6:8;
        return amount.ToString("N"+digits,System.Globalization.CultureInfo.CurrentCulture).TrimEnd('0').TrimEnd(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator.ToCharArray());
    }
}
