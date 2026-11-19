using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LightReader;

public sealed record SpeechTuning(double Rate = 1, double Pitch = 1, double Volume = 1)
{
    public SpeechTuning Clamp() => new(Math.Clamp(Rate,.5,2),Math.Clamp(Pitch,.5,2),Math.Clamp(Volume,0,1));
}
public sealed record SpeechMark(double Seconds, int Start, int Length);
public sealed record ReaderProgress(int Start, int Length);
public sealed record ReaderHistory(string Id, string Text, DateTimeOffset Time)
{
    public string? ImageId { get; init; }
    public string Source { get; init; } = "文本";
    public string? Note { get; init; }
    public string Preview => string.IsNullOrWhiteSpace(Text)?(ImageId is not null?"图片记录 · "+(Note??"暂无识别文字"):"空文本"):(Text.Length > 85 ? Text[..85] + "…" : Text).ReplaceLineEndings(" ");
    public string Caption => $"{Time.LocalDateTime:MM-dd HH:mm} · {Source} · {Text.Length} 字{(ImageId is null?"":" · 含原图")}";
}
public sealed class ReaderState
{
    public string Text { get; set; } = "";
    public bool Dark { get; set; }
    public string? ThemeMode {get;set;}
    public bool ReducedTransparency { get; set; }
    public ReadingPreferences ReadingPreferences {get;set;}=new();
    public double FontSize { get; set; } = 18;
    public SpeechTuning Tuning { get; set; } = new();
    public List<ReaderHistory> History { get; set; } = new();
    public string? CurrentHistoryId { get; set; }
    public string? CurrentImageId { get; set; }
    public string CurrentSource { get; set; } = "文本";
    public bool SuppressCurrentHistory { get; set; }
    static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LightReader","native-reader-v2.dat");
    public static bool HasSavedState(string? path=null)=>File.Exists(path??PathName)||path is null&&File.Exists(Path.Combine(Path.GetDirectoryName(PathName)!,"native-reader.dat"));
    public static ReaderState Load(string? path = null)
    {
        var source=path??PathName;
        if(path is null&&!File.Exists(source))source=Path.Combine(Path.GetDirectoryName(PathName)!,"native-reader.dat");
        return !File.Exists(source)?new():JsonSerializer.Deserialize<ReaderState>(ProtectedData.Unprotect(File.ReadAllBytes(source),null,DataProtectionScope.CurrentUser))??new();
    }
    public void Save(string? path = null)
    {
        path??=PathName;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path+".tmp",ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this),null,DataProtectionScope.CurrentUser));
        File.Move(path+".tmp",path,true);
    }
    public void Remember(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        Upsert(text,null,"文本",History.FirstOrDefault(x=>x.Text==text&&x.ImageId is null)?.Id);
    }
    public ReaderHistory? Upsert(string text,string? imageId,string source,string? id=null,string? note=null)
    {
        if(string.IsNullOrWhiteSpace(text)&&imageId is null)return null;
        var old=History.FirstOrDefault(x=>x.Id==id);
        if(old is not null&&old.Text==text&&old.ImageId==imageId&&old.Note==note&&old.Source==source)return old;
        var item=new ReaderHistory(old?.Id??Guid.NewGuid().ToString("N"),text,DateTimeOffset.Now){ImageId=imageId,Source=source,Note=note};
        History.RemoveAll(x=>x.Id==item.Id);History.Insert(0,item);History=History.Take(30).ToList();return item;
    }
    public static ReaderState Import(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement;
        if(root.TryGetProperty("values",out var legacy))
        {
            var state=new ReaderState();
            if(legacy.TryGetProperty("textReader_inputText",out var text))state.Text=text.GetString()??"";
            if(legacy.TryGetProperty("textReader_theme",out var theme))state.Dark=theme.GetString()=="dark";
            if(legacy.TryGetProperty("textReader_settings",out var settings))
            {
                using var s=JsonDocument.Parse(settings.GetString()??"{}");
                double Number(string key,double fallback)=>s.RootElement.TryGetProperty(key,out var v)&&v.TryGetDouble(out var n)&&double.IsFinite(n)?n:fallback;
                state.Tuning=new SpeechTuning(Number("rate",1),Number("pitch",1),Number("volume",1)).Clamp();
            }
            if(legacy.TryGetProperty("textReader_history",out var history))
            {
                using var h=JsonDocument.Parse(history.GetString()??"[]");
                foreach(var item in h.RootElement.EnumerateArray().Take(30))
                    if(item.TryGetProperty("text",out var t)&&t.GetString() is string body)
                        state.History.Add(new(Guid.NewGuid().ToString("N"),body,item.TryGetProperty("time",out var when)&&when.TryGetInt64(out var millis)&&millis>=0&&millis<253402300800000?DateTimeOffset.FromUnixTimeMilliseconds(millis):DateTimeOffset.Now));
            }
            return state;
        }
        var native=JsonSerializer.Deserialize<ReaderState>(json)??throw new InvalidOperationException("无效的阅读数据。");
        native.Tuning=(native.Tuning??new()).Clamp();
        native.History=(native.History??new()).Where(x=>!string.IsNullOrWhiteSpace(x.Text)||x.ImageId is not null).Take(30).ToList();
        native.FontSize=Math.Clamp(native.FontSize,13,32);
        return native;
    }
}
public static class ReaderText
{
    public static IReadOnlyList<(string Text,int Start)> Tokens(string text) => Regex.Matches(text,@"[\u3400-\u9fff]|[A-Za-z]+(?:'[A-Za-z]+)?|\r\n|\n|[\uD800-\uDBFF][\uDC00-\uDFFF]|[\s\S]")
        .Select(m=>(m.Value,m.Index)).ToList();
    public static IReadOnlyList<(string Word,int Start)> EnglishWords(string text) => Regex.Matches(text,@"[A-Za-z]{2,}(?:'[A-Za-z]+)?")
        .Select(m=>(Word:m.Value.ToLowerInvariant(),Start:m.Index)).DistinctBy(x=>x.Word).Take(200).ToList();
}
