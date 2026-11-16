using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LightReader;

public partial class NativeReaderWindow : Window
{
    Settings settings=new();
    ReaderState state=new();
    readonly HttpClient cloud=new(){Timeout=TimeSpan.FromSeconds(300)};
    readonly HttpClient dictionaryHttp=new(){Timeout=TimeSpan.FromSeconds(8)};
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};
    readonly DispatcherTimer tuningTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    CancellationTokenSource? reading, dictionary;
    WindowsSpeech? speech;
    Playback? player;
    List<VoiceChoice> voices=new();
    readonly ObservableCollection<DictionaryWord> words=new();
    readonly Dictionary<string,DictionaryWord> dictionaryCache=new(StringComparer.OrdinalIgnoreCase);
    readonly List<(int Start,int Length,Run Run)> runs=new();
    bool loading=true,loadingVoices,paused,ocrBusy,compact;
    bool waitingForSpeech;
    bool pinned;
    readonly ContentControl pageHost=new(){Visibility=Visibility.Collapsed};
    readonly WrapPanel settingsActions=new();
    Grid readerPage=null!;
    SettingsWindow? embeddedSettings;
    readonly StackPanel bottomNavigation=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left};
    Button? settingsNav;
    void SelectNavigation(string page){
        foreach(var pair in new[]{(ReadNavButton,"reader"),(HistoryNavButton,"history"),(settingsNav,"settings")}){
            if(pair.Item1 is not {} button)continue;
            button.SetResourceReference(Control.ForegroundProperty,pair.Item2==page?"AccentBrush":"InkBrush");
            if(pair.Item2==page)button.SetResourceReference(Control.BackgroundProperty,"LensBrush");else button.Background=Brushes.Transparent;
        }
    }
    int wordHighlight=-1,wordHighlightEnd=-1;
    int waitingSeconds;
    int position,highlight=-1,highlightEnd=-1;
    long lastScroll;
    double normalHeight=800, normalWidth=1180, normalLeft, normalTop;
    bool originalRequested, tuningExpanded, settingRate;
    ComboBoxItem? customRateItem;
    double originalZoom;
    WindowState normalState;
    string spokenText="";
    SpeechTuning? activeTuning;
    byte[]? lastOcrImage;
    SpeechTuning Tuning=>new SpeechTuning(Rate.Value,Pitch.Value,Volume.Value).Clamp();
    bool Local=>Engine.SelectedIndex==0;
    bool Edge=>Engine.SelectedIndex==2;
    List<EdgeVoice>? onlineVoices;
    readonly string? storagePath;
    readonly HistoryImages historyImages;
    NativeHistoryWindow? historyWindow;
    string? historyNote;
    ReaderTray? tray;
    WindowState trayRestoreState=WindowState.Normal;
    public NativeReaderWindow() : this(null) { }
    public NativeReaderWindow(string? statePath)
    {
        storagePath=statePath;historyImages=new HistoryImages(statePath);
        InitializeComponent();
        SetupPages();
        ApplyStitchLayout();
        ((Panel)CaretButton.Parent).Children.Remove(CaretButton);
        PlaybackControls.Children.Insert(PlaybackControls.Children.IndexOf(ExpandButton),CaretButton);
        CaretButton.Height=43;
        Voice.SearchChanged+=q=>{if(VoiceSearch.Text!=q)VoiceSearch.Text=q;};
        try{settings=statePath is null?Settings.Load():new Settings();state=ReaderState.Load(statePath);}catch(Exception ex){Status.Text="配置读取失败："+ex.Message;}
        state.Tuning=(state.Tuning??new()).Clamp();
        Editor.Text=state.Text;Rate.Value=state.Tuning.Rate;Pitch.Value=state.Tuning.Pitch;Volume.Value=state.Tuning.Volume;
        Editor.FontSize=state.FontSize;Engine.SelectedIndex=settings.SpeechProvider switch {"Cloud"=>1,"Edge"=>2,_=>0};OcrEngineChoice.SelectedIndex=settings.OcrProvider=="Cloud"?1:0;
        GlassWindow.Attach(this,()=> (state.Dark,state.ReducedTransparency));
        SizeChanged+=(_,_)=>AdaptPanels();
        StateChanged+=(_,_)=>{WindowFrame.Margin=WindowState==WindowState.Maximized?new Thickness(7):new Thickness(0);if(WindowState!=WindowState.Minimized)trayRestoreState=WindowState;};
        RestoreHistoryImage();
        Words.ItemsSource=words;loading=false;ApplyTheme();UpdateTuningLabels();UpdateTextCount();PositionLabel.Text=$"0 / {Editor.Text.Length}";LoadVoices();SetBusy(false);
        if(statePath is null)tray=new ReaderTray(ShowFromTray,()=>Play(this,new()),()=>Stop(this,new()),Close,()=>new(reading is not null,paused,ocrBusy,!string.IsNullOrWhiteSpace(Editor.Text)));
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();SaveState();if(DictionaryPanel.Visibility==Visibility.Visible)RefreshDictionary(null,new());};
        tuningTimer.Tick+=async (_,_)=>{tuningTimer.Stop();if(reading is not null&&!ocrBusy&&!paused){var pos=position;CancelRead();await Begin(pos);}};
        Closed+=(_,_)=>{tray?.Dispose();saveTimer.Stop();tuningTimer.Stop();CancelRead();dictionary?.Cancel();SaveState();cloud.Dispose();dictionaryHttp.Dispose();};
    }
    void SaveState()
    {
        try{state.Text=Editor.Text;state.Tuning=Tuning;state.FontSize=Editor.FontSize;ArchiveDocument();state.Save(storagePath);historyImages.Clean(state.History.Select(x=>x.ImageId).Append(state.CurrentImageId));historyWindow?.Refresh(state.History);}
        catch(Exception ex){Status.Text="阅读数据保存失败："+ex.Message;}
    }
    void ArchiveDocument()
    {
        if(state.SuppressCurrentHistory)return;
        var item=state.Upsert(Editor.Text,state.CurrentImageId,state.CurrentSource,state.CurrentHistoryId,historyNote);
        state.CurrentHistoryId=item?.Id;
    }
    void NewDocument(string source="文本")
    {
        saveTimer.Stop();SaveState();state.CurrentHistoryId=null;state.CurrentImageId=null;state.CurrentSource=source;state.SuppressCurrentHistory=false;historyNote=null;lastOcrImage=null;ImagePreviewButton.Visibility=Visibility.Collapsed;OriginalImage.Source=null;originalRequested=false;AdaptPanels();UpdateSource();
    }
    void RestoreHistoryImage()
    {
        historyNote=state.History.FirstOrDefault(x=>x.Id==state.CurrentHistoryId)?.Note;
        try{lastOcrImage=historyImages.Load(state.CurrentImageId);}catch{lastOcrImage=null;}
        ImagePreviewButton.Visibility=lastOcrImage is null?Visibility.Collapsed:Visibility.Visible;
        UpdateOriginal(false);UpdateSource();
    }
    void SaveSettings(){if(storagePath is not null)return;try{settings.Save();}catch(Exception ex){Status.Text="服务设置保存失败："+ex.Message;}}
    void LoadVoices()
    {
        loadingVoices=true;
        try{
            voices=Local?WindowsSpeech.GetVoices():Edge?EdgeTtsClient.Choices(onlineVoices):VoiceCatalog.Choices(settings.AudioVoices);
            VoiceCatalog.Configure(Voice);
            if(Local)Voice.ToolTip="Windows / SAPI 已安装音色，免费离线。音色数量取决于本机语言组件。";
            if(Edge)Voice.ToolTip="Edge 在线神经音色 · 无需 API Key · 正文发送至微软合成；可搜索中文、地区、名称或 ID。";
            var id=Local?settings.WindowsVoice:Edge?settings.EdgeVoice:settings.AudioVoice;
            Voice.ItemsSource=voices;
            Voice.SetCatalogue(Local?"Windows":Edge?"Edge":"Cloud",voices,onlineVoices);
            Voice.SelectedItem=voices.FirstOrDefault(v=>v.Id==id)??voices.FirstOrDefault(v=>v.Compatible&&v.Name.Contains("zh-CN"))??voices.FirstOrDefault(v=>v.Compatible);
            if(voices.Count==0)Status.Text="没有可用的本机音色，请安装 Windows 语音组件。";
            else Status.Text=Local?$"Windows 本机 · {voices.Count} 个音色 · 免费离线":Edge?$"Edge 自然语音 · {voices.Count} 个音色 · 免费在线，正文将发送至微软":$"火山引擎 · 云端音频生成 · {voices.Count} 个音色";
            VoiceSearch.Text="";
        }catch(Exception ex){Status.Text=ex.Message;}
        finally{loadingVoices=false;CompactVoice.Text=(Voice.SelectedItem as VoiceChoice)?.Name??"未选择音色";}
    }
    void EngineChanged(object sender,SelectionChangedEventArgs e)
    {
        if(loading)return;CancelRead();settings.SpeechProvider=Local?"Windows":Edge?"Edge":"Cloud";SaveSettings();LoadVoices();
    }
    async void VoiceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(loading||loadingVoices||Voice.SelectedItem is not VoiceChoice voice)return;
        var restart=reading is not null&&!ocrBusy;var pos=position;if(restart)CancelRead();
        if(Local)settings.WindowsVoice=voice.Id;else if(Edge)settings.EdgeVoice=voice.Id;else settings.AudioVoice=voice.Id;SaveSettings();
        CompactVoice.Text=voice.Name;
        if(restart)await Begin(pos);
    }
    void FilterVoices(object sender,TextChangedEventArgs e)
    {
        if(loading||loadingVoices)return;
        Voice.Filter(VoiceSearch.Text);
    }

    async void ReloadVoices(object sender,RoutedEventArgs e)
    {
        CancelRead();if(!Edge){LoadVoices();return;}
        var cts=new CancellationTokenSource();reading=cts;Status.Text="正在刷新 Edge 在线音色…";
        try{var list=await new EdgeTtsClient().RefreshVoices(settings.EdgePython,cts.Token);if(reading==cts&&Edge){onlineVoices=list;LoadVoices();}}
        catch(OperationCanceledException)when(cts.IsCancellationRequested){}
        catch(Exception ex){if(reading==cts)Status.Text=ex.Message;}
        finally{if(reading==cts)reading=null;cts.Dispose();SetBusy(reading is not null);}
    }
    void EditorChanged(object sender,TextChangedEventArgs e)
    {
        if(TextCount is null)return;UpdateTextCount();
        if(loading)return;state.SuppressCurrentHistory=false;if(!ocrBusy)historyNote=null;position=0;Seek.Value=0;PositionLabel.Text=$"0 / {Editor.Text.Length}";
        UpdateSource();saveTimer.Stop();saveTimer.Start();
    }
    void UpdateTextCount()
    {
        TextCount.Text=$"{(ReadingView.Visibility==Visibility.Visible?"朗读正文":"文本输入")} · {Editor.Text.Length} 字";
        if(EmptyHint is not null)EmptyHint.Visibility=string.IsNullOrEmpty(Editor.Text)?Visibility.Visible:Visibility.Collapsed;
        if(PlayButton is not null&&!ocrBusy)PlayButton.IsEnabled=!string.IsNullOrWhiteSpace(Editor.Text);tray?.Refresh();
    }
    void UpdateTuningLabels(){RateLabel.Text=$"语速 · {Rate.Value:F2}×";PitchLabel.Text=$"音调 · {Pitch.Value:F2}";VolumeLabel.Text=$"音量 · {Volume.Value:P0}";
        if(RateChoice is not null){
            settingRate=true;
            if(customRateItem is not null){RateChoice.Items.Remove(customRateItem);customRateItem=null;}
            var preset=RateChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>double.TryParse(x.Tag?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&Math.Abs(value-Rate.Value)<.001);
            if(preset is null){customRateItem=new ComboBoxItem{Content=$"{Rate.Value:F2}×",Tag=Rate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)};RateChoice.Items.Add(customRateItem);preset=customRateItem;}
            RateChoice.SelectedItem=preset;RateChoice.ToolTip=$"当前语速 {Rate.Value:F2}×";settingRate=false;
        }
    }


    void RatePreset(object sender,SelectionChangedEventArgs e){if(!loading&&!settingRate&&sender is ComboBox {SelectedItem:ComboBoxItem item}&&double.TryParse(item.Tag?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var rate))Rate.Value=rate;}
    void TuningChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(loading||RateLabel is null)return;UpdateTuningLabels();saveTimer.Stop();saveTimer.Start();
        if(reading is not null&&!ocrBusy){tuningTimer.Stop();tuningTimer.Start();}
    }
    void SetBusy(bool busy)
    {
        PauseButton.IsEnabled=busy&&!ocrBusy&&!paused;StopButton.IsEnabled=busy;
        CaptureButton.IsEnabled=!busy;ImageOcrButton.IsEnabled=!busy;OcrEngineChoice.IsEnabled=!busy;Editor.IsReadOnly=busy;ImportButton.IsEnabled=!busy;
        PlayButton.Content=paused?"▶ 继续":busy&&waitingForSpeech?$"等待 {waitingSeconds}s":busy&&!ocrBusy?"Ⅱ 暂停":"▶ 播放";PlayButton.IsEnabled=!ocrBusy&&!string.IsNullOrWhiteSpace(Editor.Text);
        PlayerState.Text=ocrBusy?"正在识别":paused?"已暂停":busy?"正在朗读":"等待朗读";
        PasteButton.IsEnabled=!ocrBusy;tray?.Refresh();
        if(!busy)ApplySentenceFocus(false);
    }
    void CancelRead()
    {
        tuningTimer.Stop();
        if(ocrBusy){historyNote="识别已停止，可从原图重新识字";SaveState();}
        reading?.Cancel();reading=null;player?.Stop();player=null;speech=null;paused=false;ocrBusy=false;waitingForSpeech=false;waitingSeconds=0;SetBusy(false);
    }
    void RenderText()
    {
        spokenText=Editor.Text;highlight=-1;highlightEnd=-1;wordHighlight=-1;wordHighlightEnd=-1;runs.Clear();
        var doc=MarkdownReading.Render(spokenText,Editor.FontSize,(Brush)FindResource("InkBrush"),async index=>{if(ocrBusy)return;CancelRead();await Begin(index);},runs);
        doc.FontFamily=Editor.FontFamily;
        ReadingView.Document=doc;
        ReadingView.Visibility=Visibility.Visible;Editor.Visibility=Visibility.Collapsed;EditButton.Visibility=Visibility.Visible;UpdateTextCount();
    }
    void ReportPosition(int start,int length)
    {
        position=Math.Clamp(start,0,spokenText.Length);
        Seek.Value=spokenText.Length==0?0:(double)position/spokenText.Length;PositionLabel.Text=$"{position} / {spokenText.Length}";
        var sentence=SentenceFocus.Range(spokenText,position);
        int low=0,high=runs.Count-1,found=-1;
        while(low<=high){var mid=(low+high)/2;if(runs[mid].Start<=position){found=mid;low=mid+1;}else high=mid-1;}
        var end=found;
        var wordStart=found;var wordEnd=found;
        while(wordEnd+1<runs.Count&&runs[wordEnd+1].Start<position+Math.Max(1,length))wordEnd++;
        while(found>0&&runs[found-1].Start>=sentence.Start)found--;
        while(end+1<runs.Count&&runs[end+1].Start<sentence.End)end++;
        if(found==highlight&&end==highlightEnd&&wordStart==wordHighlight&&wordEnd==wordHighlightEnd)return;
        wordHighlight=wordStart;wordHighlightEnd=wordEnd;
        highlight=found;highlightEnd=end;if(found<0)return;
        ApplySentenceFocus(true);
        if(Environment.TickCount64-lastScroll>600){runs[found].Run.BringIntoView();lastScroll=Environment.TickCount64;}
    }
    void ApplySentenceFocus(bool active)
    {
        var ink=(Brush)FindResource("InkBrush");var faded=ink.Clone();faded.Opacity=.35;faded.Freeze();
        for(int i=0;i<runs.Count;i++){
            runs[i].Run.Background=Brushes.Transparent;
            runs[i].Run.Foreground=!active||i>=highlight&&i<=highlightEnd?ink:faded;
            if(active&&i>=wordHighlight&&i<=wordHighlightEnd){
                runs[i].Run.Background=new SolidColorBrush(Color.FromRgb(197,222,255));
                runs[i].Run.Foreground=Brushes.Black;
            }
        }
    }
    async void Play(object? sender,RoutedEventArgs e)
    {
        if(paused){if(Tuning!=activeTuning){var pos=position;CancelRead();await Begin(pos);return;}paused=false;speech?.Resume();player?.Resume();Status.Text="继续朗读";SetBusy(true);return;}
        if(reading is not null&&!ocrBusy){Pause(this,new());return;}
        if(reading is null)await Begin(position);
    }
    async Task Begin(int start)
    {
        if(ocrBusy)return;
        var text=Editor.Text;if(string.IsNullOrWhiteSpace(text)){Status.Text="请先输入文字。";return;}
        var speechText=SpeechText.Filter(text);
        if(Voice.SelectedItem is not VoiceChoice voice||!voice.Compatible){Status.Text="请选择当前接口支持的音色。";return;}
        start=Math.Clamp(start,0,Math.Max(0,text.Length-1));
        if(!SpeechText.HasWords(speechText[start..])){Status.Text="没有需要朗读的文字，已跳过网址、符号和表情。";return;}
        var local=Local;var edge=Edge;var tuning=Tuning;string key="";
        if(!local&&!edge){try{key=AudioKeyStore.Load();}catch(Exception ex){Status.Text=ex.Message;return;}if(string.IsNullOrWhiteSpace(key)){Status.Text="请先在服务设置中填写音频生成密钥。";return;}}
        CancelRead();RenderText();ReportPosition(start,1);state.SuppressCurrentHistory=false;SaveState();
        var cts=new CancellationTokenSource();reading=cts;paused=false;activeTuning=tuning;SetBusy(true);
        WindowsSpeech? localSpeech=null;Playback? cloudPlayer=null;
        var callback=new Progress<ReaderProgress>(p=>{if(reading==cts)ReportPosition(start+p.Start,p.Length);});
        try{
            if(local){
                localSpeech=new WindowsSpeech();speech=localSpeech;Status.Text="本机朗读 · 文本不会上传";
                await localSpeech.Speak(speechText[start..],voice.Id,cts.Token,tuning,callback);
            }else if(edge){
                var client=new EdgeTtsClient();
                var pieces=TextChunks.Split(speechText[start..],150,true).ToArray();var offset=start;
                using var generation=CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                Task<EdgeAudio>? prepared=null;
                try{
                    for(int i=0;i<pieces.Length;i++){
                        cts.Token.ThrowIfCancellationRequested();while(paused)await Task.Delay(30,cts.Token);
                        var chunk=pieces[i];if(!SpeechText.HasWords(chunk)){offset+=chunk.Length;continue;}
                        Status.Text=$"Edge 自然语音 · 正在生成第 {i+1} 段 · 正文发送至微软";
                        prepared??=client.Generate(chunk,voice.Id,tuning,settings.EdgePython,generation.Token);
                        ReaderDiagnostics.Write($"edge segment={i+1} sourceOffset={offset} length={chunk.Length} generationWaiting");
                        var waiting=System.Diagnostics.Stopwatch.StartNew();
                        waitingForSpeech=true;waitingSeconds=0;SetBusy(true);
                        while(!prepared.IsCompleted){
                            cts.Token.ThrowIfCancellationRequested();
                            if(reading==cts){waitingSeconds=(int)waiting.Elapsed.TotalSeconds;SetBusy(true);Status.Text=$"Edge 第 {i+1} 段 · 等待在线语音返回 {waitingSeconds} 秒（可停止）";}
                            await Task.WhenAny(prepared,Task.Delay(250,cts.Token));
                        }
                        var audio=await prepared;prepared=null;cts.Token.ThrowIfCancellationRequested();
                        var pcm=EdgeTtsClient.DecodeMp3(audio.Mp3);
                        ReaderDiagnostics.Write($"edge segment={i+1} generationReady pcmBytes={pcm.Length} marks={audio.Marks.Count}");
                        cloudPlayer=new Playback{Volume=(float)tuning.Volume};player=cloudPlayer;if(paused)cloudPlayer.Pause();
                    await cloudPlayer.Add(pcm,cts.Token);ReportPosition(offset,1);
                        waitingForSpeech=false;SetBusy(true);
                        // Prefetch at most one next segment while the current segment is playing.
                        if(i+1<pieces.Length&&!paused&&SpeechText.HasWords(pieces[i+1]))prepared=client.Generate(pieces[i+1],voice.Id,tuning,settings.EdgePython,generation.Token);
                        Status.Text=$"Edge 自然语音 · 第 {i+1} 段 · {(audio.Marks.Count>0?"文字同步朗读":"段落进度")}";
                        int mark=0;var drain=cloudPlayer.Drain(cts.Token);
                        while(!drain.IsCompleted){
                            while(!paused&&mark<audio.Marks.Count&&audio.Marks[mark].Seconds<=cloudPlayer.PositionSeconds){var cue=audio.Marks[mark++];if(reading==cts)ReportPosition(offset+cue.Start,cue.Length);}
                            await Task.Delay(30,cts.Token);
                        }
                        await drain;ReaderDiagnostics.Write($"edge segment={i+1} playbackDrained");cloudPlayer.Dispose();cloudPlayer=null;player=null;offset+=chunk.Length;
                    }
                }finally{generation.Cancel();if(prepared is not null){try{await prepared;}catch{ /* Observe a canceled/failed prefetch; original operation owns error reporting. */ }}}
            }else{
                var api=new AudioGenerationClient(cloud);int offset=start,segment=0;
                foreach(var chunk in TextChunks.Split(speechText[start..],260,true)){
                    cts.Token.ThrowIfCancellationRequested();while(paused)await Task.Delay(30,cts.Token);segment++;
                    if(!SpeechText.HasWords(chunk)){offset+=chunk.Length;continue;}
                    Status.Text=$"正在生成第 {segment} 段（非流式）…";
                    var wav=await api.Generate(chunk,voice.Id,key,cts.Token,tuning);
                    var pcm=AudioGenerationClient.ToPcm(wav);cts.Token.ThrowIfCancellationRequested();
                    cloudPlayer=new Playback();cloudPlayer.Volume=(float)tuning.Volume;player=cloudPlayer;if(paused)cloudPlayer.Pause();
                    await cloudPlayer.Add(pcm,cts.Token);
                    var marks=api.LastMarks;int mark=0;ReportPosition(offset,chunk.Length);
                    Status.Text=marks.Count>0?$"第 {segment} 段 · 字幕同步朗读":$"第 {segment} 段 · 服务未返回时间戳，显示段落进度";
                    var drain=cloudPlayer.Drain(cts.Token);
                    while(!drain.IsCompleted){
                        while(!paused&&mark<marks.Count&&marks[mark].Seconds<=cloudPlayer.PositionSeconds){
                            var cue=marks[mark++];if(reading==cts)ReportPosition(offset+cue.Start,cue.Length);
                        }
                        await Task.Delay(30,cts.Token);
                    }
                    await drain;cloudPlayer.Dispose();cloudPlayer=null;player=null;offset+=chunk.Length;
                }
            }
            if(reading==cts){ReportPosition(text.Length,0);Status.Text="朗读完成";position=0;}
        }catch(OperationCanceledException)when(cts.IsCancellationRequested){}
        catch(Exception ex){ReaderDiagnostics.Write($"reading failed type={ex.GetType().Name} position={position}");if(reading==cts){Status.Text=ex.Message;if(ex.Message.Contains("45001125"))MessageBox.Show(this,ex.Message,"文本审核未通过");}}
        finally{
            localSpeech?.Dispose();cloudPlayer?.Dispose();cts.Dispose();
            if(reading==cts){reading=null;speech=null;player=null;paused=false;waitingForSpeech=false;SetBusy(false);}
        }
    }
    void Pause(object sender,RoutedEventArgs e)
    {
        if(reading is null||ocrBusy)return;paused=true;speech?.Pause();player?.Pause();SetBusy(true);Status.Text="已暂停 · 点击继续";
    }
    void Stop(object? sender,RoutedEventArgs e){CancelRead();position=0;Seek.Value=0;PositionLabel.Text=$"0 / {Editor.Text.Length}";Status.Text="已停止 · 下次从头播放"; }
    async void Restart(object sender,RoutedEventArgs e){CancelRead();await Begin(0);}
    async void ReadCaret(object sender,RoutedEventArgs e){var start=Editor.Visibility==Visibility.Visible?Editor.CaretIndex:position;CancelRead();await Begin(start);}
    void Edit(object sender,RoutedEventArgs e){CancelRead();ReadingView.Visibility=Visibility.Collapsed;Editor.Visibility=Visibility.Visible;EditButton.Visibility=Visibility.Collapsed;Editor.CaretIndex=Math.Min(position,Editor.Text.Length);Editor.Focus();UpdateTextCount();}
    async void Paste(object sender,RoutedEventArgs e)
    {
        try{await PasteClipboard(Clipboard.GetDataObject());}
        catch(Exception ex){Status.Text="粘贴失败："+ex.Message;}
    }
    async Task PasteClipboard(IDataObject? data)
    {
        if(ocrBusy){Status.Text="正在识字，请等待或先停止当前识别。";return;}
        if(data is null){Status.Text="剪贴板为空，请先复制文字或图片。";return;}
        try{
            // Screenshots and browser copies may expose both text and image formats.
            // Prefer the actual image rather than a URL or descriptive clipboard text.
            if(ClipboardImage.HasImage(data)){
                var png=ClipboardImage.ReadPng(data);
                if(!OcrConfigured())return;
                CancelRead();await RecognizeImage(png);
                return;
            }
            var text=data.GetData(DataFormats.UnicodeText,true) as string??data.GetData(DataFormats.Text,true) as string;
            if(text is not null){Edit(this,new());NewDocument("粘贴文本");Editor.Text=text;SaveState();if(MarkdownReading.IsMarkdown(text))RenderText();return;}
            Status.Text="剪贴板中没有可用的文字或图片，请复制图片本身后重试。";
        }catch(Exception ex){Status.Text="粘贴失败："+ex.Message;}
    }
    void Clear(object sender,RoutedEventArgs e){var preview=ReadingView.Visibility==Visibility.Visible;Edit(sender,e);NewDocument();Editor.Clear();position=0;Seek.Value=0;SaveState();if(preview)RenderText();Status.Text="正文已清空";}
    void Sample(object sender,RoutedEventArgs e){Edit(sender,e);NewDocument("示例");Editor.Text="你好，这是轻读原生桌面版。\n\n点击播放后，文字会跟随朗读高亮。点击正文中的词语，可以从那里继续。\n\nReading makes ideas easier to understand. Take your time and enjoy the story.";}
    async void SeekReleased(object sender,MouseButtonEventArgs e){if(string.IsNullOrWhiteSpace(Editor.Text))return;int start=(int)Math.Round(Seek.Value*Editor.Text.Length);CancelRead();await Begin(start);}
    async void SeekKey(object sender,KeyEventArgs e){if(e.Key is Key.Left or Key.Right or Key.Home or Key.End){var start=(int)Math.Round(Seek.Value*Editor.Text.Length);CancelRead();await Begin(start);}}
    void Smaller(object sender,RoutedEventArgs e)=>SetFont(Editor.FontSize-1);
    void Larger(object sender,RoutedEventArgs e)=>SetFont(Editor.FontSize+1);
    void SetFont(double size){Editor.FontSize=Math.Clamp(size,13,32);if(ReadingView.Document is not null){ReadingView.Document.FontSize=Editor.FontSize;ReadingView.Document.LineHeight=Editor.FontSize*1.9;}SaveState();}
    void ToggleTheme(object sender,RoutedEventArgs e){state.Dark=!state.Dark;ApplyTheme();SaveState();}
    void ApplyTheme()
    {
        GlassWindow.Apply(this,state.Dark,state.ReducedTransparency);
        TransparencyMenu.IsChecked=state.ReducedTransparency;
        historyWindow?.SetAppearance(state.Dark,state.ReducedTransparency);
        if(ReadingView.Document is not null){ReadingView.Document.Foreground=(Brush)FindResource("InkBrush");ApplySentenceFocus(reading is not null);}
    }
    void ToggleTransparency(object sender,RoutedEventArgs e){state.ReducedTransparency=!state.ReducedTransparency;ApplyTheme();SaveState();}
    void ShowFromTray()
    {
        if(compact)Compact(this,new());
        Show();if(WindowState==WindowState.Minimized)WindowState=trayRestoreState;Activate();
    }
    void MinimizeWindow(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    void TogglePinned(object sender,RoutedEventArgs e)
    {
        pinned=!pinned;Topmost=compact||pinned;
        PinButton.Content=pinned?"\uE77A":"\uE718";
        PinButton.ToolTip=pinned?"取消窗口置顶":"窗口置顶";
        PinButton.SetResourceReference(Control.ForegroundProperty,pinned?"AccentBrush":"InkBrush");
        System.Windows.Automation.AutomationProperties.SetName(PinButton,pinned?"取消窗口置顶":"窗口置顶");
    }
    void MaximizeWindow(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    void CloseWindow(object sender,RoutedEventArgs e)=>Close();
    void OpenMore(object sender,RoutedEventArgs e){if(sender is Button {ContextMenu: {} menu} button){menu.PlacementTarget=button;menu.IsOpen=true;}}
    void BackToReading(object sender,RoutedEventArgs e){pageHost.Content=null;pageHost.Visibility=Visibility.Collapsed;readerPage.Visibility=Visibility.Visible;SelectNavigation("reader");Activate();}
    void ShowPage(FrameworkElement page)
    {
        if(compact)Compact(this,new());
        if(page.Parent is Panel parent)parent.Children.Remove(page);
        pageHost.Content=page;pageHost.Visibility=Visibility.Visible;readerPage.Visibility=Visibility.Collapsed;
    }
    void SetupPages()
    {
        readerPage=Workspace.Children.OfType<Grid>().Single();
        Grid.SetColumn(pageHost,1);Workspace.Children.Add(pageHost);
        IEnumerable<DependencyObject> Descendants(DependencyObject root){
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
            foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){
                yield return child;foreach(var nested in DescendantsLogical(child))yield return nested;
            }
        }
        IEnumerable<DependencyObject> DescendantsLogical(DependencyObject root){foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){yield return child;foreach(var nested in DescendantsLogical(child))yield return nested;}}
        var menus=Descendants(readerPage).OfType<Button>().Where(x=>x.ContextMenu is not null).Distinct().ToList();
        settingsNav=Descendants(NavRail).OfType<Button>().FirstOrDefault(x=>x.ToolTip?.ToString()=="服务设置");
        Workspace.RowDefinitions.Add(new RowDefinition());
        Workspace.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetColumn(bottomNavigation,1);Grid.SetRow(bottomNavigation,1);Workspace.Children.Add(bottomNavigation);
        foreach(var button in new[]{ReadNavButton,HistoryNavButton,settingsNav}){
            if(button is null)continue;
            if(button.Parent is Panel old)old.Children.Remove(button);
            button.Width=88;button.Height=42;button.Margin=new Thickness(3);
            if(button.Content is StackPanel content){content.Orientation=Orientation.Horizontal;foreach(var label in content.Children.OfType<TextBlock>()){label.FontSize=13;label.Margin=new Thickness(4,0,4,0);label.VerticalAlignment=VerticalAlignment.Center;}}
            bottomNavigation.Children.Add(button);
        }
        NavRail.Visibility=Visibility.Collapsed;RailColumn.Width=new GridLength(0);
        foreach(var button in menus){
            bool document=System.Windows.Automation.AutomationProperties.GetName(button)=="正文操作";
            foreach(var item in button.ContextMenu!.Items.OfType<MenuItem>()){
                if(!document&&item.Header?.ToString()=="服务设置")continue;
                var action=new Button{Content=item.Header,Margin=new Thickness(3),ToolTip=item.Header};
                if(document){action.Content=item.Header?.ToString() switch{"清空正文"=>"\uE74D","填入示例"=>"\uE8A5","字体增大"=>"\uE8E8","字体缩小"=>"\uE8E7",_=>item.Header};action.FontFamily=new FontFamily("Segoe MDL2 Assets");action.Width=32;action.Padding=new Thickness(0);System.Windows.Automation.AutomationProperties.SetName(action,item.Header?.ToString()??"");}
                action.Click+=(_,_)=>item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(document&&button.Parent is Panel panel)panel.Children.Add(action);else settingsActions.Children.Add(action);
            }
            button.Visibility=Visibility.Collapsed;
        }
        ((Panel)PinButton.Parent).Children.Remove(PinButton);
        var tools=(Panel)CompactButton.Parent;tools.Children.Insert(0,PinButton);
        PinButton.Height=35;
        PinButton.Style=(Style)FindResource("QuietButton");PinButton.Width=42;PinButton.FontFamily=new FontFamily("Segoe MDL2 Assets");
        Closed+=(_,_)=>{historyWindow?.Close();embeddedSettings?.Close();};
    }
    void ApplyStitchLayout()
    {
        Title="轻读 · Fluent Clarity";
        WindowFrame.CornerRadius=new CornerRadius(8);
        PlayerDock.CornerRadius=new CornerRadius(8);
        PlayerDock.Padding=new Thickness(12);
        bottomNavigation.HorizontalAlignment=HorizontalAlignment.Center;
        bottomNavigation.Margin=new Thickness(0,6,0,2);
        Header.Margin=new Thickness(8,4,8,8);
        OcrBar.Margin=new Thickness(8,0,8,10);
        ReadingArea.Margin=new Thickness(8,0,8,12);
        void Refine(DependencyObject root){
            foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){
                if(child is Border border&&border.CornerRadius.TopLeft>8)border.CornerRadius=new CornerRadius(8);
                if(child is TextBlock label&&label.Text=="把阅读交给声音"){label.Text="阅读工作区";label.FontSize=18;}
                Refine(child);
            }
        }
        Refine(ReadingArea);
    }
    void PreviewReading(object sender,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(Editor.Text))return;CancelRead();RenderText();}
    void ToggleTuning(object sender,RoutedEventArgs e){tuningExpanded=!tuningExpanded;TuningPanel.Visibility=tuningExpanded&&!compact?Visibility.Visible:Visibility.Collapsed;}
    void UpdateSource(){if(SourceLabel is not null)SourceLabel.Text=lastOcrImage is not null?"来自图片 · 原图已加密保存":string.IsNullOrWhiteSpace(Editor.Text)?"粘贴一段文字，或截取屏幕上的内容":"正文已在本机自动保存";}
    void Compact(object sender,RoutedEventArgs e)
    {
        compact=!compact;
        if(compact){normalState=WindowState;normalHeight=Height;normalWidth=Width;normalLeft=Left;normalTop=Top;WindowState=WindowState.Normal;}
        foreach(var control in new UIElement[]{Header,OcrBar,ReadingArea,VoicePanel,ProgressPanel,StatusPanel,bottomNavigation})control.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        RailColumn.Width=new GridLength(0);TuningPanel.Visibility=!compact&&tuningExpanded?Visibility.Visible:Visibility.Collapsed;
        RestartButton.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        CompactVoice.Visibility=ExpandButton.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        MinWidth=compact?490:820;MinHeight=compact?125:600;
        if(compact){Width=540;Height=136;Top=Math.Max(SystemParameters.WorkArea.Top,Math.Min(Top,SystemParameters.WorkArea.Bottom-Height));Left=Math.Max(SystemParameters.WorkArea.Left,Math.Min(Left,SystemParameters.WorkArea.Right-Width));}
        else{Width=normalWidth;Height=normalHeight;Left=normalLeft;Top=normalTop;WindowState=normalState;}
        PlayerLayout.ColumnDefinitions[0].Width=new GridLength(compact?0:1,compact?GridUnitType.Pixel:GridUnitType.Star);
        PlayerLayout.ColumnDefinitions[1].Width=compact?new GridLength(1,GridUnitType.Star):GridLength.Auto;
        PlaybackControls.HorizontalAlignment=compact?HorizontalAlignment.Center:HorizontalAlignment.Right;
        MaximizeCaptionButton.IsEnabled=!compact;
        Topmost=compact||pinned;AdaptPanels();
    }
    void AdaptPanels()
    {
        if(OriginalPanel is null)return;
        bool show=originalRequested&&lastOcrImage is not null&&!compact;
        if(ActualWidth<1060&&DictionaryPanel.Visibility==Visibility.Visible)show=false;
        OriginalColumn.Width=new GridLength(show?Math.Clamp(ActualWidth*.27,220,310):0);
        OriginalPanel.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        if(show&&originalZoom==0)OriginalImage.MaxWidth=Math.Max(80,OriginalColumn.Width.Value-70);
        if(ActualWidth<1000&&OriginalPanel.Visibility==Visibility.Visible){DictionaryPanel.Visibility=Visibility.Collapsed;DictionaryColumn.Width=new GridLength(0);}
    }
    void Shortcut(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.V&&Keyboard.Modifiers==ModifierKeys.Control&&(Keyboard.FocusedElement==Editor||Keyboard.FocusedElement is not TextBox)){
            try{
                var data=Clipboard.GetDataObject();
                if(ClipboardImage.HasImage(data)||data?.GetDataPresent(DataFormats.UnicodeText)==true){e.Handled=true;_=PasteClipboard(data);return;}
            }catch(Exception ex){e.Handled=true;Status.Text="读取剪贴板失败，请重新复制后重试："+ex.Message;return;}
        }
        if(e.Key==Key.Escape){Stop(sender,new());e.Handled=true;return;}
        if(e.OriginalSource is TextBox||Keyboard.FocusedElement is TextBox)return;
        if(e.Key==Key.Space){if(reading is not null&&!paused)Pause(sender,new());else Play(sender,new());e.Handled=true;}
        else if(e.Key is Key.Up or Key.Down){Rate.Value=Math.Clamp(Rate.Value+(e.Key==Key.Up?.1:-.1),.5,2);e.Handled=true;}
    }
    async void Capture(object sender,RoutedEventArgs e)
    {
        if(reading is not null)return;
        if(!OcrConfigured())return;
        byte[]? png=null;try{Hide();await Task.Delay(180);png=CaptureWindow.Select();}catch(Exception ex){Status.Text=ex.Message;}finally{Show();Activate();}
        if(png is null)return;
        await RecognizeImage(png);
    }
    void OcrEngineChanged(object sender,SelectionChangedEventArgs e)
    {
        if(loading)return;
        settings.OcrProvider=OcrEngineChoice.SelectedIndex==1?"Cloud":"Local";SaveSettings();
        Status.Text=settings.OcrProvider=="Local"?"本机 OCR · 免费离线，图片不上传":"豆包 OCR · 云端识别，按服务计费";
    }
    bool OcrConfigured()
    {
        try {
            if(settings.OcrProvider=="Cloud"&&ConfigurationChecks.OcrError(settings) is string error){Status.Text=error;return false;}
            if(settings.OcrProvider!="Cloud"&&LocalOcr.Languages().Count==0){Status.Text="未安装本机 OCR 语言，请在服务设置中查看并安装 Windows 文字识别组件。";return false;}
            return true;
        }catch(Exception ex){Status.Text="本机 OCR 不可用："+ex.Message;return false;}
    }
    async void ReadImage(object sender,RoutedEventArgs e)
    {
        if(reading is not null||!OcrConfigured())return;
        try {
            var file=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp"};
            if(file.ShowDialog(this)!=true)return;
            if(new FileInfo(file.FileName).Length>32*1024*1024)throw new InvalidOperationException("图片超过 32 MB，请裁剪后重试。");
            await RecognizeImage(await File.ReadAllBytesAsync(file.FileName));
        }catch(Exception ex){Status.Text=ex.Message;}
    }
    async Task RecognizeImage(byte[] image)
    {
        // Persist the original before OCR, including failed recognition attempts.
        NewDocument("截图 / 图片");
        var imageId=historyImages.Save(image);
        loading=true;Editor.Clear();loading=false;
        state.CurrentImageId=imageId;lastOcrImage=image;ImagePreviewButton.Visibility=Visibility.Visible;historyNote="正在识别";UpdateOriginal(true);UpdateSource();SaveState();
        ReadingView.Visibility=Visibility.Collapsed;Editor.Visibility=Visibility.Visible;EditButton.Visibility=Visibility.Collapsed;
        var cts=new CancellationTokenSource();reading=cts;ocrBusy=true;SetBusy(true);
        var local=settings.OcrProvider!="Cloud";var watch=Stopwatch.StartNew();
        Status.Text=local?"正在本机识字 · 图片不会上传…":"正在调用豆包识字…";
        try{
            var text=local?await new LocalOcr().Extract(image,settings.OcrLanguage,cts.Token):await new OcrClient(cloud).Extract(image,settings.Copy(),cts.Token);
            if(reading==cts){historyNote=null;Editor.Text=text;SaveState();Status.Text=$"{(local?"本机 OCR（免费）":"豆包 OCR")} · {watch.Elapsed.TotalSeconds:F2}s · 已保存文字和原图，可校对后朗读";}
        }
        catch(OperationCanceledException)when(cts.IsCancellationRequested){}
        catch(Exception ex){if(reading==cts){historyNote=ex.Message;SaveState();Status.Text=ex.Message+" · 原图已保存在历史";}}
        finally{cts.Dispose();if(reading==cts){reading=null;ocrBusy=false;SetBusy(false);}}
    }
    void UpdateOriginal(bool open)
    {
        if(lastOcrImage is null){OriginalImage.Source=null;originalRequested=false;AdaptPanels();return;}
        try{
            var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=new MemoryStream(lastOcrImage);bitmap.EndInit();bitmap.Freeze();OriginalImage.Source=bitmap;
            originalZoom=0;OriginalImage.Width=double.NaN;OriginalScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;ZoomLabel.Content="适应";if(open)originalRequested=true;AdaptPanels();
        }catch(Exception ex){Status.Text="原图预览失败："+ex.Message;}
    }
    void PreviewOcrImage(object sender,RoutedEventArgs e){originalRequested=!originalRequested;UpdateOriginal(false);AdaptPanels();}
    void CloseOriginal(object sender,RoutedEventArgs e){originalRequested=false;AdaptPanels();}
    void ResetZoom(object sender,RoutedEventArgs e){originalZoom=0;OriginalImage.Width=double.NaN;OriginalScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;ZoomLabel.Content="适应";AdaptPanels();}
    void ZoomIn(object sender,RoutedEventArgs e)=>ZoomOriginal(.25);
    void ZoomOut(object sender,RoutedEventArgs e)=>ZoomOriginal(-.25);
    void ZoomOriginal(double delta){if(OriginalImage.Source is not System.Windows.Media.Imaging.BitmapSource bitmap)return;originalZoom=Math.Clamp((originalZoom==0?Math.Min(1,OriginalImage.ActualWidth/bitmap.PixelWidth):originalZoom)+delta,.1,3);OriginalScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;OriginalImage.MaxWidth=double.PositiveInfinity;OriginalImage.Width=bitmap.PixelWidth*originalZoom;ZoomLabel.Content=$"{originalZoom:P0}";}
    async void RetryOriginal(object sender,RoutedEventArgs e){if(lastOcrImage is null||reading is not null)return;try{if(OcrConfigured())await RecognizeImage(lastOcrImage);}catch(Exception ex){Status.Text=ex.Message;}}
    void OpenServices(object? sender,RoutedEventArgs e)
    {
        if(reading is not null){Status.Text="请先停止朗读再修改服务设置。";return;}
        embeddedSettings?.Close();
        var window=new SettingsWindow(settings,state.Dark,state.ReducedTransparency){Owner=this};embeddedSettings=window;
        if(settingsActions.Parent is Panel old)old.Children.Remove(settingsActions);
        window.AddOptions(settingsActions);
        window.Saved=()=>{settings=window.Result;loading=true;OcrEngineChoice.SelectedIndex=settings.OcrProvider=="Cloud"?1:0;loading=false;LoadVoices();BackToReading(this,new());window.Close();embeddedSettings=null;};
        ShowPage(window.Page);
        SelectNavigation("settings");
    }
    void OpenDebug(object sender,RoutedEventArgs e){CancelRead();new AudioDebugWindow(state.Dark,state.ReducedTransparency){Owner=this}.Show();}
    void ShowHistory(object sender,RoutedEventArgs e)
    {
        SaveState();
        if(historyWindow is not null){ShowPage(historyWindow.Page);SelectNavigation("history");return;}
        historyWindow=new NativeHistoryWindow(historyImages,LoadHistory,DeleteHistory,ClearHistory,RecognizeHistory){Owner=this};
        historyWindow.Closed+=(_,_)=>historyWindow=null;
        historyWindow.ReturnToReader=()=>BackToReading(this,new());
        historyWindow.SetAppearance(state.Dark,state.ReducedTransparency);historyWindow.Refresh(state.History,true);ShowPage(historyWindow.Page);
        SelectNavigation("history");
    }
    void LoadHistory(ReaderHistory item,bool speak)
    {
        CancelRead();SaveState();saveTimer.Stop();loading=true;Editor.Text=item.Text;loading=false;
        state.CurrentHistoryId=item.Id;state.CurrentImageId=item.ImageId;state.CurrentSource=item.Source;state.SuppressCurrentHistory=false;
        RestoreHistoryImage();Edit(this,new());position=0;Seek.Value=0;PositionLabel.Text=$"0 / {Editor.Text.Length}";SaveState();
        if(speak)_=Begin(0);
    }
    void ForgetCurrentHistory()
    {
        saveTimer.Stop();state.CurrentHistoryId=null;state.CurrentImageId=null;state.SuppressCurrentHistory=true;historyNote=null;lastOcrImage=null;ImagePreviewButton.Visibility=Visibility.Collapsed;OriginalImage.Source=null;originalRequested=false;AdaptPanels();UpdateSource();
    }
    void DeleteHistory(ReaderHistory item)
    {
        if(ocrBusy){Status.Text="请先停止识别再删除历史。";return;}
        state.History.RemoveAll(x=>x.Id==item.Id);if(state.CurrentHistoryId==item.Id)ForgetCurrentHistory();SaveState();
    }
    void ClearHistory()
    {
        if(ocrBusy){Status.Text="请先停止识别再清空历史。";return;}
        state.History.Clear();ForgetCurrentHistory();SaveState();
    }
    async Task RecognizeHistory(ReaderHistory item)
    {
        try{var bytes=historyImages.Load(item.ImageId);if(bytes is null){Status.Text="历史原图不存在。";return;}if(!OcrConfigured())return;CancelRead();await RecognizeImage(bytes);}
        catch(Exception ex){Status.Text="重新识字失败："+ex.Message;}
    }
    void ExportData(object sender,RoutedEventArgs e)
    {
        try{SaveState();var dialog=new SaveFileDialog{FileName="轻读原生阅读数据.json",Filter="JSON|*.json"};if(dialog.ShowDialog(this)==true)File.WriteAllText(dialog.FileName,historyImages.Export(state));}
        catch(Exception ex){Status.Text=ex.Message;}
    }
    void ImportData(object sender,RoutedEventArgs e)
    {
        try{var dialog=new OpenFileDialog{Filter="阅读数据 JSON|*.json"};if(dialog.ShowDialog(this)!=true)return;if(new FileInfo(dialog.FileName).Length>160*1024*1024)throw new InvalidOperationException("数据文件超过 160 MB。");
            var json=File.ReadAllText(dialog.FileName);var imported=ReaderState.Import(json);var assets=HistoryImages.ReadImportImages(json);
            if(MessageBox.Show(this,"导入将覆盖当前文本、阅读历史和阅读参数，继续吗？","轻读",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            CancelRead();foreach(var asset in assets.Values)historyImages.Save(asset);state=imported;loading=true;Editor.Text=state.Text;Rate.Value=state.Tuning.Rate;Pitch.Value=state.Tuning.Pitch;Volume.Value=state.Tuning.Volume;Editor.FontSize=state.FontSize;loading=false;RestoreHistoryImage();ApplyTheme();UpdateTuningLabels();Edit(sender,e);SaveState();Status.Text="已导入阅读数据（含原图）";
        }catch(Exception ex){Status.Text="导入失败："+ex.Message;}
    }
    void ToggleDictionary(object sender,RoutedEventArgs e)
    {
        bool show=DictionaryPanel.Visibility!=Visibility.Visible;DictionaryPanel.Visibility=show?Visibility.Visible:Visibility.Collapsed;DictionaryColumn.Width=new GridLength(show?290:0);
        if(show){originalRequested=false;AdaptPanels();RefreshDictionary(sender,e);}else{dictionary?.Cancel();AdaptPanels();}
    }
    void DictionaryOptChanged(object sender,RoutedEventArgs e){if(!loading)RefreshDictionary(sender,e);}
    async void RefreshDictionary(object? sender,RoutedEventArgs e)
    {
        dictionary?.Cancel();var cts=new CancellationTokenSource();dictionary=cts;
        words.Clear();foreach(var token in ReaderText.EnglishWords(Editor.Text))words.Add(dictionaryCache.TryGetValue(token.Word,out var cached)?cached with{Start=token.Start}:new(token.Word,token.Start,"在线查询未启用"));
        if(OnlineDictionary.IsChecked!=true){cts.Dispose();if(dictionary==cts)dictionary=null;return;}
        try{
            var api=new ReaderDictionary(dictionaryHttp);
            using var gate=new SemaphoreSlim(4);
            await Task.WhenAll(words.ToArray().Select(async word=>{
                await gate.WaitAsync(cts.Token);
                try{var result=await api.Lookup(word,cts.Token);if(dictionary==cts){dictionaryCache[word.Word]=result;var i=words.IndexOf(word);if(i>=0)words[i]=result;}}
                finally{gate.Release();}
            }));
        }catch(OperationCanceledException)when(cts.IsCancellationRequested){}
        catch(Exception ex){Status.Text="字典查询失败："+ex.Message;}
        finally{cts.Dispose();if(dictionary==cts)dictionary=null;}
    }
    async void JumpDictionary(object sender,MouseButtonEventArgs e){if(Words.SelectedItem is DictionaryWord word){CancelRead();await Begin(word.Start);}}
}

