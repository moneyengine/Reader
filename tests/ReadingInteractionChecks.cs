using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using LightReader;

static class ReadingInteractionChecks
{
    public static async Task Run()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderInteractionTests",Guid.NewGuid().ToString("N"),"state.dat");
        var thread=new Thread(()=>{
            var app=new Application();var window=new NativeReaderWindow(profile);
            window.Loaded+=async (_,_)=>{
                await Task.Yield();
                try{
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    T Find<T>(string name) where T:class=>(T)window.FindName(name);
                    void Call(string name,params object[] args)=>window.GetType().GetMethod(name,flags)!.Invoke(window,args);
                    object? Field(string name)=>window.GetType().GetField(name,flags)!.GetValue(window);
                    var editor=Find<TextBox>("Editor");var view=Find<RichTextBox>("ReadingView");
                    editor.Text="# 标题\n\n**重点**正文。\n\n```text\n发现问题\n修复问题\n```\n\n末尾。";
                    Call("PreviewReading",window,new RoutedEventArgs());
                    if(view.IsReadOnly||Find<Button>("CaretButton").IsVisible)throw new Exception("Reading mode not editable/context-only");
                    var runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;
                    var first=runs.First(x=>x.Run.Text=="重");
                    var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=ContentElement.MouseLeftButtonDownEvent};
                    first.Run.RaiseEvent(click);
                    if(click.Handled||Field("reading") is not null)throw new Exception("Left click still starts speech or blocks selection");
                    var second=runs.First(x=>x.Run.Text=="点");
                    view.Selection.Select(first.Run.ContentStart,second.Run.ContentEnd);
                    if(view.Selection.Text!="重点")throw new Exception("Reading text cannot be selected");
                    view.Selection.Text="关键";
                    await Task.Delay(30);
                    if(!editor.Text.Contains("**关键**正文。")||!editor.Text.Contains("```text\n发现问题\n修复问题\n```"))throw new Exception("Rendered edit damaged Markdown or fenced flow: "+editor.Text);
                    Call("PreviewReading",window,new RoutedEventArgs());
                    var document=(ReadingDocument)Field("readingDocument")!;runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;
                    var tail=runs.First(x=>x.Run.Text=="末");
                    if(document.SourcePosition(view.Document,tail.Run.ContentStart)!=editor.Text.IndexOf("末尾",StringComparison.Ordinal))throw new Exception("Source mapping incorrect after edit");
                    var original=editor.Text;
                    view.Focus();view.Selection.Select(tail.Run.ContentStart,tail.Run.ContentEnd);EditingCommands.Delete.Execute(null,view);
                    await Task.Delay(30);
                    if(editor.Text!=original.Replace("末尾","尾"))throw new Exception("Second rendered edit offset stale");
                    view.Undo();await Task.Delay(30);if(editor.Text!=original)throw new Exception("Rich-text undo did not restore source: "+editor.Text);
                    view.Redo();await Task.Delay(30);if(editor.Text!=original.Replace("末尾","尾"))throw new Exception("Rich-text redo did not restore source");
                    Console.WriteLine("PASS selection, rendered edits, Markdown/fenced-flow preservation, source mapping and undo/redo");
                    editor.Text="**重点**正文。";Call("PreviewReading",window,new RoutedEventArgs());
                    runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;
                    view.Selection.Select(runs.First(x=>x.Run.Text=="点").Run.ContentStart,runs.First(x=>x.Run.Text=="文").Run.ContentEnd);
                    view.Selection.Text="新";await Task.Delay(30);
                    if(editor.Text!="**重新**。")throw new Exception("Cross-format edit damaged hidden delimiters: "+editor.Text);
                    Console.WriteLine("PASS cross-format selection replacement preserves Markdown delimiters");
                    editor.Text=string.Join("\n\n",Enumerable.Range(0,100).Select(i=>$"第{i}段，这是滚动和朗读定位测试。"));
                    Call("PreviewReading",window,new RoutedEventArgs());await Task.Delay(120);view.UpdateLayout();
                    var fake=new CancellationTokenSource();window.GetType().GetField("reading",flags)!.SetValue(window,fake);
                    Call("ReportPosition",0,1);await Task.Delay(50);
                    var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent};view.RaiseEvent(wheel);
                    view.ScrollToVerticalOffset(700);await Task.Delay(80);var manual=view.VerticalOffset;
                    if(manual<100||wheel.Handled)throw new Exception("Native wheel scrolling blocked");
                    Call("ReportPosition",editor.Text.IndexOf("第80段",StringComparison.Ordinal),1);await Task.Delay(100);
                    if(Math.Abs(view.VerticalOffset-manual)>1)throw new Exception($"Speech interrupted manual scrolling: {manual} -> {view.VerticalOffset}");
                    await Task.Delay(900);view.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
                    await Task.Delay(1200);if(Math.Abs(view.VerticalOffset-manual)>1)throw new Exception("Wheel idle deadline not restarted");
                    await Task.Delay(1100);if(view.VerticalOffset<=manual+100)throw new Exception("Two-second idle did not restore latest speech position");
                    Console.WriteLine("PASS native scrolling, uninterrupted speech position updates, restarted two-second deadline and automatic return");
                    Call("ReadingMouseWheel",view,new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120));
                    view.ScrollToVerticalOffset(500);Call("Stop",window,new RoutedEventArgs());await Task.Delay(2200);
                    fake.Dispose();
                    if(Math.Abs(view.VerticalOffset-500)>1)throw new Exception("Stopped playback still auto-scrolls");
                    Call("PreviewReading",window,new RoutedEventArgs());
                    var offset=editor.Text.IndexOf("第50段",StringComparison.Ordinal);
                    window.GetType().GetField("contextPosition",flags)!.SetValue(window,offset);
                    Find<Slider>("Volume").Value=0;
                    var menu=(MenuItem)view.ContextMenu.Items[0];
                    if(menu.Header?.ToString()!="从这里开始朗读"||editor.ContextMenu.Items.OfType<MenuItem>().First().Header?.ToString()!=menu.Header.ToString())throw new Exception("Read-here context menu missing");
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(250);
                    if(Field("reading") is null||(int)Field("position")!<offset)throw new Exception("Context command did not start at target source offset");
                    runs=(List<(int Start,int Length,Run Run)>)Field("runs")!;
                    var editRun=runs.First(x=>x.Run.Text=="第");view.Selection.Select(editRun.Run.ContentStart,editRun.Run.ContentEnd);view.Selection.Text="新";
                    await Task.Delay(30);
                    if(Field("reading") is not null||!editor.Text.StartsWith("新0段"))throw new Exception("Editing during speech did not safely stop and synchronize");
                    await Task.Delay(700);if(ReaderState.Load(profile).Text!=editor.Text)throw new Exception("Reading-mode edit not persisted");
                    Console.WriteLine("PASS read-here context action, stop cancels scroll timer, edit during speech cancels safely and encrypted autosave");
                    done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}
                finally{window.Close();}
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(35));thread.Join(3000);
    }
}
