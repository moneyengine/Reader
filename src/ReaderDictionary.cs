using System.Net.Http;
using System.Text.Json;

namespace LightReader;

public sealed record DictionaryWord(string Word, int Start, string Translation = "", string Phonetic = "")
{
    public string Display => Word + (string.IsNullOrWhiteSpace(Phonetic) ? "" : "  " + Phonetic);
}
public sealed class ReaderDictionary(HttpClient http)
{
    public async Task<DictionaryWord> Lookup(DictionaryWord word,CancellationToken ct)
    {
        string translation="",phonetic="";
        try {
            using var response=await http.GetAsync("https://api.mymemory.translated.net/get?q="+Uri.EscapeDataString(word.Word)+"&langpair=en%7Czh-CN",ct);
            response.EnsureSuccessStatusCode();
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root=doc.RootElement;
            if(root.TryGetProperty("responseStatus",out var status)&&status.TryGetInt32(out var n)&&n!=200)
                translation="翻译暂不可用";
            else if(root.TryGetProperty("responseData",out var data)&&data.TryGetProperty("translatedText",out var text))
                translation=text.GetString()??"";
            if(translation.Contains("MYMEMORY WARNING",StringComparison.OrdinalIgnoreCase))translation="翻译额度暂用尽";
        }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch {translation="翻译失败，点击刷新重试";}
        if(string.IsNullOrWhiteSpace(translation)||translation.Contains("不可用")||translation.Contains("失败")||translation.Contains("额度")||!translation.Any(c=>c>127)) {
            try {
                using var response=await http.GetAsync("https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q="+Uri.EscapeDataString(word.Word),ct);
                if(response.IsSuccessStatusCode) {
                    using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var translated=string.Concat(doc.RootElement[0].EnumerateArray().Select(x=>x[0].GetString()));
                    if(!string.IsNullOrWhiteSpace(translated))translation=translated;
                }
            }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch { }
        }
        try {
            using var response=await http.GetAsync("https://api.dictionaryapi.dev/api/v2/entries/en/"+Uri.EscapeDataString(word.Word),ct);
            if(response.IsSuccessStatusCode) {
                using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if(doc.RootElement.GetArrayLength()>0) {
                    var first=doc.RootElement[0];
                    if(first.TryGetProperty("phonetic",out var p))phonetic=p.GetString()??"";
                    else if(first.TryGetProperty("phonetics",out var ps))foreach(var candidate in ps.EnumerateArray())
                        if(candidate.TryGetProperty("text",out p)){phonetic=p.GetString()??"";break;}
                }
            }
        }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch { }
        return word with {Translation=translation,Phonetic=phonetic};
    }
}
