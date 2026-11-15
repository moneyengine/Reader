using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using LightReader;

static class GlassUiChecks
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    public static async Task Run(bool preview=false)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderGlassTests",Guid.NewGuid().ToString("N"),"state.dat");
        const string article="有些内容，适合慢慢读。\n\n把文字粘贴进来，或者截取屏幕上的一段内容。轻读会提取文字，让你先校对，再开始朗读。\n\n让眼睛休息片刻，也让注意力回到内容本身。";
        var thread=new Thread(()=>{
            try{
                var app=new Application();
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                    dc.DrawRectangle(Brushes.White,null,new Rect(0,0,680,510));
                    dc.DrawText(new FormattedText("把阅读交给声音",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),30,Brushes.Black,1),new Point(32,26));
                    var body=new FormattedText(article,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),23,Brushes.Black,1){MaxTextWidth=610,LineHeight=36};dc.DrawText(body,new Point(32,90));
                }
                var bitmap=new RenderTargetBitmap(680,510,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);
                var images=new HistoryImages(profile);var imageId=images.Save(stream.ToArray());
                var state=new ReaderState{Text=article,CurrentImageId=imageId,CurrentSource="截图"};state.Remember("一次安静的阅读。\nReading gives us time to think.");var record=state.Upsert(article,imageId,"截图")!;state.CurrentHistoryId=record.Id;state.Save(profile);
                var window=new NativeReaderWindow(profile);
                window.Closed+=(_,_)=>{if(preview)done.TrySetResult();};
                window.Loaded+=async(_,_)=>{
                    try{
                        T Find<T>(string name) where T:class=>window.FindName(name) as T??throw new Exception("Missing "+name);
                        void Call(string name)=>window.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                        void Capture(Window target,string name){target.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(target);var b=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(target.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);b.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));Directory.CreateDirectory("artifacts");using var output=File.Create(Path.Combine("artifacts",name));png.Save(output);}
                        if(preview){Find<Slider>("Volume").Value=0;window.Title="轻读 · 液态玻璃界面验收";Call("PreviewOcrImage");Call("PreviewReading");return;}
                        await Task.Delay(200);Call("PreviewOcrImage");Call("PreviewReading");await Task.Delay(100);
                        if(Find<Image>("OriginalImage").Source is null||!Find<Border>("OriginalPanel").IsVisible)throw new Exception("Inline original-image comparison missing");
                        if(WindowChrome.GetWindowChrome(window)?.CaptionHeight<30)throw new Exception("Native draggable titlebar missing");
                        var captionPoint=window.PointToScreen(new Point(400,22));var hit=SendMessage(new WindowInteropHelper(window).Handle,0x84,IntPtr.Zero,new IntPtr(((int)captionPoint.Y<<16)|((int)captionPoint.X&0xffff)));
                        if(hit.ToInt32()!=2)throw new Exception("Titlebar native hit-test does not return HTCAPTION; dragging would fail");
                        Capture(window,"glass-main-light.png");
                        var report=window.GetType().GetMethod("ReportPosition",BindingFlags.Instance|BindingFlags.NonPublic)!;
                        report.Invoke(window,new object[]{article.IndexOf("轻读",StringComparison.Ordinal),1});
                        var focusRuns=(List<(int Start,int Length,System.Windows.Documents.Run Run)>)window.GetType().GetField("runs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        var sentence=SentenceFocus.Range(article,article.IndexOf("轻读",StringComparison.Ordinal));
                        foreach(var entry in focusRuns){var expected=entry.Start>=sentence.Start&&entry.Start<sentence.End?1:.35;if(Math.Abs(entry.Run.Foreground.Opacity-expected)>.001)throw new Exception("Sentence focus opacity wrong");}
                        Capture(window,"glass-sentence-focus.png");
                        var current=article.IndexOf("轻读",StringComparison.Ordinal);
                        if(!focusRuns.Any(x=>x.Start==current&&x.Run.Background is SolidColorBrush b&&b.Color.A>0))throw new Exception("Current word highlight missing");
                        report.Invoke(window,new object[]{current+1,1});
                        if(focusRuns.Single(x=>x.Start==current).Run.Background!=Brushes.Transparent||!focusRuns.Any(x=>x.Start==current+1&&x.Run.Background is SolidColorBrush b&&b.Color.A>0))throw new Exception("Word highlight did not move within sentence");
                        Call("Stop");if(focusRuns.Any(x=>x.Run.Foreground.Opacity!=1))throw new Exception("Stopped text remains dimmed");
                        Console.WriteLine("PASS active sentence full opacity, other sentences 35%, stop restores normal display");
                        var combo=Find<ComboBox>("Voice");combo.IsDropDownOpen=true;await Task.Delay(100);if(!combo.IsDropDownOpen||combo.Items.Count==0)throw new Exception("Glass voice dropdown failed");combo.IsDropDownOpen=false;
                        Find<Slider>("Rate").Value=1.13;if((Find<ComboBox>("RateChoice").SelectedItem as ComboBoxItem)?.Content?.ToString()!="1.13×")throw new Exception("Custom rate shows empty selector");
                        Find<ComboBox>("RateChoice").SelectedIndex=2;if(Find<Slider>("Rate").Value!=1.25)throw new Exception("Rate preset not applied");
                        Call("ToggleTuning");await Task.Delay(60);if(!Find<Border>("TuningPanel").IsVisible)throw new Exception("Advanced controls unavailable");Call("ToggleTuning");
                        Call("ToggleTransparency");if(GlassWindow.Apply(window,false,true))throw new Exception("Reduced transparency enabled acrylic");
                        if(((SolidColorBrush)window.FindResource("GlassBrush")).Color.A!=255)throw new Exception("Reduced transparency did not become opaque");Call("ToggleTransparency");
                        var settingsWindow=new SettingsWindow(new Settings()){Owner=window};settingsWindow.Show();settingsWindow.UpdateLayout();
                        IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
                        var modelChoice=Descendants(settingsWindow).OfType<ComboBox>().Single(x=>x.IsEditable);modelChoice.ApplyTemplate();
                        var modelEdit=modelChoice.Template.FindName("PART_EditableTextBox",modelChoice) as TextBox??throw new Exception("Model ID editable combo lost native editor");
                        modelEdit.Text="ep-ui-test";if(modelChoice.Text!="ep-ui-test")throw new Exception("Model ID keyboard editor binding broken");settingsWindow.Close();
                        var debugWindow=new AudioDebugWindow(){Owner=window};debugWindow.Show();debugWindow.UpdateLayout();if(debugWindow.Content is not Border)throw new Exception("Debug window theme not applied");debugWindow.Close();
                        Call("ShowHistory");await Task.Delay(100);var history=window.OwnedWindows.OfType<NativeHistoryWindow>().Single();
                        var text=(TextBox)typeof(NativeHistoryWindow).GetField("text",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        var img=(Image)typeof(NativeHistoryWindow).GetField("image",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        if(history.IsVisible||!text.IsVisible||!img.IsVisible||text.ActualWidth<150||img.ActualWidth<150)throw new Exception("Embedded history full text and original not side by side");Capture(window,"glass-history-light.png");Call("BackToReading");history.Close();
                        Call("OpenServices");await Task.Delay(100);
                        if(window.OwnedWindows.OfType<SettingsWindow>().Any(x=>x.IsVisible))throw new Exception("Settings opened new window");
                        Capture(window,"glass-settings-inline.png");Call("BackToReading");
                        Call("ToggleTheme");await Task.Delay(60);Capture(window,"glass-main-dark.png");Call("ToggleTheme");
                        window.Width=820;window.Height=600;await Task.Delay(150);window.UpdateLayout();
                        foreach(var name in new[]{"PlayButton","StopButton","CaptureButton","ImageOcrButton"}){
                            var element=Find<FrameworkElement>(name);var pos=element.TransformToAncestor(window).Transform(new Point());if(pos.X<0||pos.X+element.ActualWidth>window.ActualWidth+1||pos.Y+element.ActualHeight>window.ActualHeight+1)throw new Exception("Narrow layout clipped "+name);
                        }
                        Capture(window,"glass-main-narrow.png");
                        var width=window.Width;var height=window.Height;Call("Compact");await Task.Delay(100);if(window.ActualHeight>180||!window.Topmost||Find<Border>("OriginalPanel").IsVisible)throw new Exception("Compact glass player layout invalid");Capture(window,"glass-player.png");Call("Compact");await Task.Delay(100);
                        if(window.Width!=width||window.Height!=height||window.Topmost)throw new Exception("Compact mode did not restore original window bounds");
                        window.WindowState=WindowState.Maximized;await Task.Delay(100);
                        var handle=new WindowInteropHelper(window).Handle;var work=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                        if(!GetWindowRect(handle,out var rect)||rect.Left<work.Left||rect.Top<work.Top||rect.Right>work.Right||rect.Bottom>work.Bottom)throw new Exception("Maximized window covers taskbar/outside monitor work area");
                        Console.WriteLine($"PASS maximized native window stays within work area {work.Width}x{work.Height}");
                        Call("Compact");await Task.Delay(60);Call("Compact");await Task.Delay(60);if(window.WindowState!=WindowState.Maximized)throw new Exception("Maximized window state not restored from compact player");window.WindowState=WindowState.Normal;
                        Console.WriteLine("PASS glass main/history rendering, original comparison, voice popup, editable model ID, settings/debug styling, arbitrary rate, tuning panel, opaque fallback, dark theme, 820px layout and normal/maximized compact restore, native draggable caption hit-test");
                        Console.WriteLine("Native acrylic available: "+GlassWindow.Apply(window,false,false));
                        Call("Clear");
                        if(Find<TextBox>("Editor").Text.Length!=0||!Find<System.Windows.Controls.FlowDocumentScrollViewer>("ReadingView").IsVisible||focusRuns.Count!=0)throw new Exception("Reading-mode clear did not clear and retain preview");
                        Console.WriteLine("PASS reading-mode clear preserves preview and clears text/runs");
                        var paste=window.GetType().GetMethod("PasteClipboard",BindingFlags.Instance|BindingFlags.NonPublic)!;
                        await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"# 标题\n\n**重点**正文。")})!;
                        if(!Find<FlowDocumentScrollViewer>("ReadingView").IsVisible||Find<TextBox>("Editor").IsVisible)throw new Exception("Markdown paste did not enter preview");
                        await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"普通文本。")})!;
                        if(!Find<TextBox>("Editor").IsVisible)throw new Exception("Plain paste not editable");
                        if(Find<Button>("CaretButton").Parent!=Find<Button>("PlayButton").Parent)throw new Exception("Caret button outside playback row");
                        Console.WriteLine("PASS Markdown paste previews, plain paste edits, caret button shares playback row");
                        Call("TogglePinned");if(!window.Topmost)throw new Exception("Pin did not enable topmost");
                        Call("Compact");Call("Compact");if(!window.Topmost)throw new Exception("Compact restore lost pin");
                        Call("TogglePinned");if(window.Topmost)throw new Exception("Unpin did not disable topmost");
                        Console.WriteLine("PASS pin/unpin and pin preservation across compact mode");
                        done.TrySetResult();
                    }catch(Exception ex){done.TrySetException(ex);}finally{if(!preview)window.Close();}
                };
                app.Run(window);
            }catch(Exception ex){done.TrySetException(ex);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(preview?900:35));thread.Join(3000);
    }
}
