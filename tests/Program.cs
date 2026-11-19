using LightReader;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    static void Main(string[] args){System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;Run(args).GetAwaiter().GetResult();}
    static async Task Run(string[] args)
    {

if (args.Contains("--check-reading-typography")) { try { await ReadingTypographyChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-markdown-replacement")) { try { await MarkdownReplacementChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-slim-reader")) { try { await SlimReaderChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-reading-interactions")) { try { await ReadingInteractionChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-edge-completion")) { try { await EdgeTtsChecks.Native(true); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-stitch-html")) { try { await StitchHtmlChecks.Run(Path.GetFullPath("artifacts/stitch-web-prototype")); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-markdown")) { MarkdownChecks.Run(); return; }
if (args.Contains("--check-speech-filter")) { try { SpeechTextChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-categories-tray")) { try { await CategoryTrayChecks.Native(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-edge-ui")) { try { await EdgeTtsChecks.Native(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-edge-live")) { try { await EdgeTtsChecks.Live(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--preview-glass-ui")) { await GlassUiChecks.Run(true); return; }
if (args.Contains("--check-glass-ui")) { try { await GlassUiChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-ocr-alpha")) { try { await OcrAlphaChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-local-ocr")) { try { await LocalOcrChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if (args.Contains("--check-native-voices")) { try { await NativeReaderChecks.CheckVoices(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }

if (args.Contains("--check-native-reader")) { try { await NativeReaderChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
EdgeTtsChecks.Offline();
if (args.Contains("--check-windows"))
{
    var installed = WindowsSpeech.GetVoices();
    if (installed.Count == 0) throw new Exception("No Windows SAPI voices installed");
    Console.WriteLine("Installed voices: " + string.Join(", ", installed.Select(x => x.Name)));
    var voice = installed.FirstOrDefault(x => x.Name.Contains("zh-CN")) ?? installed[0];
    using (var synth = new WindowsSpeech())
    {
        using var stop = new CancellationTokenSource();
        var task = synth.Speak(string.Concat(Enumerable.Repeat("这是微软本机朗读测试，可以暂停、继续和停止。", 20)), voice.Id, stop.Token);
        await Task.Delay(400);
        synth.Pause(); if (!synth.Paused) throw new Exception("Pause failed");
        await Task.Delay(200); if (task.IsCompleted) throw new Exception("Paused reading completed unexpectedly");
        synth.Resume(); if (synth.Paused) throw new Exception("Resume failed");
        synth.Pause(); stop.Cancel();
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS local pause, resume, stop while paused"); }
    }
    using (var synth = new WindowsSpeech())
    {
        await synth.Speak("你好，这是免费的 Windows 本机朗读。", voice.Id, default).WaitAsync(TimeSpan.FromSeconds(25));
        Console.WriteLine("PASS fresh local playback completed after cancellation; no API key or HTTP client used");
    }
    return;
}

if (args.Contains("--repro"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
    int index = 0;
    foreach (var chunk in TextChunks.Split(File.ReadAllText("LightReader/tests/repro.txt").Trim(), 260)) {
        index++;
        Console.WriteLine($"Segment {index}: {chunk.Length} characters");
        try {
            var wav = await new AudioGenerationClient(client).Generate(chunk, "zh_female_vv_uranus_bigtts", AudioKeyStore.Load(), default);
            Console.WriteLine($"OK PCM bytes: {AudioGenerationClient.ToPcm(wav).Length}");
        } catch (Exception ex) { Console.WriteLine(ex.Message); break; }
    }
    return;
}
if (args.Contains("--play-sample"))
{
    var pcm = AudioGenerationClient.ToPcm(File.ReadAllBytes("artifacts/朗读测试.wav"));
    using var player = new Playback();
    await player.Add(pcm, default);
    await player.Drain(default);
    Console.WriteLine("Sample playback completed through the default audio device.");
    return;
}

if (args.Contains("--check-audio"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try {
        var wav = await new AudioGenerationClient(client).Generate("你好，这是轻读的朗读测试。请放松，慢慢听。", Settings.Load().AudioVoice, AudioKeyStore.Load(), default);
        var pcm = AudioGenerationClient.ToPcm(wav);
        Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/朗读测试.wav", wav);
        Console.WriteLine($"Audio generation OK: {watch.Elapsed.TotalSeconds:F2}s, PCM {pcm.Length} bytes ({pcm.Length / 48000.0:F2}s).");
    } catch (Exception ex) { Console.WriteLine(ex.Message); Environment.ExitCode = 1; }
    return;
}

if (args.Contains("--check-services"))
{
    var saved = Settings.Load();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    string Safe(string message) {
        foreach (var key in new[] { saved.ArkKey, saved.SpeechKey }) if (!string.IsNullOrEmpty(key)) message = message.Replace(key, "[redacted]");
        return message;
    }
    try { var ids = await new OcrClient(client).ListModels(saved.ArkKey, deadline.Token); Console.WriteLine($"Ark model listing OK: {ids.Length} visible models."); }
    catch (Exception ex) { Console.WriteLine("Ark: " + Safe(ex.Message)); }
    try { await new DuplexClient().Speak("", saved, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, deadline.Token); Console.WriteLine("Seeduplex session OK (no text or audio sent)."); }
    catch (Exception ex) { Console.WriteLine("Speech: " + Safe(ex.Message)); }
    return;
}

if (args.Contains("--capture-check"))
{
    Exception? failure = null;
    var thread = new Thread(() => {
        try {
            var bytes = CaptureWindow.Select();
            if (bytes is null) { File.WriteAllText("capture-check.txt", "CANCELLED"); return; }
            File.WriteAllBytes("capture-check.png", bytes);
            using var stream = new MemoryStream(bytes);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(stream);
            File.WriteAllText("capture-check.txt", $"PNG {frame.PixelWidth} x {frame.PixelHeight}");
        } catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (failure is not null) throw failure;
    return;
}

int passed = 0;
using (var dictionaryHttp = new HttpClient(new DictionaryFixtureHandler())) {
    var result=await new ReaderDictionary(dictionaryHttp).Lookup(new("hello",3),default);
    if(result.Translation!="你好"||result.Phonetic!="/hello/"||result.Start!=3)throw new Exception("Native dictionary response parsing failed");
    Console.WriteLine("PASS native dictionary translation, phonetic and source position");
}
if (VoiceCatalog.All.Count < 600) throw new Exception("Official catalog incomplete");
if (VoiceCatalog.All.Select(x => x.Id).Distinct().Count() != VoiceCatalog.All.Count) throw new Exception("Duplicate catalog IDs");
if (!VoiceCatalog.All.Any(x => x.Id == "zh_female_vv_uranus_bigtts" && x.Compatible)) throw new Exception("Missing default voice");
if (!VoiceCatalog.All.Any(x => x.Id.Contains('-') && x.Compatible)) throw new Exception("Hyphenated IDs lost");
if (!VoiceCatalog.Choices("自定义|custom-test-voice").Any(x => x.Id == "custom-test-voice")) throw new Exception("Custom voice lost");
Console.WriteLine($"PASS catalog: {VoiceCatalog.All.Count} distinct voices, {VoiceCatalog.All.Count(x => x.Compatible)} TTS2; default, hyphenated and custom IDs preserved");
{
    var valid = System.Text.Json.Nodes.JsonNode.Parse("{\"model\":\"seed-audio-1.0\",\"text_prompt\":\"test\",\"audio_config\":{\"format\":\"wav\",\"sample_rate\":24000},\"references\":[{\"speaker\":\"voice\"}]}")!.AsObject();
    AudioDebugValidation.Validate(valid);
    Console.WriteLine("PASS debug request valid");
    var debugInvalid = (System.Text.Json.Nodes.JsonObject)valid.DeepClone();
    debugInvalid["audio_config"]!["format"] = "ogg_opus";
    try { AudioDebugValidation.Validate(debugInvalid); throw new Exception("debugInvalid rate accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS opus rate rejected"); }
    debugInvalid = (System.Text.Json.Nodes.JsonObject)valid.DeepClone();
    debugInvalid["references"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["image_url"] = "https://example.com/image.png" });
    try { AudioDebugValidation.Validate(debugInvalid); throw new Exception("mixed references accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS mixed reference types rejected"); }
    debugInvalid = (System.Text.Json.Nodes.JsonObject)valid.DeepClone();
    debugInvalid["text_prompt"] = new string('x', 3001);
    try { AudioDebugValidation.Validate(debugInvalid); throw new Exception("oversized prompt accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS prompt limit rejected"); }
    debugInvalid = (System.Text.Json.Nodes.JsonObject)valid.DeepClone();
    debugInvalid["audio_config"]!["pitch_rate"] = 13;
    try { AudioDebugValidation.Validate(debugInvalid); throw new Exception("debugInvalid pitch accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS pitch range rejected"); }
}
if (!AudioGenerationClient.DescribeFailure("{\"code\":45001125,\"message\":\"demo text audit failed\"}", "fake-key").Contains("文本未通过")) throw new Exception("Audit error mapping failed");
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
Check(ConfigurationChecks.ModelError("apikey-20261111-example") is not null, "API key resource ID rejected as model");
Check(ConfigurationChecks.ModelError("ep-example") is null && ConfigurationChecks.ModelError("doubao-vision-example") is null, "model and endpoint IDs accepted");
Check(ConfigurationChecks.ModelError("") is not null, "missing model rejected");
byte[] fixture;
using (var memory = new MemoryStream()) {
    using (var writer = new NAudio.Wave.WaveFileWriter(new KeepOpenStream(memory), new NAudio.Wave.WaveFormat(24000, 16, 1))) writer.Write(new byte[4800], 0, 4800);
    fixture = memory.ToArray();
}
Check(AudioGenerationClient.ToPcm(fixture).Length == 4800, "WAV header decoded rather than played as PCM");
var audioHandler = new FakeAudioHttp(fixture);
using (var audioHttp = new HttpClient(audioHandler)) {
    var api = new AudioGenerationClient(audioHttp);
    var wav = await api.Generate("逐字朗读。", "voice-2", "fake-audio-key", default);
    Check(wav.SequenceEqual(fixture), "audio generation base64 response decoded");
    using var body = JsonDocument.Parse(audioHandler.Body!);
    Check(audioHandler.Key == "fake-audio-key" && audioHandler.Url!.EndsWith("/api/v3/tts/create") && body.RootElement.GetProperty("model").GetString() == "seed-audio-1.0", "audio endpoint model and authentication");
    Check(body.RootElement.GetProperty("references")[0].GetProperty("speaker").GetString() == "voice-2" && body.RootElement.GetProperty("text_prompt").GetString()!.Contains("@音频1"), "speaker reference included in prompt");
    await api.Generate("Hello world", "voice-2", "fake-audio-key", default, new SpeechTuning(1.25,1.1,.5));
    using var tuned=JsonDocument.Parse(audioHandler.Body!);
    Check(tuned.RootElement.GetProperty("audio_config").GetProperty("speech_rate").GetInt32()==25 && tuned.RootElement.GetProperty("audio_config").GetProperty("enable_subtitle").GetBoolean(), "native reader cloud tuning and subtitles requested");
    Check(api.LastMarks.Count==2 && api.LastMarks[1].Start==6 && api.LastMarks[1].Seconds==1, "cloud word timestamps aligned to original text");
    audioHandler.Fail = true;
    try { await api.Generate("test", "v", "fake-audio-key", default); throw new Exception("business error accepted"); }
    catch (InvalidOperationException ex) { Check(!ex.Message.Contains("fake-audio-key"), "audio business error redacts key"); }
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await api.Generate("test", "v", "fake-audio-key", cancelled.Token); throw new Exception("cancel ignored"); }
    catch (OperationCanceledException) { Check(true, "audio generation cancellation"); }
}
var text = string.Concat(Enumerable.Repeat("你好！保留标点、数字123和emoji😀。\n", 40));
var chunks = TextChunks.Split(text).ToArray();
Check(string.Concat(chunks) == text && chunks.All(x => x.Length <= 260), "chunk boundaries preserve all text");
var emoji = new string('字', 259) + "😀尾";
Check(string.Concat(TextChunks.Split(emoji)) == emoji && TextChunks.Split(emoji).All(x => !char.IsHighSurrogate(x[^1])), "surrogate pairs remain intact");
Check(VoiceChoice.Parse("A|one\nB|two\nC|one").Count == 2, "voice IDs deduplicated");
using var session = JsonDocument.Parse(JsonSerializer.Serialize(DuplexClient.CreateSession("voice")));
Check(session.RootElement.GetProperty("session").GetProperty("audio").GetProperty("output").GetProperty("format").GetProperty("type").GetString() == "pcm_s16le", "16-bit audio format explicit");
var config = new Settings { ArkKey = "fake-ocr", SpeechKey = "fake-speech", Model = "test-vision" };
var handler = new FakeHttp();
using var http = new HttpClient(handler);
var ocr = await new OcrClient(http).Extract(new byte[] { 1, 2, 3 }, config, default);
Check(ocr == "测试原文。", "OCR response text parsed");
using var request = JsonDocument.Parse(handler.Body!);
Check(handler.Auth == "Bearer fake-ocr" && request.RootElement.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("detail").GetString() == "high", "OCR auth and high detail");
handler.Truncated = true;
try { await new OcrClient(http).Extract(new byte[] { 1 }, config, default); throw new Exception("truncation accepted"); }
catch (InvalidOperationException) { Check(true, "truncated OCR rejected"); }
handler.Truncated = false; handler.Fail = true;
try { await new OcrClient(http).Extract(new byte[] { 1 }, config, default); throw new Exception("401 accepted"); }
catch (InvalidOperationException ex) { Check(ex.Message.Contains("401"), "OCR auth failure visible"); }
handler.Fail = false;
var invalid = config.Copy(); invalid.Model = "apikey-example"; handler.Body = null;
try { await new OcrClient(http).Extract(new byte[] { 1 }, invalid, default); throw new Exception("invalid model accepted"); }
catch (InvalidOperationException) { Check(handler.Body is null, "invalid model blocked before network request"); }
var modelIds = await new OcrClient(http).ListModels("fake-ocr", default);
Check(modelIds.SequenceEqual(new[] { "doubao-test-vision" }) && handler.Auth == "Bearer fake-ocr", "model listing uses Ark bearer auth");

foreach (var status in new[] { 401, 403, 429 })
{
    var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); int port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
    using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var server = Task.Run(async () => { var context = await listener.GetContextAsync().WaitAsync(timeout.Token); context.Response.StatusCode = status; context.Response.Close(); });
    try {
        await new DuplexClient().Speak("test", config, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, timeout.Token, new Uri($"ws://127.0.0.1:{port}/"));
        throw new Exception("handshake failure accepted");
    } catch (InvalidOperationException ex) { Check(ex.Message == ConfigurationChecks.SpeechHandshakeError(status), $"real HTTP {status} handshake mapped to actionable error"); }
    await server;
}

async Task SocketTest(bool fail, bool cancel)
{
    var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); int port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
    using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
    var expected = new byte[] { 0, 0, 5, 0, 20, 0, 30, 0 };
    var received = new List<byte>(); var events = new List<string>();
    async Task Send(WebSocket ws, object item, bool split = false)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(item);
        if (split) {
            await ws.SendAsync(new ArraySegment<byte>(data, 0, 5), WebSocketMessageType.Text, false, timeout.Token);
            await ws.SendAsync(new ArraySegment<byte>(data, 5, data.Length - 5), WebSocketMessageType.Text, true, timeout.Token);
        } else await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, timeout.Token);
    }
    var server = Task.Run(async () => {
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        if (context.Request.Headers["X-Api-Key"] != "fake-speech") throw new Exception("missing speech auth");
        using var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
        while (true)
        {
            var data = new byte[65536]; var result = await ws.ReceiveAsync(new ArraySegment<byte>(data), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            using var doc = JsonDocument.Parse(data.AsMemory(0, result.Count));
            var type = doc.RootElement.GetProperty("type").GetString()!; events.Add(type);
            if (type == "session.create") await Send(ws, new { type = "session.created", session = new { id = "local" } });
            if (type == "speech_text_buffer.commit")
            {
                if (cancel) { cts.Cancel(); break; }
                if (fail) await Send(ws, new { type = "error", error = new { code = "401", message = "invalid test key" } });
                else {
                    await Send(ws, new { type = "response.output_audio.delta", delta = Convert.ToBase64String(expected) }, true);
                    await Send(ws, new { type = "response.output_audio.done" });
                }
            }
            if (type == "session.close") { await Send(ws, new { type = "session.closed" }); break; }
        }
    });
    try
    {
        await new DuplexClient().Speak("测试。", config, (b, ct) => { received.AddRange(b); return Task.CompletedTask; }, _ => Task.CompletedTask, cts.Token, new Uri($"ws://127.0.0.1:{port}/"));
        if (fail || cancel) throw new Exception("expected failure");
        Check(received.SequenceEqual(expected), "fragmented WebSocket audio decoded losslessly");
        Check(events.SequenceEqual(new[] { "session.create", "input_audio_mute.commit", "speech_text_buffer.commit", "session.close" }), "session lifecycle and mute sequence");
    }
    catch (InvalidOperationException ex) when (fail) { Check(ex.Message.Contains("401"), "speech server error surfaced"); }
    catch (OperationCanceledException) when (cancel) { Check(true, "in-flight speech cancellation"); }
    await server; cts.Dispose();
}
await SocketTest(false, false);
await SocketTest(true, false);
await SocketTest(false, true);
using (var player = new Playback())
{
    player.Pause(); await player.Add(new byte[4800], default);
    Check(player.Paused && player.HasAudio, "pause before first audio retains data");
    using var cts = new CancellationTokenSource(120);
    try { await player.Ready(cts.Token); throw new Exception("pause advanced"); }
    catch (OperationCanceledException) { Check(true, "paused prefetch remains cancellable"); }
    player.Resume(); await player.Drain(default);
    Check(!player.Paused, "resume drains buffered audio");
    player.Stop();
}
Console.WriteLine($"All {passed} checks passed.");
    }
}

sealed class FakeHttp : HttpMessageHandler
{
    public string? Body, Auth;
    public bool Truncated, Fail;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct); Auth = request.Headers.Authorization?.ToString();
        if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"doubao-test-vision\"}]}") };
        return new HttpResponseMessage(Fail ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = Truncated ? "length" : "stop", message = new { content = "测试原文。" } } } }), Encoding.UTF8, "application/json") };
    }
}

sealed class FakeAudioHttp(byte[] fixture) : HttpMessageHandler
{
    public string? Body, Key, Url; public bool Fail;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Body = await request.Content!.ReadAsStringAsync(ct); Key = request.Headers.GetValues("X-Api-Key").Single(); Url = request.RequestUri!.ToString();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fail
            ? "{\"code\":45000001,\"message\":\"bad fake-audio-key\"}"
            : JsonSerializer.Serialize(new { code = 0, audio = Convert.ToBase64String(fixture), subtitle = new { sentences = new[] { new { words = new[] { new { text="Hello",start_time=0 },new { text="world",start_time=1000 } } } } } })) };
    }
}

sealed class KeepOpenStream(Stream inner) : NAudio.Utils.IgnoreDisposeStream(inner);

sealed class DictionaryFixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json=request.RequestUri!.Host=="api.dictionaryapi.dev"?"[{\"phonetic\":\"/hello/\"}]":"{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"你好\"}}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
    }
}










