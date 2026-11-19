using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using LightReader;

static class ReadingTypographyChecks
{
    const string Article="明白，是 `HideWhenOutOfRange` **超出原范围 10 米后才隐藏**。应修改 **MiniMap Module 的激活／回收距离**，不是 `CalculatePosition()`。\n\n当前 Module 会按世界距离判断是否激活，超范围就调用 `RecycleSubWidgetByID()`。两处入口都要改：\n\n- [RefreshAllSubWidget](https://example.com/RefreshAllSubWidget)\n- [RefreshSubWidget](https://example.com/RefreshSubWidget)\n\n### 最小方案\n\n如果所有 `HideWhenOutOfRange` 都统一延长 10 米，把这两处的距离判断改为：\n\n```cpp\nconst float ExtraWorldDistance =\n    Data.DisplayMode == ESubWidgetDisplayMode::HideWhenOutOfRange\n        ? 1000.0f : 0.0f; // 10 meters, in centimeters\n\nconst float HideRadius = MiniMapDisplayRadius + ExtraWorldDistance;\nbNewActivated = DistSquared <= FMath::Square(HideRadius);\n```\n\n例如小地图当前显示半径是 50 米，图标到地图中心的距离超过 60 米才回收。这里直接使用世界距离，**不需要转换成地图 UI 距离，也不需要改共享的** `Config.ViewRadius`。\n\n### 如果每个图标需要单独配置\n\n在 FSubWidgetBaseData 增加：\n\n```cpp\n// Extra range in world centimeters.\nfloat HideRangeWorldOffset = 0.0f;\n```\n\n然后把上面固定的 `1000.0f` 替换成 `Data.HideRangeWorldOffset`，目标图标的数据设为 `1000` 即可。";
    public static async Task Run()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderTypographyTests",Guid.NewGuid().ToString("N"),"state.dat");
        new ReaderState{Text=Article,FontSize=18}.Save(profile);
        var thread=new Thread(()=>{
            var app=new Application();var window=new NativeReaderWindow(profile){Width=1180,Height=1050};
            window.Loaded+=async (_,_)=>{
                await Task.Yield();
                try{
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    var editor=(TextBox)window.FindName("Editor");var view=(RichTextBox)window.FindName("ReadingView");
                    void Call(string method,params object[] args)=>window.GetType().GetMethod(method,flags)!.Invoke(window,args);
                    IEnumerable<DependencyObject> Children(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}}
                    void Capture(string name){window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts");using var file=File.Create(Path.Combine("artifacts",name));png.Save(file);}
                    if(Math.Abs(view.Document.LineHeight-27)>.01)throw new Exception("Body line height did not tighten");
                    var list=view.Document.Blocks.OfType<System.Windows.Documents.List>().Single();
                    if(list.ListItems.Any(item=>item.Blocks.OfType<Paragraph>().Any(p=>p.Margin.Bottom>1)))throw new Exception("Tight list retains paragraph gaps");
                    var blocks=view.Document.Blocks.OfType<Section>().ToList();
                    if(blocks.Count!=2||blocks.Any(block=>block.Background is not SolidColorBrush brush||brush.Color.R<220))throw new Exception("Code blocks not neutral light surfaces");
                    var codeParagraph=(Paragraph)blocks[0].Blocks.LastBlock;
                    if(codeParagraph.FontFamily!=StitchAssets.Code||Math.Abs(codeParagraph.FontSize-15.3)>.01)throw new Exception("Code/body sizing or monospace wrong");
                    var runs=(List<(int Start,int Length,Run Run)>)window.GetType().GetField("runs",flags)!.GetValue(window)!;
                    var mapping=(ReadingDocument)window.GetType().GetField("readingDocument",flags)!.GetValue(window)!;
                    foreach(var entry in runs)if(Article.Substring(entry.Start,entry.Length)!=entry.Run.Text||mapping.SourcePosition(view.Document,entry.Run.ContentStart)!=entry.Start)throw new Exception("Styled source mapping changed: "+entry.Run.Text);
                    var syntaxKeys=runs.Where(x=>x.Run.Tag as string=="programming").Select(x=>x.Run.GetValue(MarkdownReading.ForegroundKeyProperty)).Distinct().ToList();
                    if(syntaxKeys.Count<4)throw new Exception("Syntax token categories missing: "+string.Join(",",syntaxKeys));
                    var inline=runs.First(x=>x.Run.Text=="HideWhenOutOfRange").Run.Parent as Span;
                    if(inline?.FontFamily!=StitchAssets.Code||inline.Background is not SolidColorBrush)throw new Exception("Inline code has no monospace/background");
                    await Task.Delay(100);view.UpdateLayout();Capture("reading-typography-light.png");
                    var visibleCopy=Children(view).OfType<Button>().FirstOrDefault(x=>x.ToolTip?.ToString()=="复制代码");
                    if(visibleCopy is null||visibleCopy.ActualWidth<24)throw new Exception("Code copy icon clipped: "+visibleCopy?.ActualWidth);
                    var scroll=Children(view).OfType<ScrollBar>().FirstOrDefault(x=>x.Orientation==Orientation.Vertical&&x.IsVisible);
                    if(scroll is null||Math.Abs(scroll.ActualWidth-10)>.1)throw new Exception("Reader scrollbar not 10 DIP: "+scroll?.ActualWidth);
                    Console.WriteLine("PASS compact body/lists, inline code, neutral code surfaces, syntax colors and thin scrollbar");

                    // Exercise the renderer callbacks without changing the system clipboard or launching a browser.
                    Uri? opened=null;string? copied=null;
                    var detachedRuns=new List<(int Start,int Length,Run Run)>();
                    var detached=MarkdownReading.Render(Article,18,Brushes.Black,_=>{},detachedRuns,navigate:uri=>opened=uri,copy:text=>copied=text);
                    var link=detached.Blocks.OfType<System.Windows.Documents.List>().Single().ListItems.FirstListItem.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Hyperlink>().Single();
                    link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri,"_blank"){RoutedEvent=Hyperlink.RequestNavigateEvent});
                    if(opened?.Host!="example.com")throw new Exception("Link navigation callback missing");
                    var heading=(Paragraph)detached.Blocks.OfType<Section>().First().Blocks.FirstBlock;
                    var copyButton=(Button)((BlockUIContainer)heading.Inlines.OfType<Floater>().Single().Blocks.FirstBlock).Child;
                    copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var expected="const float ExtraWorldDistance =\n    Data.DisplayMode == ESubWidgetDisplayMode::HideWhenOutOfRange\n        ? 1000.0f : 0.0f; // 10 meters, in centimeters\n\nconst float HideRadius = MiniMapDisplayRadius + ExtraWorldDistance;\nbNewActivated = DistSquared <= FMath::Square(HideRadius);";
                    if(copied!=expected)throw new Exception("Copy button does not preserve exact code text");
                    Console.WriteLine("PASS link/copy callbacks, exact code copy and unchanged source positions");

                    Call("ReportPosition",Article.IndexOf("当前 Module",StringComparison.Ordinal),1);Call("Stop",window,new RoutedEventArgs());
                    var keyword=runs.First(x=>x.Run.Text=="const"&&x.Run.Tag as string=="programming").Run;
                    if(keyword.Foreground!=MarkdownReading.BaseInk(keyword,Brushes.Black))throw new Exception("Sentence focus destroyed syntax styling");
                    Call("ToggleTheme",window,new RoutedEventArgs());await Task.Delay(50);Capture("reading-typography-dark.png");
                    if(keyword.Foreground is not SolidColorBrush foreground||foreground.Color.R<120)throw new Exception("Dark syntax foreground not updated");
                    Call("ToggleTheme",window,new RoutedEventArgs());
                    var fake=new CancellationTokenSource();window.GetType().GetField("reading",flags)!.SetValue(window,fake);
                    Call("ReportPosition",Article.IndexOf("例如小地图",StringComparison.Ordinal),1);var position=(int)window.GetType().GetField("position",flags)!.GetValue(window)!;
                    Call("SetFont",20.0);
                    if(fake.IsCancellationRequested||((int)window.GetType().GetField("position",flags)!.GetValue(window)!)!=position||Math.Abs(view.Document.LineHeight-30)>.01)throw new Exception("Font change canceled speech or lost position");
                    Call("Stop",window,new RoutedEventArgs());fake.Dispose();
                    window.Width=820;window.Height=600;await Task.Delay(80);view.ScrollToHome();await Task.Delay(30);Capture("reading-typography-narrow.png");
                    view.ScrollToEnd();await Task.Delay(50);Capture("reading-typography-narrow-code.png");
                    if(editor.Text!=Article)throw new Exception("Visual changes mutated source");
                    window.Activate();view.Focus();
                    runs=(List<(int Start,int Length,Run Run)>)window.GetType().GetField("runs",flags)!.GetValue(window)!;
                    var first=runs.First();view.Selection.Select(first.Run.ContentStart,first.Run.ContentEnd);EditingCommands.Delete.Execute(null,view);await Task.Delay(40);
                    if(editor.Text==Article||!view.CanUndo)throw new Exception("Native edit/undo unavailable");
                    var existing=view.Document;Call("SetFont",22.0);
                    if(!ReferenceEquals(view.Document,existing)||!view.CanUndo)throw new Exception("Font resizing discarded document/undo history");
                    for(int i=0;i<20&&editor.Text!=Article&&view.CanUndo;i++){view.Undo();await Task.Delay(20);}
                    if(editor.Text!=Article)throw new Exception("Cannot undo edit after font resize");
                    Console.WriteLine("PASS syntax after focus/theme changes, live font sizing, narrow layout and verbatim source");
                    done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}
                finally{window.Close();}
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(30));if(!thread.Join(3000))throw new Exception("Typography STA did not exit");
    }
}
