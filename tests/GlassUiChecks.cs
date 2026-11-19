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
        string checkpoint="starting";
        bool checksPassed=false;
        using var watchdog=new System.Threading.Timer(_=>{Console.Error.WriteLine("FAIL UI test timeout at "+checkpoint);Environment.Exit(124);},null,TimeSpan.FromSeconds(preview?900:60),Timeout.InfiniteTimeSpan);
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderGlassTests",Guid.NewGuid().ToString("N"),"state.dat");
        const string article="有些内容，适合慢慢读。\n\n把文字粘贴进来，或者截取屏幕上的一段内容。轻读会提取文字，让你先校对，再开始朗读。\n\n让眼睛休息片刻，也让注意力回到内容本身。";
        Action runUi=()=>{
            try{
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                    dc.DrawRectangle(Brushes.White,null,new Rect(0,0,680,510));
                    dc.DrawText(new FormattedText("把阅读交给声音",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),30,Brushes.Black,1),new Point(32,26));
                    var body=new FormattedText(article,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),23,Brushes.Black,1){MaxTextWidth=610,LineHeight=36};dc.DrawText(body,new Point(32,90));
                }
                var bitmap=new RenderTargetBitmap(680,510,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);
                var images=new HistoryImages(profile);var imageId=images.Save(stream.ToArray());
                var state=new ReaderState{Text=article,CurrentImageId=imageId,CurrentSource="截图"};state.Remember("一次安静的阅读。\nReading gives us time to think.");var record=state.Upsert(article,imageId,"截图")!;state.CurrentHistoryId=record.Id;state.Save(profile);
                var window=new NativeReaderWindow(profile);
                window.Closed+=(_,_)=>{if(preview){done.TrySetResult();app.Shutdown();}};
                window.Loaded+=async(_,_)=>{
                    try{
                        T Find<T>(string name) where T:class=>window.FindName(name) as T??throw new Exception("Missing "+name);
                        void Call(string name)=>window.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                        void Capture(Window target,string name){target.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(target);var b=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(target.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);b.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));Directory.CreateDirectory("artifacts");using var output=File.Create(Path.Combine("artifacts",name));png.Save(output);}
                        if(preview){Find<Slider>("Volume").Value=0;window.Title="轻读 · 液态玻璃界面验收";Call("PreviewOcrImage");Call("PreviewReading");return;}
                        await Task.Delay(200);Call("PreviewOcrImage");Call("PreviewReading");await Task.Delay(100);
                        if(Find<Image>("OriginalImage").Source is null||!Find<Border>("OriginalPanel").IsVisible)throw new Exception("Inline original-image comparison missing");
                        if(WindowChrome.GetWindowChrome(window)?.CaptionHeight<30)throw new Exception("Native draggable titlebar missing");
                        var captionPoint=window.PointToScreen(new Point(600,22));var hit=SendMessage(new WindowInteropHelper(window).Handle,0x84,IntPtr.Zero,new IntPtr(((int)captionPoint.Y<<16)|((int)captionPoint.X&0xffff)));
                        if(hit.ToInt32()!=2)throw new Exception("Titlebar native hit-test does not return HTCAPTION; dragging would fail");
                        Capture(window,"glass-main-light.png");
                        var readingArea=Find<Grid>("ReadingArea");var documentCard=(Border)readingArea.Children[0];
                        if(Math.Abs(Find<Border>("PlayerDock").ActualHeight-72)>.1)throw new Exception("Stitch footer is not 72 DIP");
                        if(Math.Abs(Find<DockPanel>("Header").ActualHeight-40)>.1)throw new Exception("Stitch utility toolbar is not 40 DIP");
                        if(readingArea.ActualWidth>1408.1||documentCard.ActualWidth>820.1)throw new Exception("Exported 1440/820 maximum widths exceeded");
                        if(Math.Abs(Find<Border>("OriginalPanel").ActualWidth-320)>.1)throw new Exception("Exported inspector is not 320 DIP");
                        if(Math.Abs(((Grid)documentCard.Child).Margin.Left-48)>.1)throw new Exception("Exported document padding is not 48 DIP");
                        object Region(FrameworkElement element){var origin=element.TransformToAncestor(window).Transform(new Point());return new{X=origin.X,Y=origin.Y,Width=element.ActualWidth,Height=element.ActualHeight};}
                        File.WriteAllText("artifacts/stitch-layout-metrics.json",System.Text.Json.JsonSerializer.Serialize(new{Viewport=new{Width=window.ActualWidth,Height=window.ActualHeight},Card=Region(documentCard),Inspector=Region(Find<Border>("OriginalPanel")),Toolbar=Region(Find<DockPanel>("Header")),Footer=Region(Find<Border>("PlayerDock"))},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                        Console.WriteLine("PASS exported document width/padding, inspector width and 72 DIP footer; layout metrics recorded");
                        if(!Find<Button>("ReadNavButton").IsVisible||!Find<Button>("HistoryNavButton").IsVisible)throw new Exception("Exported title navigation not visible");
                        foreach(var font in new[]{StitchAssets.Body,StitchAssets.Heading,StitchAssets.Symbols}){
                            var typeface=new Typeface(font,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
                            if(!typeface.TryGetGlyphTypeface(out var glyph)||!glyph.FontUri.ToString().Contains("Assets/Fonts",StringComparison.OrdinalIgnoreCase))throw new Exception("Stitch font fallback instead of packaged font: "+font.Source);
                        }
                        Console.WriteLine("PASS packaged Source Sans 3, Plus Jakarta Sans and Material Symbols glyph sources");
                        foreach(var name in new[]{"split_scene","code_blocks","neurology","file_download","file_upload"}){var label=StitchAssets.Icon(name);var face=new Typeface(label.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);if(!face.TryGetGlyphTypeface(out var glyph)||!glyph.CharacterToGlyphMap.ContainsKey(char.ConvertToUtf32(label.Text,0)))throw new Exception("Missing exact Material Symbols icon: "+name);}
                        Console.WriteLine("PASS exported title navigation");
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
                        checkpoint="voice dropdown";
                        var combo=Find<ComboBox>("Voice");combo.IsDropDownOpen=true;await Task.Delay(100);if(!combo.IsDropDownOpen||combo.Items.Count==0)throw new Exception("Glass voice dropdown failed");combo.IsDropDownOpen=false;
                        var picker=Find<VoicePicker>("Voice");
                        await Task.Delay(80);
                        if(picker.SelectionContent is not Grid||picker.ProviderElement!=Find<ComboBox>("Engine")||picker.IsDropDownOpen)throw new Exception("Footer voice tile/provider composition incorrect");
                        var providerPopup=(System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup",picker);
                        if(!Descendants(providerPopup.Child).Contains(Find<ComboBox>("Engine")))throw new Exception("Engine selector not owned by voice popup content");
                        picker.IsDropDownOpen=true;await Task.Delay(60);if(!Find<ComboBox>("Engine").IsVisible)throw new Exception("Voice popup does not expose engine selector");
                        Find<ComboBox>("Engine").SelectedIndex=2;await Task.Delay(60);
                        var voiceTitle=(TextBlock)window.GetType().GetField("voiceTileTitle",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        if(picker.Items.Count<100||!voiceTitle.Text.StartsWith("Edge"))throw new Exception("Combined voice control does not switch engine/catalogue");
                        picker.IsDropDownOpen=false;Find<ComboBox>("Engine").SelectedIndex=0;
                        Console.WriteLine("PASS combined voice tile, engine selector inside popup and live catalogue/caption switching");
                        Find<Slider>("Rate").Value=1.13;if((Find<ComboBox>("RateChoice").SelectedItem as ComboBoxItem)?.Content?.ToString()!="1.13×")throw new Exception("Custom rate shows empty selector");
                        Find<ComboBox>("RateChoice").SelectedIndex=2;if(Find<Slider>("Rate").Value!=1.25)throw new Exception("Rate preset not applied");
                        checkpoint="advanced tuning";Call("ToggleTuning");await Task.Delay(60);if(!Find<Border>("TuningPanel").IsVisible)throw new Exception("Advanced controls unavailable");Call("ToggleTuning");
                        Call("ToggleTransparency");if(GlassWindow.Apply(window,false,true))throw new Exception("Reduced transparency enabled acrylic");
                        if(((SolidColorBrush)window.FindResource("GlassBrush")).Color.A!=255)throw new Exception("Reduced transparency did not become opaque");Call("ToggleTransparency");
                        IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
                        checkpoint="debug window show";var debugWindow=new AudioDebugWindow(){Owner=window};debugWindow.Show();debugWindow.UpdateLayout();if(debugWindow.Content is not Border)throw new Exception("Debug window theme not applied");debugWindow.Close();
                        checkpoint="embedded history render";Call("ShowHistory");await Task.Delay(100);var history=window.OwnedWindows.OfType<NativeHistoryWindow>().Single();
                        var text=(TextBox)typeof(NativeHistoryWindow).GetField("text",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        var img=(Image)typeof(NativeHistoryWindow).GetField("image",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        Capture(window,"glass-history-light.png");
                        var master=(Grid)typeof(NativeHistoryWindow).GetField("historyMasterDetail",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        var layoutTolerance=1/VisualTreeHelper.GetDpi(window).DpiScaleX;
                        if(Math.Abs(master.ColumnDefinitions[1].ActualWidth-20)>layoutTolerance||Math.Abs(master.ColumnDefinitions[0].ActualWidth-(master.ActualWidth-40)/3)>layoutTolerance)throw new Exception($"History exported 4/8 columns not aligned: width={master.ActualWidth}, left={master.ColumnDefinitions[0].ActualWidth}, gap={master.ColumnDefinitions[1].ActualWidth}");
                        var formatButton=Descendants(history.Page).OfType<Button>().Single(x=>x.ToolTip?.ToString()=="Markdown");formatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var md=(FlowDocumentScrollViewer)typeof(NativeHistoryWindow).GetField("historyMarkdown",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
                        if(!md.IsVisible||text.IsVisible)throw new Exception("History Markdown preview did not switch");formatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Console.WriteLine("PASS history source toolbar/master-detail proportions and Markdown/plain switching");
                        if(history.IsVisible||!text.IsVisible||!img.IsVisible||text.ActualWidth<150||img.ActualHeight<20)throw new Exception("Embedded history text/original not visible");Call("BackToReading");history.Close();
                        var settingsButton=(Button)window.GetType().GetField("settingsNav",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        using var fakeReading=new CancellationTokenSource();
                        var readingField=window.GetType().GetField("reading",BindingFlags.Instance|BindingFlags.NonPublic)!;
                        readingField.SetValue(window,fakeReading);
                        checkpoint="preferences navigation while reading";settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                        var pageHost=(ContentControl)window.GetType().GetField("pageHost",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        if(!pageHost.IsVisible||pageHost.Content is null||fakeReading.IsCancellationRequested)throw new Exception("Preferences blocked during reading or opening canceled playback");
                        checkpoint="embedded model editor";
                        var modelChoice=Descendants((DependencyObject)pageHost.Content).OfType<ComboBox>().Single(x=>x.IsEditable);modelChoice.ApplyTemplate();
                        var modelEdit=modelChoice.Template.FindName("PART_EditableTextBox",modelChoice) as TextBox??throw new Exception("Model ID editable combo lost native editor");
                        modelEdit.Text="ep-ui-test";if(modelChoice.Text!="ep-ui-test")throw new Exception("Embedded model ID editor binding broken");
                        if(!WindowChrome.GetIsHitTestVisibleInChrome(settingsButton))throw new Exception("Preferences title button not hit-testable");
                        readingField.SetValue(window,null);
                        Call("BackToReading");settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                        if(!pageHost.IsVisible)throw new Exception("Repeat preferences navigation failed");
                        if(!Descendants((DependencyObject)pageHost.Content).OfType<Button>().Any(x=>x.ToolTip?.ToString()=="API 调试"))throw new Exception("Repeat preferences lost diagnostic actions");
                        Console.WriteLine("PASS preferences click while reading, retained task, repeat navigation and caption input");
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
                        if(Find<TextBox>("Editor").Text.Length!=0||!Find<RichTextBox>("ReadingView").IsVisible||focusRuns.Count!=0)throw new Exception("Reading-mode clear did not clear and retain preview");
                        Console.WriteLine("PASS reading-mode clear preserves preview and clears text/runs");
                        Call("PreviewOcrImage");await Task.Delay(60);
                        var placeholder=(StackPanel)window.GetType().GetField("stitchImagePlaceholder",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        if(!Find<Border>("OriginalPanel").IsVisible||!placeholder.IsVisible)throw new Exception("Filter inspector cannot open without an image");
                        Call("CloseOriginal");
                        Console.WriteLine("PASS original-image empty state and text-only filter drawer");
                        var tuningPopups=(List<System.Windows.Controls.Primitives.Popup>)window.GetType().GetField("tuningPopups",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        tuningPopups[1].IsOpen=true;await Task.Delay(80);
                        if(!Find<Slider>("Volume").IsVisible)throw new Exception("Volume popup slider not visible");
                        Find<Slider>("Volume").Value=.7;if(!Find<TextBlock>("VolumeLabel").Text.Contains("70"))throw new Exception("Volume popup not connected to tuning values");
                        tuningPopups[1].IsOpen=false;Find<Slider>("Volume").Value=1;
                        Console.WriteLine("PASS compact volume popup uses actual tuning slider and live labels");
                        var paste=window.GetType().GetMethod("PasteClipboard",BindingFlags.Instance|BindingFlags.NonPublic)!;
                        await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"# 标题\n\n**重点**正文。")})!;
                        if(!Find<RichTextBox>("ReadingView").IsVisible||Find<TextBox>("Editor").IsVisible)throw new Exception("Markdown paste did not enter preview");
                        await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,"普通文本。")})!;
                        if(!Find<TextBox>("Editor").IsVisible)throw new Exception("Plain paste not editable");
                        if(Find<Button>("CaretButton").IsVisible)throw new Exception("Caret button should be replaced by text context menu");
                        Console.WriteLine("PASS Markdown paste previews, plain paste edits, caret action moved to context menu");
                        Call("TogglePinned");if(!window.Topmost)throw new Exception("Pin did not enable topmost");
                        Call("Compact");Call("Compact");if(!window.Topmost)throw new Exception("Compact restore lost pin");
                        Call("TogglePinned");if(window.Topmost)throw new Exception("Unpin did not disable topmost");
                        Console.WriteLine("PASS pin/unpin and pin preservation across compact mode");
                        var ruleControls=(Dictionary<string,CheckBox>)window.GetType().GetField("inspectorRules",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        ruleControls["SkipLinks"].IsChecked=false;
                        if(ReaderState.Load(profile).ReadingPreferences.SkipLinks)throw new Exception("Inspector link rule did not persist");
                        ruleControls["SkipLinks"].IsChecked=true;
                        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
                        var embedded=(SettingsWindow)window.GetType().GetField("embeddedSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        var codeSwitch=Descendants(embedded.Page).OfType<CheckBox>().Single(x=>x.Name=="SkipCode");codeSwitch.IsChecked=false;
                        if(embedded.ResultPreferences.SkipCode)throw new Exception("Settings filter switch ignored");
                        var sectionButtons=(Dictionary<string,Button>)typeof(SettingsWindow).GetField("sectionButtons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(embedded)!;
                        window.WindowState=WindowState.Normal;window.Width=1180;window.Height=800;await Task.Delay(150);
                        sectionButtons["filters"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Capture(window,"stitch-settings-filters.png");
                        sectionButtons["window"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                        var themeButtons=Descendants(embedded.Page).OfType<Button>().Where(x=>x.Tag is string mode&&mode is "Light" or "Dark" or "System").ToList();
                        themeButtons.Single(x=>(string)x.Tag=="Dark").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var currentState=(ReaderState)window.GetType().GetField("state",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        if(!currentState.Dark||currentState.ThemeMode!="Dark")throw new Exception("Settings theme selector ignored");
                        themeButtons.Single(x=>(string)x.Tag=="Light").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(currentState.Dark||ReaderState.Load(profile).ThemeMode!="Light")throw new Exception("Settings theme did not persist");
                        var reduced=Descendants(embedded.Page).OfType<CheckBox>().Single(x=>x.Name=="ReducedTransparency");reduced.IsChecked=true;
                        if(!ReaderState.Load(profile).ReducedTransparency)throw new Exception("Settings transparency switch ignored");reduced.IsChecked=false;
                        Capture(window,"stitch-settings-appearance.png");
                        Call("BackToReading");
                        Console.WriteLine("PASS inspector switch persistence and settings draft filter switches");
                        Console.WriteLine("PASS settings theme selection, transparency controls and encrypted appearance persistence");
                        Find<TextBox>("Editor").Text="# 高并发图形管线中的半角正弦近似算法\n\n在现代三维游戏着色器中，采用预计算查找表减少低频指令单元的硬件除法调用。\n\n在此次优化实践中，我们重新推导了半角正弦公式的数值边界，消除了寄存器溢出风险。\n\n> 专业术语 CosHalfAngleSq 在行内仍正常朗读。\n\n```hlsl\nfloat FastHalfAngle(float value) {\n    return saturate(value * 0.5f + 0.5f);\n}\n```\n\n在多轮压力测试下，上述近似算法保持了数值一致性。";
                        Call("PreviewReading");report.Invoke(window,new object[]{Find<TextBox>("Editor").Text.IndexOf("半角正弦公式",StringComparison.Ordinal),6});
                        window.WindowState=WindowState.Normal;window.Width=1180;window.Height=800;await Task.Delay(150);Capture(window,"stitch-reader-aligned.png");
                        // Completion is signaled only after the STA application has shut down.
                        checksPassed=true;
                    }catch(Exception ex){done.TrySetException(ex);}finally{if(!preview){window.Close();app.Shutdown();}}
                };
                app.Run(window);
                checkpoint="STA shutdown cleanup";
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                if(!preview&&!checksPassed&&!done.Task.IsFaulted)throw new Exception("UI application closed before all assertions completed");
                done.TrySetResult();
            }catch(Exception ex){done.TrySetException(ex);}
        };
        if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA){runUi();await done.Task;}
        else{
            var thread=new Thread(()=>runUi());thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(preview?900:45));if(!thread.Join(10000))throw new Exception("UI test STA did not exit after shutdown");
        }
    }
}
