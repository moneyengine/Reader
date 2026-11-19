using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LightReader;

static class MarkdownReplacementChecks
{
    const string Article="**缺陷原因**\\\n地图标记控件创建后，位置更新先于排队的表现初始化执行，导致蓝图默认电脑图标和 `12:05` 倒计时提前显示，被误认为未刷新的任务机位置。同时，CanvasSlot 居中对齐在延迟回调中才设置，造成首次开图时标记位置跳动。\n\n**修改方法**\\\n创建及回收时隐藏未初始化的图标和倒计时，保留排队初始化，正确图标异步加载完成后再显示；统一图片加载状态管理，修复重复请求计数和 Construct 清除有效加载状态的问题。在位置更新前设置居中对齐，避免延迟修改 Alignment 导致位置跳动。\n";
    public static async Task Run()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderReplacementTests",Guid.NewGuid().ToString("N"),"state.dat");
        new ReaderState{Text=Article}.Save(profile);
        var thread=new Thread(()=>{
            var app=new Application();var window=new NativeReaderWindow(profile);
            window.Loaded+=async (_,_)=>{
                await Task.Yield();
                try{
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    var editor=(TextBox)window.FindName("Editor");var view=(RichTextBox)window.FindName("ReadingView");
                    var paste=window.GetType().GetMethod("PasteClipboard",flags)!;
                    void Render()=>window.GetType().GetMethod("RenderText",flags)!.Invoke(window,null);
                    void SelectAll(){window.Activate();Keyboard.Focus(view);view.SelectAll();}
                    async Task Paste(string text)=>await (Task)paste.Invoke(window,new object[]{new DataObject(DataFormats.UnicodeText,text)})!;
                    async Task Settled()=>await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                    string Visible()=>new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;
                    if(editor.Text!=Article||!Visible().TrimEnd().EndsWith("避免延迟修改 Alignment 导致位置跳动。"))throw new Exception("Original article rendering changed source/tail");
                    SelectAll();EditingCommands.Delete.Execute(null,view);await Settled();
                    if(editor.Text!="")throw new Exception("Select-all delete left hidden Markdown: "+editor.Text);
                    view.Undo();await Settled();if(editor.Text!=Article)throw new Exception("Undo full deletion lost original Markdown");
                    view.Redo();await Settled();if(editor.Text!="")throw new Exception("Redo full deletion retained Markdown");
                    await Paste(Article);
                    if(editor.Text!=Article)throw new Exception("Paste after deletion left markup: "+editor.Text);
                    for(int i=0;i<3;i++){
                        SelectAll();await Paste(Article);
                        if(editor.Text!=Article)throw new Exception("Repeated select-all paste left markup: "+editor.Text);
                    }
                    foreach(var text in new[]{"**最后一句。**","`12:05`","[链接](https://example.com)","# 标题\n\n**内容。**","```text\n流程正文\n```","| 项目 | 内容 |\n| --- | --- |\n| 中文 | **重点** |"}){
                        editor.Text=text;Render();await Settled();SelectAll();EditingCommands.Delete.Execute(null,view);await Settled();
                        if(editor.Text!=""){
                            var mapping=window.GetType().GetField("readingDocument",flags)!.GetValue(window)!;
                            throw new Exception("Deleting formatted source left delimiters: "+text+" -> "+editor.Text+" visible=["+Visible()+"] mapped=["+mapping.GetType().GetField("visible",flags)!.GetValue(mapping)+"] pending=["+window.GetType().GetField("pendingReadingText",flags)!.GetValue(window)+"] updating="+window.GetType().GetField("updatingReadingDocument",flags)!.GetValue(window));
                        }
                        view.Undo();await Settled();if(editor.Text!=text)throw new Exception("Undo formatted deletion lost syntax: "+text+" source=["+editor.Text+"] visible=["+Visible()+"] canUndo="+view.CanUndo);
                        SelectAll();await Paste(Article);
                        if(editor.Text!=Article)throw new Exception("Replacing formatted source left delimiters: "+text+" -> "+editor.Text);
                        SelectAll();EditingCommands.Delete.Execute(null,view);await Settled();
                        if(editor.Text!="")throw new Exception("Deleting replaced article retained hidden source");
                    }
                    Console.WriteLine("PASS exact user article, select-all delete/paste, repeated replacement and formatted document boundaries");
                    editor.Text=Article;Render();view.UpdateLayout();
                    var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(window);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts");using(var file=File.Create("artifacts/markdown-replacement-fixed.png"))encoder.Save(file);
                    await Task.Delay(700);if(ReaderState.Load(profile).Text!=Article)throw new Exception("Autosave changed article");
                    done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}
                finally{window.Close();}
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(30));if(!thread.Join(3000))throw new Exception("Replacement test STA did not exit");
    }
}
