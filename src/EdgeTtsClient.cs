using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LightReader;

public sealed record EdgeVoice(string Id, string Locale, string Gender, string Name);
public sealed record EdgeAudio(byte[] Mp3, IReadOnlyList<SpeechMark> Marks);

/// <summary>Optional external edge-tts adapter; text/audio never goes into temp files or process arguments.</summary>
public sealed class EdgeTtsClient
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static IReadOnlyList<EdgeVoice> Catalogue { get; } = LoadCatalogue();
    static List<EdgeVoice> LoadCatalogue() {
        using var stream=typeof(EdgeTtsClient).Assembly.GetManifestResourceStream("LightReader.Data.edge-voices.json")!;
        return JsonSerializer.Deserialize<List<EdgeVoice>>(stream,Json)??new();
    }
    static readonly Dictionary<string,string> ChineseNames = new() {
        ["zh-CN-XiaoxiaoNeural"]="晓晓", ["zh-CN-XiaoyiNeural"]="晓伊", ["zh-CN-YunjianNeural"]="云健",
        ["zh-CN-YunxiNeural"]="云希", ["zh-CN-YunxiaNeural"]="云夏", ["zh-CN-YunyangNeural"]="云扬",
        ["zh-CN-liaoning-XiaobeiNeural"]="晓北 · 辽宁", ["zh-CN-shaanxi-XiaoniNeural"]="晓妮 · 陕西",
        ["zh-HK-HiuGaaiNeural"]="晓佳 · 粤语", ["zh-HK-HiuMaanNeural"]="晓曼 · 粤语", ["zh-HK-WanLungNeural"]="云龙 · 粤语",
        ["zh-TW-HsiaoChenNeural"]="晓臻 · 台湾", ["zh-TW-HsiaoYuNeural"]="晓雨 · 台湾", ["zh-TW-YunJheNeural"]="云哲 · 台湾"
    };
    public static List<VoiceChoice> Choices(IEnumerable<EdgeVoice>? source=null) => (source??Catalogue)
        .OrderBy(x=>x.Locale=="zh-CN"?0:x.Locale.StartsWith("zh-")?1:x.Locale.StartsWith("en-")?2:3)
        .ThenBy(x=>x.Id).Select(x=>new VoiceChoice($"{(ChineseNames.TryGetValue(x.Id,out var name)?name:x.Id.Replace("Neural",""))} · {x.Locale} · {(x.Gender=="Female"?"女声":"男声")}",x.Id)).ToList();

    public async Task<string> CheckEnvironment(string? python,CancellationToken ct) {
        using var result=await Run("--check",null,python,ct);
        var version=result.RootElement.GetProperty("version").GetString()??"";
        if(!Version.TryParse(version,out var parsed)||parsed<new Version(7,3,1))throw new InvalidOperationException("请将 edge-tts 更新到 7.3.1 或以上，详见免费自然语音说明。");
        return $"edge-tts {version} · {result.RootElement.GetProperty("python").GetString()}";
    }
    public async Task<List<EdgeVoice>> RefreshVoices(string? python,CancellationToken ct) {
        using var doc=await Run("--voices",null,python,ct);
        var items=JsonSerializer.Deserialize<List<EdgeVoice>>(doc.RootElement.GetProperty("voices").GetRawText(),Json)??new();
        if(items.Count==0||items.Count>5000||items.Any(x=>string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.Locale)))throw new InvalidOperationException("在线音色列表无效，请稍后重试。");
        return items.DistinctBy(x=>x.Id).ToList();
    }
    public static object Request(string text,string voice,SpeechTuning tuning) {
        var t=tuning.Clamp();string Signed(int n,string unit)=>n.ToString("+0;-0;+0",CultureInfo.InvariantCulture)+unit;
        return new{text,voice,rate=Signed((int)Math.Round((t.Rate-1)*100),"%"),pitch=Signed((int)Math.Round((t.Pitch-1)*100),"Hz")};
    }
    public async Task<EdgeAudio> Generate(string text,string voice,SpeechTuning tuning,string? python,CancellationToken ct) {
        if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new InvalidOperationException("自然语音段落为空或过长。");
        // Filtering leaves offset-preserving blanks. Do not send hundreds of blanks to TTS.
        var compact=new StringBuilder();var offsets=new List<int>();bool blank=false;
        for(int i=0;i<text.Length;i++){
            if(char.IsWhiteSpace(text[i])){if(compact.Length>0&&!blank){compact.Append(' ');offsets.Add(i);}blank=true;}
            else{compact.Append(text[i]);offsets.Add(i);blank=false;}
        }
        var requestText=compact.ToString().TrimEnd();
        using var result=await Run("--synthesize",Request(requestText,voice,tuning),python,ct);
        var bytes=Convert.FromBase64String(result.RootElement.GetProperty("audio").GetString()??"");
        if(bytes.Length==0||bytes.Length>20*1024*1024)throw new InvalidOperationException("自然语音返回音频为空或过大。");
        var marks=AlignMarks(requestText,result.RootElement.GetProperty("marks")).Select(m=>new SpeechMark(m.Seconds,offsets[m.Start],offsets[Math.Min(m.Start+m.Length-1,requestText.Length-1)]-offsets[m.Start]+1)).ToList();
        return new(bytes,marks);
    }
    public static List<SpeechMark> AlignMarks(string text,JsonElement events) {
        var marks=new List<SpeechMark>();var cursor=0;
        foreach(var item in events.EnumerateArray()) {
            var token=WebUtility.HtmlDecode(item.GetProperty("text").GetString()??"");if(token.Length==0)continue;
            var found=text.IndexOf(token,cursor,StringComparison.OrdinalIgnoreCase);
            if(found<0)continue;
            var time=item.GetProperty("seconds").GetDouble();if(!double.IsFinite(time)||time<0)continue;
            marks.Add(new(time,found,token.Length));cursor=found+token.Length;
        }
        return marks.OrderBy(x=>x.Seconds).ToList();
    }
    public static byte[] DecodeMp3(byte[] mp3) {
        using var memory=new MemoryStream(mp3);using var reader=new Mp3FileReader(memory);
        ISampleProvider sample=reader.ToSampleProvider();if(sample.WaveFormat.Channels==2)sample=new StereoToMonoSampleProvider(sample);
        if(sample.WaveFormat.Channels!=1)throw new InvalidOperationException("自然语音返回了不支持的音频声道。");
        if(sample.WaveFormat.SampleRate!=24000)sample=new WdlResamplingSampleProvider(sample,24000);
        var pcm=new SampleToWaveProvider16(sample);using var output=new MemoryStream();var buffer=new byte[8192];int length;
        while((length=pcm.Read(buffer,0,buffer.Length))>0)output.Write(buffer,0,length);
        return output.Length==0?throw new InvalidOperationException("自然语音解码后为空。"):output.ToArray();
    }
    static IEnumerable<(string File,string[] Prefix)> Candidates(string? configured) {
        if(!string.IsNullOrWhiteSpace(configured)){yield return(configured.Trim().Trim('"'),Array.Empty<string>());yield break;}
        var env=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LightReader","edge-tts-env","Scripts","python.exe");
        if(File.Exists(env))yield return(env,Array.Empty<string>());
        string? OnPath(string name)=> (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)
            .Where(x=>!string.IsNullOrWhiteSpace(x)&&!x.Contains("WindowsApps",StringComparison.OrdinalIgnoreCase))
            .Select(x=>Path.Combine(x.Trim('"'),name)).FirstOrDefault(File.Exists);
        var command=OnPath("python.exe");if(command is not null)yield return(command,Array.Empty<string>());
        var launcher=OnPath("py.exe");if(launcher is not null)yield return(launcher,new[]{"-3"});
    }
    static async Task<JsonDocument> Run(string mode,object? request,string? python,CancellationToken token) {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(75));var ct=timeout.Token;
        var script=Path.Combine(AppContext.BaseDirectory,"Backends","edge_tts_bridge.py");
        if(!File.Exists(script))throw new InvalidOperationException("缺少 Backends/edge_tts_bridge.py，请使用完整发布目录。");
        foreach(var candidate in Candidates(python)) {
            ct.ThrowIfCancellationRequested();
            var start=new ProcessStartInfo(candidate.File){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,StandardInputEncoding=new UTF8Encoding(false)};
            foreach(var arg in candidate.Prefix)start.ArgumentList.Add(arg);start.ArgumentList.Add("-u");start.ArgumentList.Add(script);start.ArgumentList.Add(mode);
            start.Environment["PYTHONIOENCODING"]="utf-8";start.Environment["PYTHONUTF8"]="1";
            using var process=new Process{StartInfo=start};
            try { if(!process.Start())continue; } catch(System.ComponentModel.Win32Exception) { continue; }
            using var cancel=ct.Register(()=>{try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
            try {
                var output=process.StandardOutput.ReadToEndAsync(ct);var error=process.StandardError.ReadToEndAsync(ct);
                if(request is not null)await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),ct);
                process.StandardInput.Close();await process.WaitForExitAsync(ct);var body=await output;await error;
                ct.ThrowIfCancellationRequested();
                if(body.Length==0){if(process.ExitCode!=0)continue;throw new InvalidOperationException("自然语音进程没有返回结果。");}
                if(body.Length>30*1024*1024)throw new InvalidOperationException("自然语音响应过大。");
                JsonDocument doc;
                try{doc=JsonDocument.Parse(body);}catch(JsonException){throw new InvalidOperationException("自然语音进程响应格式不正确，请检查 Python 环境。");}
                if(doc.RootElement.TryGetProperty("error",out var failure)) {
                    var dependency=doc.RootElement.TryGetProperty("code",out var code)&&code.GetString()=="dependency_missing";
                    var message=failure.GetString();doc.Dispose();if(dependency)continue;
                    throw new InvalidOperationException("Edge 自然语音请求失败："+message+"。请检查网络后重试。");
                }
                if(process.ExitCode!=0){doc.Dispose();throw new InvalidOperationException("Edge 自然语音进程异常结束。");}
                return doc;
            } catch(OperationCanceledException)when(!token.IsCancellationRequested) { throw new InvalidOperationException("Edge 自然语音连接超时，请检查网络后重试。"); }
        }
        throw new InvalidOperationException("未找到可用的 Python / edge-tts 环境。请在服务设置中检测，或运行 tools/Install-EdgeTts.ps1 安装依赖。");
    }
}
