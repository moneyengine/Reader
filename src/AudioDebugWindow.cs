using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.Wave;

namespace LightReader;

public sealed class AudioDebugWindow : Window
{
    readonly JsonSerializerOptions pretty = new() { WriteIndented = true };
    readonly TextBox prompt = Edit("请使用@音频1的音色，自然地朗读：你好，欢迎使用轻读。", 110);
    readonly TextBox json = Edit("", 280), response = Edit("", 330);
    readonly TextBox model = Edit("seed-audio-1.0"), requestId = Edit(Guid.NewGuid().ToString());
    readonly ComboBox auth = Choice("API Key", "旧版 App ID + Access Key");
    readonly PasswordBox apiKey = new() { Padding = new Thickness(6) }, accessKey = new() { Padding = new Thickness(6) };
    readonly TextBox appId = Edit(""), rate = Edit("24000"), speed = Edit("0"), volume = Edit("0"), pitch = Edit("0");
    readonly ComboBox format = Choice("wav", "mp3", "pcm", "ogg_opus");
    readonly CheckBox subtitle = new() { Content = "返回字级字幕 enable_subtitle" }, visibleMark = new() { Content = "显式水印 aigc_watermark" }, hiddenMark = new() { Content = "隐式水印 enable" };
    readonly TextBox producer = Edit(""), produceId = Edit(""), propagator = Edit(""), propagateId = Edit("");
    readonly StackPanel references = new();
    readonly List<ReferenceRow> rows = new();
    readonly TextBlock status = new() { Text = "就绪 · 调试请求原样发送，不添加指令或自动分段", TextWrapping = TextWrapping.Wrap };
    readonly Button send = new() { Content = "发送 JSON 请求", Padding = new Thickness(12, 7, 12, 7) };
    readonly Button cancel = new() { Content = "取消请求", IsEnabled = false, Padding = new Thickness(12, 7, 12, 7) };
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    CancellationTokenSource? pending;
    byte[]? audio;
    string audioFormat = "wav", subtitleJson = "";
    Playback? player; CancellationTokenSource? playbackCancel;
    public AudioDebugWindow(bool dark=false,bool reduced=false)
    {
        Title = "轻读 · Seed-Audio 1.0 API 调试"; Width = 1120; Height = 850; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        try { apiKey.Password = AudioKeyStore.Load(); } catch { }
        var root = new DockPanel { Margin = new Thickness(18) }; GlassWindow.Decorate(this,root,dark,reduced);send.Style=(Style)FindResource("PrimaryButton");
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var actions = new WrapPanel(); bottom.Children.Add(actions); bottom.Children.Add(status);
        void Action(string name, Action work) { var b = new Button { Content = name, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(3) }; b.Click += (_, _) => { try { work(); } catch (Exception ex) { status.Text = ex.Message; } }; actions.Children.Add(b); }
        actions.Children.Add(send); actions.Children.Add(cancel);
        Action("播放 / 继续", Play); Action("暂停", () => player?.Pause()); Action("停止", () => { playbackCancel?.Cancel(); player?.Stop(); player?.Dispose(); player = null; });
        Action("导出音频", SaveAudio); Action("导出字幕 JSON", () => SaveText(subtitleJson, "字幕.json"));
        Action("复制响应", () => Clipboard.SetText(response.Text)); Action("复制请求 JSON", () => Clipboard.SetText(json.Text));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(grid);
        var tabs = new TabControl { Margin = new Thickness(0, 0, 10, 10) }; grid.Children.Add(tabs);
        var form = new StackPanel { Margin = new Thickness(10) };
        tabs.Items.Add(new TabItem { Header = "参数表单", Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "实际请求 JSON（可编辑）", Content = json });
        void Field(string name, UIElement input) { form.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap }); form.Children.Add(input); }
        Field("鉴权方式", auth); Field("X-Api-Key（仅在请求头发送，不进入 JSON 或导出）", apiKey);
        Field("旧版 X-Api-App-Id", appId); Field("旧版 X-Api-Access-Key", accessKey);
        Field("X-Api-Request-Id（每次发送后自动更新；可手动填写）", requestId);
        Field("model", model); Field("text_prompt（原样发送，最多 3000 字符；可描述语气、音效及时间轴）", prompt);
        Field("参考资源：顺序对应 @音频1、@音频2…；最多 3 音频或 1 图片，不可混用", references);
        var add = new Button { Content = "添加参考资源", Margin = new Thickness(0, 5, 0, 0) }; add.Click += (_, _) => AddReference(); form.Children.Add(add);
        Field("format", format); Field("sample_rate：wav/pcm 8000,16000,24000,32000,40000,44100,48000；mp3 无 40000；opus 仅 48000", rate);
        format.SelectionChanged += (_, _) => { if ((string)format.SelectedItem == "ogg_opus") rate.Text = "48000"; else if (rate.Text == "48000") rate.Text = "24000"; };
        Field("speech_rate [-50,100]", speed); Field("loudness_rate [-50,100]", volume); Field("pitch_rate [-12,12]", pitch);
        Field("字幕", subtitle); Field("watermark", visibleMark); form.Children.Add(hiddenMark);
        Field("content_producer", producer); Field("produce_id", produceId); Field("content_propagator", propagator); Field("propagate_id", propagateId);
        var build = new Button { Content = "将表单写入请求 JSON", Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(8) };
        build.Click += (_, _) => { try { json.Text = Build().ToJsonString(pretty); tabs.SelectedIndex = 1; status.Text = "JSON 已更新；发送按钮将发送此 JSON。"; } catch (Exception ex) { status.Text = ex.Message; } }; form.Children.Add(build);
        var output = new StackPanel(); Grid.SetColumn(output, 1); grid.Children.Add(output);
        output.Children.Add(new TextBlock { Text = "响应 / HTTP 状态 / Logid / 耗时 / 时长 / 字幕", Margin = new Thickness(0, 0, 0, 8) });
        response.IsReadOnly = true; response.Height = 560;
        output.Children.Add(new ScrollViewer { Content = response, MaxHeight = 670, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var help = new TextBlock { Text = "响应面板省略 audio 的 Base64 正文，保留其他返回字段。音频只在内存中，点击导出才保存。WAV/MP3 支持内置播放；PCM/Opus 可导出。远端参考 URL 的内容、时长和可访问性由服务端校验。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; output.Children.Add(help);
        AddReference("speaker", "zh_female_vv_uranus_bigtts"); json.Text = Build().ToJsonString(pretty);
        send.Click += async (_, _) => await Send(); cancel.Click += (_, _) => pending?.Cancel();
        Closed += (_, _) => { pending?.Cancel(); playbackCancel?.Cancel(); player?.Dispose(); http.Dispose(); };
    }
    static TextBox Edit(string value, double height = 32) => new() { Text = value, Height = height, Padding = new Thickness(6), AcceptsReturn = height > 40, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    static ComboBox Choice(params string[] options) => new() { ItemsSource = options, SelectedIndex = 0, Padding = new Thickness(6) };
    sealed record ReferenceRow(StackPanel Panel, ComboBox Kind, TextBox Value);
    void AddReference(string kind = "speaker", string value = "")
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var types = Choice("speaker", "audio_url", "audio_data", "image_url", "image_data"); types.SelectedItem = kind;
        var input = Edit(value, 58); panel.Children.Add(types); panel.Children.Add(input);
        var search = Edit(""); search.ToolTip = "搜索音色名称、ID 或分类";
        var picker = new ComboBox { Margin = new Thickness(0, 4, 0, 4) };
        VoiceCatalog.Configure(picker);
        var options = VoiceCatalog.Choices(Settings.Load().AudioVoices);
        picker.ItemsSource = options;
        picker.SelectedItem = options.FirstOrDefault(x => x.Id == value);
        panel.Children.Insert(1, search); panel.Children.Insert(2, picker);
        search.TextChanged += (_, _) => {
            var query = search.Text.Trim();
            picker.ItemsSource = options.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is VoiceChoice choice && choice.Compatible) input.Text = choice.Id; };
        void TogglePicker() { search.Visibility = picker.Visibility = (string)types.SelectedItem == "speaker" ? Visibility.Visible : Visibility.Collapsed; }
        types.SelectionChanged += (_, _) => TogglePicker(); TogglePicker();
        var tools = new WrapPanel(); panel.Children.Add(tools);
        var browse = new Button { Content = "选择本地文件 → Base64", Padding = new Thickness(5) }; tools.Children.Add(browse);
        var remove = new Button { Content = "删除", Padding = new Thickness(5) }; tools.Children.Add(remove);
        var row = new ReferenceRow(panel, types, input); rows.Add(row); references.Children.Add(panel);
        remove.Click += (_, _) => { rows.Remove(row); references.Children.Remove(panel); };
        browse.Click += (_, _) => {
            try {
                var type = (string)types.SelectedItem;
                if (type is not ("audio_data" or "image_data")) throw new InvalidOperationException("先选择 audio_data 或 image_data。");
                var dialog = new OpenFileDialog { Filter = type == "image_data" ? "图片|*.png;*.jpg;*.jpeg;*.webp" : "音频|*.wav;*.mp3;*.pcm;*.ogg;*.opus" };
                if (dialog.ShowDialog(this) != true) return;
                var file = new FileInfo(dialog.FileName); if (file.Length > 10 * 1024 * 1024) throw new InvalidOperationException("参考文件不能超过 10 MB。");
                if (type == "audio_data" && file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) { using var r = new WaveFileReader(file.FullName); if (r.TotalTime.TotalSeconds > 30) throw new InvalidOperationException("参考音频不能超过 30 秒。"); }
                if (type == "audio_data" && file.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) { using var r = new Mp3FileReader(file.FullName); if (r.TotalTime.TotalSeconds > 30) throw new InvalidOperationException("参考音频不能超过 30 秒。"); }
                input.Text = Convert.ToBase64String(File.ReadAllBytes(file.FullName));
                status.Text = "文件已载入内存；只有发送请求时才上传。";
            } catch (Exception ex) { status.Text = ex.Message; }
        };
    }
    int Number(TextBox input, int min, int max) => int.TryParse(input.Text, out var n) && n >= min && n <= max ? n : throw new InvalidOperationException($"参数需为 {min} 至 {max} 的整数。");
    JsonObject Build()
    {
        var refs = new JsonArray();
        foreach (var row in rows) { if (!string.IsNullOrWhiteSpace(row.Value.Text)) refs.Add(new JsonObject { [(string)row.Kind.SelectedItem] = row.Value.Text.Trim() }); }
        var body = new JsonObject {
            ["model"] = model.Text, ["text_prompt"] = prompt.Text,
            ["audio_config"] = new JsonObject { ["format"] = (string)format.SelectedItem, ["sample_rate"] = Number(rate, 8000, 48000), ["speech_rate"] = Number(speed, -50, 100), ["loudness_rate"] = Number(volume, -50, 100), ["pitch_rate"] = Number(pitch, -12, 12), ["enable_subtitle"] = subtitle.IsChecked == true },
            ["watermark"] = new JsonObject { ["aigc_watermark"] = visibleMark.IsChecked == true, ["aigc_metadata"] = new JsonObject { ["enable"] = hiddenMark.IsChecked == true, ["content_producer"] = producer.Text, ["produce_id"] = produceId.Text, ["content_propagator"] = propagator.Text, ["propagate_id"] = propagateId.Text } }
        };
        if (refs.Count > 0) body["references"] = refs;
        AudioDebugValidation.Validate(body);
        return body;
    }
    async Task Send()
    {
        if (pending is not null) return;
        try {
            var body = JsonNode.Parse(json.Text) as JsonObject ?? throw new InvalidOperationException("请求 JSON 必须是对象。"); AudioDebugValidation.Validate(body);
            var currentFormat = body["audio_config"]?["format"]?.GetValue<string>() ?? "wav";
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://openspeech.bytedance.com/api/v3/tts/create");
            if (auth.SelectedIndex == 0) { if (string.IsNullOrWhiteSpace(apiKey.Password)) throw new InvalidOperationException("请填写 API Key。"); request.Headers.Add("X-Api-Key", apiKey.Password.Trim()); }
            else { if (string.IsNullOrWhiteSpace(appId.Text) || string.IsNullOrWhiteSpace(accessKey.Password)) throw new InvalidOperationException("旧版鉴权需要 App ID 和 Access Key。"); request.Headers.Add("X-Api-App-Id", appId.Text.Trim()); request.Headers.Add("X-Api-Access-Key", accessKey.Password.Trim()); }
            var id = string.IsNullOrWhiteSpace(requestId.Text) ? Guid.NewGuid().ToString() : requestId.Text.Trim(); request.Headers.Add("X-Api-Request-Id", id);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            pending = new CancellationTokenSource(); send.IsEnabled = false; cancel.IsEnabled = true; audio = null; subtitleJson = ""; playbackCancel?.Cancel(); player?.Dispose(); player = null;
            response.Text = ""; status.Text = "正在生成（非流式），可取消…"; var watch = Stopwatch.StartNew();
            using var result = await http.SendAsync(request, pending.Token);
            var raw = await result.Content.ReadAsStringAsync(pending.Token);
            string Safe(string text) { foreach (var secret in new[] { apiKey.Password, accessKey.Password }) if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[已隐藏]"); return text; }
            var heading = $"Request ID: {id}\nHTTP: {(int)result.StatusCode}\nLogid: {(result.Headers.TryGetValues("X-Tt-Logid", out var values) ? string.Join(", ", values) : "(未返回)")}\n耗时: {watch.Elapsed.TotalSeconds:F2}s\n";
            try {
                var data = JsonNode.Parse(raw) as JsonObject ?? throw new JsonException();
                if (result.IsSuccessStatusCode && (data["code"] is null || data["code"]!.GetValue<int>() is 0 or 20000000) && data["audio"] is JsonValue value) {
                    audio = Convert.FromBase64String(value.GetValue<string>()); audioFormat = currentFormat;
                    data["audio"] = $"[Base64 已省略，解码后 {audio.Length} bytes]";
                } else if (data["audio"] is not null) data["audio"] = "[未成功，不载入音频]";
                subtitleJson = data["subtitle"]?.ToJsonString(pretty) ?? "";
                response.Text = heading + Safe(data.ToJsonString(pretty));
                status.Text = audio is null ? "请求未返回可用音频，请查看右侧错误。" : $"生成完成 · {audio.Length} bytes · 可播放或导出。";
            } catch (JsonException) { response.Text = heading + Safe(raw); status.Text = "返回内容不是 JSON，请查看响应。"; }
        } catch (OperationCanceledException) { status.Text = "请求已取消或超时；服务端已开始的生成可能仍计费。"; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { pending?.Dispose(); pending = null; send.IsEnabled = true; cancel.IsEnabled = false; requestId.Text = Guid.NewGuid().ToString(); }
    }
    async void Play()
    {
        try {
            if (player?.Paused == true) { player.Resume(); return; }
            if (audio is null) throw new InvalidOperationException("请先成功生成音频。");
            if (audioFormat is not ("wav" or "mp3")) throw new InvalidOperationException("PCM/Opus 当前支持导出，请使用外部播放器试听。");
            if (player is not null) return;
            byte[] pcm;
            if (audioFormat == "wav") pcm = AudioGenerationClient.ToPcm(audio);
            else { using var input = new MemoryStream(audio); using var reader = new Mp3FileReader(input); using var wav = new MemoryStream(); WaveFileWriter.WriteWavFileToStream(new NAudio.Utils.IgnoreDisposeStream(wav), reader); pcm = AudioGenerationClient.ToPcm(wav.ToArray()); }
            var playing = new Playback(); player = playing; var playToken = new CancellationTokenSource(); playbackCancel = playToken;
            try { await playing.Add(pcm, playToken.Token); await playing.Drain(playToken.Token); }
            finally { playToken.Dispose(); if (playbackCancel == playToken) playbackCancel = null; if (player == playing) { player = null; playing.Dispose(); } }
        } catch (Exception ex) { status.Text = ex.Message; }
    }
    void SaveAudio()
    {
        if (audio is null) throw new InvalidOperationException("没有可导出的音频。");
        var dialog = new SaveFileDialog { FileName = "生成音频." + audioFormat, Filter = "音频文件|*." + audioFormat };
        if (dialog.ShowDialog(this) == true) File.WriteAllBytes(dialog.FileName, audio);
    }
    void SaveText(string text, string name)
    {
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("没有字幕数据，请先启用字幕并生成。");
        var dialog = new SaveFileDialog { FileName = name, Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, text);
    }
}

public static class AudioDebugValidation
{
    public static void Validate(JsonObject body)
    {
        if (body["model"]?.GetValue<string>() != "seed-audio-1.0") throw new InvalidOperationException("当前文档仅支持 seed-audio-1.0。");
        var prompt = body["text_prompt"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 3000) throw new InvalidOperationException("text_prompt 需为 1–3000 字符。");
        var config = body["audio_config"];
        var format = config?["format"]?.GetValue<string>() ?? "wav";
        var rate = config?["sample_rate"]?.GetValue<int>() ?? (format == "ogg_opus" ? 48000 : format == "mp3" ? 44100 : 40000);
        int[] rates = format switch { "wav" or "pcm" => [8000,16000,24000,32000,40000,44100,48000], "mp3" => [8000,16000,24000,32000,44100,48000], "ogg_opus" => [48000], _ => throw new InvalidOperationException("不支持该音频格式。") };
        if (!rates.Contains(rate)) throw new InvalidOperationException("当前格式不支持所选采样率。");
        foreach (var field in new[] { "speech_rate", "loudness_rate", "pitch_rate" }) {
            var n = config?[field]?.GetValue<int>() ?? 0; if (n < (field == "pitch_rate" ? -12 : -50) || n > (field == "pitch_rate" ? 12 : 100)) throw new InvalidOperationException(field + " 超出范围。");
        }
        var refs = body["references"] as JsonArray; if (refs is null) return;
        int images = 0, audios = 0;
        foreach (var item in refs) {
            var keys = new[] { "speaker", "audio_data", "audio_url", "image_data", "image_url" }.Where(k => item?[k] is JsonValue v && !string.IsNullOrWhiteSpace(v.GetValue<string>())).ToArray();
            if (keys.Length != 1) throw new InvalidOperationException("每个参考资源仅可填写一种来源。");
            var key = keys[0]; if (key.StartsWith("image")) images++; else audios++;
            var value = item![key]!.GetValue<string>();
            if (key.EndsWith("_data")) { var bytes = Convert.FromBase64String(value); if (bytes.Length > 10 * 1024 * 1024) throw new InvalidOperationException("单个参考资源超过 10 MB。"); }
            if (key.EndsWith("_url") && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidOperationException("参考 URL 必须为 HTTP/HTTPS。");
        }
        if (images > 1 || audios > 3 || (images > 0 && audios > 0)) throw new InvalidOperationException("最多 1 张图片或 3 个音频参考，图片不可与音频/音色混用。");
    }
}


