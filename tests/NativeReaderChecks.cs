using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

static class NativeReaderChecks
{
    public static async Task CheckVoices()
    {
        var voices=LightReader.WindowsSpeech.GetVoices();
        var selected=new[]{voices.First(v=>v.Id.StartsWith("winrt:")),voices.First(v=>!v.Id.StartsWith("winrt:"))};
        foreach(var voice in selected) {
            using var engine=new LightReader.WindowsSpeech();
            var positions=new List<LightReader.ReaderProgress>();
            await engine.Speak("你好。Hello world.",voice.Id,default,new(1.25,1.1,0),new InlineProgress(p=>positions.Add(p))).WaitAsync(TimeSpan.FromSeconds(25));
            if(positions.Count==0||positions.Any(p=>p.Start<0||p.Start>=15))throw new Exception("Speech word boundaries missing or invalid");
            Console.WriteLine($"PASS {voice.Name}: pitch/rate/volume, {positions.Count} progress events");
        }
    }
    sealed class InlineProgress(Action<LightReader.ReaderProgress> report) : IProgress<LightReader.ReaderProgress>
    { public void Report(LightReader.ReaderProgress value)=>report(value); }
    public static async Task Run()
    {
        var fixture="你好，世界。\r\nHello world! 😀 <tag> & test.";
        if(string.Concat(LightReader.ReaderText.Tokens(fixture).Select(x=>x.Text))!=fixture)throw new Exception("Token rendering dropped content");
        var words=LightReader.ReaderText.EnglishWords("中文Hello world, hello don't stop.");
        if(words.Count!=4||words[0].Word!="hello")throw new Exception("Dictionary word extraction");
        var old=JsonSerializer.Serialize(new {version=1,values=new Dictionary<string,string>{
            ["textReader_inputText"]="旧版文本",
            ["textReader_settings"]="{\"rate\":1.25,\"pitch\":1.1,\"volume\":0.5}",
            ["textReader_theme"]="dark",
            ["textReader_history"]="[{\"text\":\"旧版历史\",\"time\":1730000000000}]"
        }});
        var migrated=LightReader.ReaderState.Import(old);
        if(migrated.Text!="旧版文本"||!migrated.Dark||migrated.Tuning.Rate!=1.25||migrated.History.Count!=1)throw new Exception("Legacy data import failed");
        var restored=LightReader.ReaderState.Import(JsonSerializer.Serialize(migrated));
        if(restored.History[0].Text!="旧版历史")throw new Exception("Native export/import failed");
        Console.WriteLine("PASS lossless text, English word extraction, legacy migration and native data roundtrip");
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderNativeTests",Guid.NewGuid().ToString("N"),"state.dat");
        new LightReader.ReaderState{Text="恢复测试"}.Save(profile);
        var thread=new Thread(()=>{
            var app=new Application();var window=new LightReader.NativeReaderWindow(profile);
            window.Loaded+=async(_,_)=>{
                try {
                    T Find<T>(string name) where T:class => window.FindName(name) as T??throw new Exception("Missing "+name);
                    void Click(string name)=>Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(Find<TextBlock>("PositionLabel").Text!="0 / 4")throw new Exception("Restored text count missing");
                    var editor=Find<TextBox>("Editor");var display=Find<FlowDocumentScrollViewer>("ReadingView");
                    Find<Slider>("Volume").Value=0;
                    editor.Text=fixture+string.Concat(Enumerable.Repeat("继续测试朗读进度、暂停和跳转。",12));
                    Click("PlayButton");await Task.Delay(1500);
                    if(display.Visibility!=Visibility.Visible||Find<Button>("PauseButton").IsEnabled!=true)throw new Exception("Native playback did not start");
                    var para=(Paragraph)display.Document.Blocks.FirstBlock;
                    var rendered=string.Concat(para.Inlines.Select(x=>x is Run r?r.Text:x is LineBreak?"\n":""));
                    if(rendered!=editor.Text.Replace("\r\n","\n"))throw new Exception("Native FlowDocument changed text");
                    Click("PlayButton");if(Find<Button>("PlayButton").Content?.ToString()?.Contains("继续")!=true)throw new Exception("Unified play/pause did not pause");var index=Find<Slider>("Seek").Value;
                    await Task.Delay(200);if(Find<Slider>("Seek").Value!=index)throw new Exception("Paused progress changed");
                    var theme=window.GetType().GetMethod("ToggleTheme",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                    theme.Invoke(window,new object[]{window,new RoutedEventArgs()});
                    var highlighted=para.Inlines.OfType<Run>().Where(r=>r.Background is SolidColorBrush b&&b.Color.A!=0).ToList();
                    if(highlighted.Count==0||highlighted.Any(r=>r.Foreground is not SolidColorBrush b||b.Color!=Colors.Black))throw new Exception("Dark theme highlighted text contrast invalid");
                    theme.Invoke(window,new object[]{window,new RoutedEventArgs()});
                    Find<Slider>("Rate").Value=1.25;await Task.Delay(550);
                    if(Find<Button>("PlayButton").Content?.ToString()?.Contains("继续")!=true)throw new Exception("Changing paused tuning resumed playback");
                    Click("PlayButton");await Task.Delay(300);
                    Find<Slider>("Seek").Value=.5;
                    Find<Slider>("Seek").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent});
                    await Task.Delay(450);
                    if(Find<Slider>("Seek").Value<.45)throw new Exception("Seek did not update position");
                    Click("StopButton");
                    if(Find<Slider>("Seek").Value!=0||Find<Button>("StopButton").IsEnabled)throw new Exception("Stop did not reset");
                    var fixturePng=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","local-ocr-fixture.png");
                    if(File.Exists(fixturePng)){
                        var imageBytes=File.ReadAllBytes(fixturePng);
                        var paste=window.GetType().GetMethod("PasteClipboard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                        var clipboard=new DataObject();clipboard.SetText("mixed-format text must not replace OCR");
                        using var pngStream=new MemoryStream(imageBytes);pngStream.Position=7;clipboard.SetData("PNG",pngStream);
                        if(!LightReader.ClipboardImage.HasImage(clipboard)||!LightReader.ClipboardImage.ReadPng(clipboard).SequenceEqual(imageBytes)||pngStream.Position!=7)throw new Exception("PNG clipboard handling failed");
                        await (Task)paste.Invoke(window,new object[]{clipboard})!;
                        if(!editor.Text.Contains("免费")||!editor.Text.Contains("Hello")||!Find<TextBlock>("Status").Text.Contains("本机 OCR"))throw new Exception("Native offline OCR integration failed");
                        window.GetType().GetMethod("ShowHistory",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                        var history=window.OwnedWindows.OfType<LightReader.NativeHistoryWindow>().Single();
                        if(!history.IsVisible)throw new Exception("History button did not show window");
                        object Field(string name)=>history.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(history)!;
                        if(((TextBox)Field("text")).Text!=editor.Text||((Image)Field("image")).Source is null)throw new Exception("History missing full OCR text or original image preview");
                        history.Close();
                        var saved=LightReader.ReaderState.Load(profile);var imageRecord=saved.History.First(x=>x.ImageId is not null);
                        var images=new LightReader.HistoryImages(profile);
                        if(!images.Load(imageRecord.ImageId)!.SequenceEqual(imageBytes))throw new Exception("History original image changed");
                        var exported=images.Export(saved);var imported=LightReader.ReaderState.Import(exported);var assets=LightReader.HistoryImages.ReadImportImages(exported);
                        if(imported.History.First(x=>x.ImageId is not null).Text!=editor.Text||!assets[imageRecord.ImageId!].SequenceEqual(imageBytes))throw new Exception("History text/image export roundtrip failed");
                        images.Clean(saved.History.Select(x=>x.ImageId));if(images.Load(imageRecord.ImageId) is null)throw new Exception("Referenced image removed");
                        Console.WriteLine("PASS actual history window visible, full original text/image preview, encrypted image persistence, image export/import roundtrip");
                        var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=new MemoryStream(imageBytes);bitmap.EndInit();bitmap.Freeze();
                        var bitmapData=new DataObject(DataFormats.Bitmap,bitmap);
                        if(!LightReader.ClipboardImage.HasImage(bitmapData)||LightReader.ClipboardImage.ReadPng(bitmapData).Length==0)throw new Exception("Bitmap clipboard handling failed");
                        var alias=new DataObject("image/png",imageBytes);
                        if(!LightReader.ClipboardImage.ReadPng(alias).SequenceEqual(imageBytes))throw new Exception("Browser PNG clipboard handling failed");
                        var textData=new DataObject(DataFormats.UnicodeText,"Hello native text paste");
                        if(LightReader.ClipboardImage.HasImage(textData))throw new Exception("Text mistaken for image");
                        await (Task)paste.Invoke(window,new object[]{textData})!;
                        if(editor.Text!="Hello native text paste")throw new Exception("Text paste regression");
                        Console.WriteLine("PASS PNG/bitmap/browser PNG clipboard formats, image priority over text, real local OCR and text paste; system clipboard untouched");
                    }
                    window.GetType().GetMethod("Compact",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                    await Task.Delay(100);if(window.ActualHeight>250||!window.Topmost)throw new Exception("Compact layout invalid");
                    Console.WriteLine("PASS native controls, actual local playback, lossless FlowDocument, pause/tuning/resume, seek, stop, compact");
                    done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}
                finally{window.Close();}
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(30));thread.Join(3000);
        var stored=LightReader.ReaderState.Load(profile);
        if(!stored.Text.Contains("Hello")||stored.History.Count==0)throw new Exception("Native autosave/history missing");
        if(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(profile)).Contains("Hello"))throw new Exception("Native storage is not encrypted");
        if(File.Exists(Path.Combine(Directory.GetCurrentDirectory(),"artifacts","local-ocr-fixture.png"))){
            var imageRecord=stored.History.First(x=>x.ImageId is not null);
            var images=new LightReader.HistoryImages(profile);
            if(images.Load(imageRecord.ImageId) is null)throw new Exception("Image history lost after text paste/restart");
            var pending=stored.Upsert("",imageRecord.ImageId,"识别失败",note:"没有文字")!;
            var completed=stored.Upsert("重试成功",pending.ImageId,pending.Source,pending.Id);
            if(completed!.Id!=pending.Id||stored.History.Count(x=>x.Id==pending.Id)!=1)throw new Exception("OCR pending history duplicated");
            var legacy=LightReader.ReaderState.Import("{\"History\":[{\"Id\":\"old\",\"Text\":\"旧文本\",\"Time\":\"2026-11-11T00:00:00Z\"}]}");
            if(legacy.History.Single().ImageId is not null)throw new Exception("Legacy history migration failed");
            images.Clean(Array.Empty<string>());if(images.Load(imageRecord.ImageId) is not null)throw new Exception("Deleted image cleanup failed");
            Console.WriteLine("PASS retained screenshot after restart/text paste, failed OCR history update, legacy text import and orphan image cleanup");
        }
        Console.WriteLine("PASS isolated encrypted autosave and history persisted");
    }
}


