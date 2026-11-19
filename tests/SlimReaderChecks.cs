using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LightReader;

static class SlimReaderChecks
{
    public static async Task Run()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderSlimTests",Guid.NewGuid().ToString("N"),"state.dat");
        var thread=new Thread(()=>{
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var window=new NativeReaderWindow(profile);
            window.Loaded+=async (_,_)=>{
                await Task.Yield();
                try{
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    T Find<T>(string name) where T:class=>(T)window.FindName(name);
                    object? Field(string name)=>window.GetType().GetField(name,flags)!.GetValue(window);
                    void Set(string name,object? value)=>window.GetType().GetField(name,flags)!.SetValue(window,value);
                    void Call(string name,params object[] args)=>window.GetType().GetMethod(name,flags)!.Invoke(window,args);
                    IEnumerable<DependencyObject> Children(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}}
                    void Capture(string name){window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts");using var output=File.Create(Path.Combine("artifacts",name));png.Save(output);}
                    var editor=Find<TextBox>("Editor");var view=Find<RichTextBox>("ReadingView");
                    if(!view.IsVisible||editor.IsVisible||!editor.Text.Contains("欢迎使用声阅")||!view.Document.Blocks.OfType<Table>().Any())throw new Exception("First launch not seeded/rendered");
                    foreach(var name in new[]{"HistoryNavButton","EditButton","PasteButton","OcrEngineChoice","Engine"})if(Find<FrameworkElement>(name).IsVisible)throw new Exception("Obsolete control visible: "+name);
                    if(Children(window).OfType<Button>().Any(b=>b.IsVisible&&(b.Content?.ToString() is "更多" or "填入示例" or "编辑模式")))throw new Exception("Obsolete action still visible");
                    var picker=Find<VoicePicker>("Voice");if(picker.ProviderElement is not null||picker.Items.Count<100||picker.Items.OfType<VoiceChoice>().Any(v=>!v.Id.EndsWith("Neural")))throw new Exception("Only Edge voice catalogue expected");
                    if(Children(window).OfType<TextBlock>().Any(t=>t.Text.Contains("免费在线")))throw new Exception("Removed online label visible");
                    var settings=(Settings)Field("settings")!;if(settings.SpeechProvider!="Edge"||settings.OcrProvider!="Local")throw new Exception("Runtime providers not restricted");
                    picker.IsDropDownOpen=true;await Task.Delay(70);if(!picker.IsDropDownOpen)throw new Exception("Edge voice selector unavailable");picker.IsDropDownOpen=false;
                    Console.WriteLine("PASS first-launch sample, single reading surface, removed controls and Edge-only selector");

                    const string markdown="# 表格验收\n\n| 项目 | 内容 | 数值 |\n| :--- | :---: | ---: |\n| **中文** | 单元格里的文字能够正常换行，长内容不会挤在一起。 | 123 |\n| English | Reading gives us time to think. | 456 |\n\n表格之后的正文。";
                    editor.Text=markdown;Call("PreviewReading",window,new RoutedEventArgs());view.UpdateLayout();
                    var table=view.Document.Blocks.OfType<Table>().Single();var rows=table.RowGroups.Single().Rows;
                    if(table.Columns.Count!=3||rows.Count!=3||rows.Any(row=>row.Cells.Count!=3)||rows[0].Cells[0].FontWeight!=FontWeights.Bold||rows[0].Cells[1].TextAlignment!=TextAlignment.Center||rows[0].Cells[2].TextAlignment!=TextAlignment.Right)throw new Exception("Native table structure/alignment incorrect");
                    var runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;var map=(ReadingDocument)Field("readingDocument")!;
                    foreach(var run in runs)if(markdown.Substring(run.Start,run.Length)!=run.Run.Text||map.SourcePosition(view.Document,run.Run.ContentStart)!=run.Start)throw new Exception($"Table source offsets lost: {run.Run.Text} expected={run.Start} mapped={map.SourcePosition(view.Document,run.Run.ContentStart)} prefix=[{new TextRange(view.Document.ContentStart,run.Run.ContentStart).Text}] visible=[{new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text}]");
                    var chinese=runs.First(r=>r.Run.Text=="中");view.Selection.Select(chinese.Run.ContentStart,chinese.Run.ContentEnd);view.Selection.Text="汉";await Task.Delay(40);
                    if(editor.Text!=markdown.Replace("中文","汉文"))throw new Exception("Table cell edit damaged Markdown: "+editor.Text);
                    Call("PreviewReading",window,new RoutedEventArgs());Capture("slim-reader-table.png");
                    window.Width=820;window.Height=600;await Task.Delay(100);view.UpdateLayout();Capture("slim-reader-narrow.png");
                    foreach(var name in new[]{"PlayButton","StopButton","CaptureButton","ImageOcrButton"}){var element=Find<FrameworkElement>(name);var point=element.TransformToAncestor(window).Transform(new Point());if(point.X<0||point.X+element.ActualWidth>window.ActualWidth+1||point.Y+element.ActualHeight>window.ActualHeight+1)throw new Exception("Narrow clipping: "+name);}
                    window.Width=1180;window.Height=800;await Task.Delay(80);
                    Console.WriteLine("PASS table headers/columns/alignment, cell editing, exact source offsets and narrow layout");

                    editor.Text="前文。后文。";Call("PreviewReading",window,new RoutedEventArgs());view.Focus();
                    runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;var after=runs.First(r=>r.Run.Text=="后");view.Selection.Select(after.Run.ContentStart,after.Run.ContentStart);
                    var paste=window.GetType().GetMethod("PasteClipboard",flags)!;
                    await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"插入。")})!;
                    if(editor.Text!="前文。插入。后文。"||!view.IsVisible||editor.IsVisible)throw new Exception("Reading-page paste did not preserve caret/source");
                    runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;after=runs.First(r=>r.Run.Text=="后");view.Selection.Select(after.Run.ContentStart,after.Run.ContentStart);
                    Find<Button>("PlayButton").Focus();Set("pasteAtReadingSelection",true);
                    await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"菜单。")})!;Set("pasteAtReadingSelection",false);
                    if(editor.Text!="前文。插入。菜单。后文。")throw new Exception("Context-menu focus caused whole-document replacement");
                    view.Focus();
                    view.Selection.Select(view.Document.ContentStart,view.Document.ContentEnd);
                    await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,markdown)})!;
                    if(editor.Text!=markdown||!view.Document.Blocks.OfType<Table>().Any())throw new Exception("Markdown paste did not render table");
                    if(!view.ContextMenu.Items.OfType<MenuItem>().Any(item=>item.Header?.ToString()=="粘贴"))throw new Exception("Context paste missing");
                    Console.WriteLine("PASS reading-page text/Markdown paste, selection replacement and context menu");
                    var fixture=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","local-ocr-fixture.png");
                    if(!File.Exists(fixture))throw new Exception("Run --check-local-ocr first to create the bilingual image fixture");
                    var png=File.ReadAllBytes(fixture);var imageData=new DataObject();imageData.SetText("Image format must take priority");imageData.SetData("PNG",new MemoryStream(png));
                    await (Task)paste.Invoke(window,new object[]{imageData})!;
                    if(!editor.Text.Contains("Hello")||!editor.Text.Contains("12345")||!view.IsVisible||editor.IsVisible||!Find<TextBlock>("Status").Text.StartsWith("本机 OCR"))throw new Exception("Reading-page image paste/local OCR failed");
                    var imageState=ReaderState.Load(profile);if(imageState.History.Count!=0||imageState.CurrentImageId is null||!new HistoryImages(profile).Load(imageState.CurrentImageId)!.SequenceEqual(png))throw new Exception("Current image not persisted without history");
                    Console.WriteLine("PASS image-over-text clipboard priority, actual local OCR and encrypted current image without history");

                    using(var playback=new Playback()){
                        var reading=new CancellationTokenSource();Set("reading",reading);Set("player",playback);Set("activeTuning",new SpeechTuning(Find<Slider>("Rate").Value,Find<Slider>("Pitch").Value,Find<Slider>("Volume").Value));
                        playback.Pause();await playback.Add(new byte[48000],default);Set("paused",true);
                        Find<Slider>("Volume").Value=.35;await Task.Delay(600);
                        if(!ReferenceEquals(Field("player"),playback)||reading.IsCancellationRequested||!ReferenceEquals(Field("reading"),reading)||Math.Abs(playback.Volume-.35)>.001||((DispatcherTimer)Field("tuningTimer")!).IsEnabled||!playback.Paused)throw new Exception("Paused volume regenerated/canceled speech");
                        Call("Play",window,new RoutedEventArgs());await Task.Delay(70);
                        if(playback.Paused||!ReferenceEquals(Field("reading"),reading))throw new Exception("Volume-only change restarted on resume");
                        Find<Slider>("Volume").Value=.2;await Task.Delay(550);
                        if(Math.Abs(playback.Volume-.2)>.001||!ReferenceEquals(Field("reading"),reading)||reading.IsCancellationRequested)throw new Exception("Playing volume restarted speech");
                        Call("Stop",window,new RoutedEventArgs());reading.Dispose();
                    }
                    var requestA=JsonSerializer.Serialize(EdgeTtsClient.Request("测试","zh-CN-XiaoxiaoNeural",new(1,1,0)));
                    var requestB=JsonSerializer.Serialize(EdgeTtsClient.Request("测试","zh-CN-XiaoxiaoNeural",new(1,1,1)));
                    if(requestA!=requestB)throw new Exception("Synthesis request depends on output volume");
                    Console.WriteLine("PASS actual output volume changes during playback/pause, same session/PCM and synthesis-independent volume");

                    var nav=(Button)Field("settingsNav")!;nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
                    var preferences=(SettingsWindow)Field("embeddedSettings")!;
                    var settingsLabels=Children(preferences.Page).OfType<TextBlock>().Select(t=>t.Text).ToList();
                    if(settingsLabels.Any(t=>t.Contains("豆包")||t.Contains("火山")||t.Contains("本机离线引擎")||t.Contains("免费在线"))||Children(preferences.Page).OfType<PasswordBox>().Any())throw new Exception("Removed provider settings still present");
                    if(!settingsLabels.Any(t=>t.Contains("Edge"))||!settingsLabels.Any(t=>t.Contains("本机 OCR")))throw new Exception("Remaining provider settings missing");
                    Capture("slim-reader-settings.png");Call("BackToReading",window,new RoutedEventArgs());preferences.Close();
                    Call("ToggleTheme",window,new RoutedEventArgs());Capture("slim-reader-dark.png");Call("ToggleTheme",window,new RoutedEventArgs());
                    Console.WriteLine("PASS simplified preferences and dark-theme rendering");

                    Call("Clear",window,new RoutedEventArgs());if(editor.Text!=""||!view.IsVisible||editor.IsVisible)throw new Exception("Clear lost reading-only surface");
                    window.Close();
                    var restored=new NativeReaderWindow(profile);restored.Show();await Task.Delay(60);
                    if(((TextBox)restored.FindName("Editor")).Text!=""||ReaderState.Load(profile).History.Count!=0)throw new Exception("Second launch seeded sample or recorded history");
                    restored.Close();
                    Console.WriteLine("PASS encrypted current-document persistence, no history, empty document retained on second launch");
                    done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}
                finally{window.Close();app.Shutdown();}
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(45));if(!thread.Join(3000))throw new Exception("Slim UI STA did not exit");
    }
}
