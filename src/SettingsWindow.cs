using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Net.Http;
using System.Diagnostics;

namespace LightReader;

public sealed class SettingsWindow : Window
{
    public FrameworkElement Page {get;private set;}=null!;
    public Action? Saved {get;set;}
    public Settings Result { get; private set; }
    public SettingsWindow(Settings current,bool dark=false,bool reduced=false)
    {
        Result = current.Copy();
        Title = "轻读 · 服务设置"; Width = 600; Height = 780; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(245, 246, 248)); FontFamily = new FontFamily("Microsoft YaHei UI");
        var stack = new StackPanel { Margin = new Thickness(24) };
        GlassWindow.Decorate(this,new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },dark,reduced);
        Page=(FrameworkElement)((DockPanel)((Border)Content).Child).Children[1];
        publicStack=stack;
        void Label(string text) => stack.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 12, 0, 6), TextWrapping = TextWrapping.Wrap });
        Label("图片文字识别 · 默认免费本机处理");
        var ocrProvider=new ComboBox{ItemsSource=new[]{"Windows 本机 OCR（免费离线）","豆包 OCR（云端付费）"},SelectedIndex=current.OcrProvider=="Cloud"?1:0,Padding=new Thickness(6)};stack.Children.Add(ocrProvider);
        Label("本机识别语言（自动优先中文，兼顾英文；仅显示已安装语言）");
        var ocrLanguages=new List<OcrLanguage>{new("自动选择","auto")};
        try{ocrLanguages.AddRange(LocalOcr.Languages());}catch{ }
        var ocrLanguage=new ComboBox{ItemsSource=ocrLanguages,SelectedItem=ocrLanguages.FirstOrDefault(x=>x.Tag==current.OcrLanguage)??ocrLanguages[0],Padding=new Thickness(6)};stack.Children.Add(ocrLanguage);
        Label(ocrLanguages.Count>1?"本机 OCR 无需密钥，图片不会上传。复杂表格、手写或低清文字请校对。":"未检测到本机 OCR 语言，请安装 Windows 相应语言的文字识别组件。");
        var languageSettings=new Button{Content="打开 Windows 语言设置",Padding=new Thickness(7),HorizontalAlignment=HorizontalAlignment.Left};stack.Children.Add(languageSettings);
        languageSettings.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo("ms-settings:regionlanguage"){UseShellExecute=true});}catch{MessageBox.Show(this,"请手动打开 Windows 设置 → 时间和语言 → 语言和区域。","轻读");}};
        Label("Edge 自然语音 · 免费在线，无需密钥");
        Label("使用开源 edge-tts 客户端调用微软在线神经音色，正文会发送至微软。Python 3.10+ 与 edge-tts 7.3.1+ 为可选依赖；本机 Windows 引擎不需要它们。");
        var edgePython=new TextBox{Text=current.EdgePython,Tag="Python 路径（留空自动检测）",ToolTip="可填写 python.exe 完整路径，不要填写命令行参数"};stack.Children.Add(edgePython);
        var edgeStatus=new TextBlock{Text="缺依赖时可运行 tools/Install-EdgeTts.ps1 安装到独立环境。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,5)};stack.Children.Add(edgeStatus);
        var checkEdge=new Button{Content="检测自然语音环境",HorizontalAlignment=HorizontalAlignment.Left};stack.Children.Add(checkEdge);
        var edgeLifetime=new CancellationTokenSource();Closed+=(_,_)=>edgeLifetime.Cancel();
        checkEdge.Click+=async(_,_)=>{checkEdge.IsEnabled=false;try{edgeStatus.Text=await new EdgeTtsClient().CheckEnvironment(edgePython.Text,edgeLifetime.Token);}catch(OperationCanceledException)when(edgeLifetime.IsCancellationRequested){}catch(Exception ex){edgeStatus.Text=ex.Message;}finally{checkEdge.IsEnabled=true;}};
        Label("火山方舟 · 截图识别 API Key");
        var ark = new PasswordBox { Password = current.ArkKey, Padding = new Thickness(8) }; stack.Children.Add(ark);
        Label("视觉模型 ID / ep- 推理接入点 ID（不是 apikey- 资源 ID）");
        var model = new ComboBox { IsEditable = true, Text = current.Model, Padding = new Thickness(8) }; stack.Children.Add(model);
        var loadModels = new Button { Content = "读取方舟模型列表", Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) }; stack.Children.Add(loadModels);
        Label("列表包含账户可见模型；请选择支持图片输入的豆包模型，是否可调用仍取决于开通权限。");
        Label("豆包语音 · Seed-Audio 1.0 音频生成 API Key");
        var speech = new PasswordBox { Password = AudioKeyStore.Load(), Padding = new Thickness(8) }; stack.Children.Add(speech);
        Label("对应 /api/v3/tts/create 接口。生成完成后播放，使用语音合成 2.0 音色。CMD 输入的密钥会自动读取。");
        var guide = new Button { Content = "打开音频生成官方接入文档", Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left }; stack.Children.Add(guide);
        guide.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://docs.volcengine.com/docs/doubao-voice/audio-generation-http?lang=zh") { UseShellExecute = true }); } catch { MessageBox.Show(this, "无法打开浏览器，请参阅随程序附带的 README。", "轻读"); } };
        Label("音色列表 · 每行：显示名称|音色ID");
        var voices = new TextBox { Text = current.AudioVoices, AcceptsReturn = true, Height = 95, Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; stack.Children.Add(voices);
        Label("仅添加语音合成 2.0 支持且你的账户有权限的音色。\n密钥、阅读记录和截图在本机加密保存，可在历史中管理。");
        var status = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) }; stack.Children.Add(status);
        if (current.OcrProvider=="Cloud"&&!string.IsNullOrWhiteSpace(current.Model)) status.Text = ConfigurationChecks.ModelError(current.Model) ?? "";
        var lifetime = new CancellationTokenSource();
        Closed += (_, _) => { lifetime.Cancel(); };
        loadModels.Click += async (_, _) => {
            loadModels.IsEnabled = false; status.Text = "正在读取模型列表…";
            try {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var ids = await new OcrClient(client).ListModels(ark.Password, lifetime.Token);
                var previous = model.Text; model.ItemsSource = ids; model.Text = previous;
                status.Text = ids.Length == 0 ? "未返回可见模型，请在控制台开通支持图片的模型。" : $"已读取 {ids.Length} 个模型，请展开列表选择视觉模型；不会自动替换你的配置。";
            } catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "读取超时，请检查网络或手动填写模型 ID。"; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { loadModels.IsEnabled = true; }
        };
        var save = new Button { Style=(Style)FindResource("PrimaryButton"), Content = "保存设置", Padding = new Thickness(12, 9, 12, 9), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true }; stack.Children.Add(save);
        save.Click += (_, _) => {
            if (ocrProvider.SelectedIndex==1 && !string.IsNullOrWhiteSpace(model.Text) && ConfigurationChecks.ModelError(model.Text.Trim()) is string modelError)
            { status.Text = modelError; return; }
            var choices = VoiceChoice.Parse(voices.Text);
            var rows = voices.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(x => !string.IsNullOrWhiteSpace(x));
            if (choices.Count == 0 || choices.Count != rows) { status.Text = "请检查音色格式，使用英文 | 分隔，ID 不可重复。"; return; }
            Result.OcrProvider=ocrProvider.SelectedIndex==1?"Cloud":"Local";Result.OcrLanguage=(ocrLanguage.SelectedItem as OcrLanguage)?.Tag??"auto";
            Result.EdgePython=edgePython.Text.Trim();
            Result.ArkKey = ark.Password.Trim();  Result.Model = model.Text.Trim(); Result.AudioVoices = voices.Text.Trim();
            if (!choices.Any(x => x.Id == Result.AudioVoice)) Result.AudioVoice = choices[0].Id;
            try { AudioKeyStore.Save(speech.Password); Result.Save(); if(Saved is not null)Saved();else DialogResult = true; }
            catch { status.Text = "保存失败，请检查本机用户目录写入权限。"; }
        };
    }
    readonly StackPanel publicStack;
    public void AddOptions(UIElement options){publicStack.Children.Insert(0,options);publicStack.Children.Insert(0,new TextBlock{Text="设置",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)});}
}


