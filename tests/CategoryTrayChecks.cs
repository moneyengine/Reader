using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightReader;

static class CategoryTrayChecks
{
    public static void Model()
    {
        var edge=EdgeTtsClient.Choices();var entries=VoiceCategories.Describe("Edge",edge);
        var tree=VoiceCategories.Build(entries,"","zh-CN-XiaoxiaoNeural");
        var all=tree.SelectMany(x=>x.Descendants()).Where(x=>x.Voice is not null).ToList();
        if(all.Count!=edge.Count||all.Select(x=>x.Voice!.Id).Distinct().Count()!=edge.Count)throw new Exception("Category tree lost or duplicated voices");
        var chinese=tree.Single(x=>x.Label=="中文");var female=chinese.Children.Single(x=>x.Label=="普通话（中国大陆）").Children.Single(x=>x.Label=="女声");
        if(!female.Children.Any(x=>x.Voice?.Id=="zh-CN-XiaoxiaoNeural")||!female.IsExpanded)throw new Exception("Chinese/region/gender hierarchy missing");
        var filtered=VoiceCategories.Build(entries,"中文 女声",null).SelectMany(x=>x.Descendants()).Where(x=>x.Voice is not null).ToList();
        if(filtered.Count==0||filtered.Any(x=>!x.Path.Contains("中文")||!x.Path.Contains("女声")))throw new Exception("Category path search failed");
        if(VoiceCategories.Build(entries,"unmatched-no-voice-123",null).Count!=0)throw new Exception("Empty search not handled");
        var cloud=VoiceCatalog.Choices("测试自定义|my-custom");var cloudTree=VoiceCategories.Build(VoiceCategories.Describe("Cloud",cloud),"",null);
        var cloudLeaves=cloudTree.SelectMany(x=>x.Descendants()).Where(x=>x.Voice is not null).ToList();
        if(cloudLeaves.Count!=cloud.Count||!cloudTree.Any(x=>x.Label=="自定义音色")||cloudLeaves.Where(x=>!x.Voice!.Compatible).Any(x=>x.Selectable))throw new Exception("Cloud/custom/unsupported hierarchy broken");
        Console.WriteLine("PASS Edge language/region/gender hierarchy, current path expansion, full catalogue preservation, multiword category search, empty state, cloud categories/custom and unsupported voice protection");
    }
    public static async Task Native()
    {
        Model();var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path=Path.Combine(Path.GetTempPath(),"LightReaderCategoryTest",Guid.NewGuid().ToString("N"),"state.dat");new ReaderState{Text="分类与托盘测试。"}.Save(path);
        var thread=new Thread(()=>{
            try{
                var app=new Application();var window=new NativeReaderWindow(path);
                window.Loaded+=async(_,_)=>{
                    try{
                        T Find<T>(string name)where T:class=>(T)window.FindName(name);
                        Find<ComboBox>("Engine").SelectedIndex=2;var picker=Find<VoicePicker>("Voice");var selected=(VoiceChoice)picker.SelectedItem;
                        var opening=System.Diagnostics.Stopwatch.StartNew();picker.IsDropDownOpen=true;picker.UpdateLayout();
                        await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                        opening.Stop();Console.WriteLine($"PERF actual Edge catalogue first popup: {opening.Elapsed.TotalMilliseconds:F1} ms ({picker.Items.Count} voices)");
                        await Task.Delay(150);
                        var popup=(Popup)picker.Template.FindName("PART_Popup",picker);
                        IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
                        var tree=Descendants(popup.Child).OfType<TreeView>().Single();tree.UpdateLayout();
                        if(!VirtualizingPanel.GetIsVirtualizing(tree)||VirtualizingPanel.GetVirtualizationMode(tree)!=VirtualizationMode.Recycling||!ScrollViewer.GetCanContentScroll(tree))throw new Exception("Tree virtualization disabled");
                        if(Descendants(picker).OfType<ComboBoxItem>().Any())throw new Exception("Hidden flat voice list was realized");
                        if(window.Icon is null)throw new Exception("Window icon missing");
                        var timings=new List<double>();
                        for(int repeat=0;repeat<5;repeat++){
                            picker.IsDropDownOpen=false;opening.Restart();picker.IsDropDownOpen=true;popup.Child.UpdateLayout();
                            await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                            opening.Stop();timings.Add(opening.Elapsed.TotalMilliseconds);
                        }
                        Console.WriteLine($"PERF actual Edge catalogue repeated popup: average {timings.Average():F1} ms, max {timings.Max():F1} ms");
                        var root=tree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem??throw new Exception("No tree category UI");root.IsSelected=true;
                        if(!picker.IsDropDownOpen||((VoiceChoice)picker.SelectedItem).Id!=selected.Id)throw new Exception("Clicking category changed voice or closed popup");
                        picker.Filter("中文 女声");await Task.Delay(100);if(((VoiceChoice)picker.SelectedItem).Id!=selected.Id)throw new Exception("Filtering erased current voice");
                        tree.UpdateLayout();
                        TreeViewItem? Leaf(ItemsControl owner,string id){foreach(var item in owner.Items){var node=(VoiceCategory)item;var container=owner.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;if(container is null)continue;if(node.Voice?.Id==id)return container;container.IsExpanded=true;container.UpdateLayout();if(Leaf(container,id) is {} found)return found;}return null;}
                        var target=Leaf(tree,"zh-CN-XiaoyiNeural")??throw new Exception("Target leaf not rendered");target.IsSelected=true;await Task.Delay(100);
                        if(picker.IsDropDownOpen||((VoiceChoice)picker.SelectedItem).Id!="zh-CN-XiaoyiNeural")throw new Exception("Leaf selection did not commit or close");
                        picker.Filter("no-match-123");if(picker.Categories.Count!=0||((VoiceChoice)picker.SelectedItem).Id!="zh-CN-XiaoyiNeural")throw new Exception("Empty search changed selected voice");
                        picker.Filter("");picker.IsDropDownOpen=true;await Task.Delay(100);picker.CollapseCategories();if(picker.Categories.Any(x=>x.IsExpanded))throw new Exception("Collapse categories failed");
                        picker.IsDropDownOpen=false;picker.IsDropDownOpen=true;await Task.Delay(100);
                        await window.Dispatcher.InvokeAsync(()=>popup.Child.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render);

                        var b=new RenderTargetBitmap((int)Math.Ceiling(popup.Child.RenderSize.Width*1.5),(int)Math.Ceiling(popup.Child.RenderSize.Height*1.5),144,144,PixelFormats.Pbgra32);b.Render(popup.Child);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(b));Directory.CreateDirectory("artifacts");using(var file=File.Create("artifacts/voice-categories.png"))enc.Save(file);picker.IsDropDownOpen=false;
                        picker.IsDropDownOpen=true;await Task.Delay(50);
                        var inlineSearch=Descendants(popup.Child).OfType<TextBox>().Single(x=>x.Tag?.ToString()=="搜索音色、语言、地区或分类");inlineSearch.Text="中文 ";if(inlineSearch.Text!="中文 "||Find<TextBox>("VoiceSearch").Text!="中文 ")throw new Exception("Typing space in category search was lost");
                        inlineSearch.Text="中文 女声";if(picker.Categories.SelectMany(x=>x.Descendants()).Where(x=>x.Voice is not null).Any(x=>!x.Path.Contains("女声")))throw new Exception("Popup multiword search failed");picker.Filter("");picker.IsDropDownOpen=false;
                        var savedVoices=picker.ItemsSource;var synthetic=Enumerable.Range(0,10000).Select(i=>new VoiceChoice($"Stress voice {i}",$"stress-{i}")).ToList();
                        picker.ItemsSource=synthetic;picker.SelectedIndex=0;picker.SetCatalogue("Cloud",synthetic);picker.Filter("Stress");
                        opening.Restart();picker.IsDropDownOpen=true;popup.Child.UpdateLayout();
                        await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);opening.Stop();
                        var realized=Descendants(popup.Child).OfType<TreeViewItem>().Count();
                        if(realized>150)throw new Exception($"10000 voice catalogue realized {realized} containers instead of viewport only");
                        var scroll=Descendants(tree).OfType<ScrollViewer>().First();scroll.ScrollToEnd();tree.UpdateLayout();
                        await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                        if(!Descendants(tree).OfType<TreeViewItem>().Any(x=>(x.DataContext as VoiceCategory)?.Voice?.Id=="stress-9999"))throw new Exception("Virtualized list cannot reach last voice");
                        Console.WriteLine($"PERF 10000 expanded voices: {opening.Elapsed.TotalMilliseconds:F1} ms, {realized} realized containers; last voice reachable");
                        Descendants(tree).OfType<TreeViewItem>().Single(x=>(x.DataContext as VoiceCategory)?.Voice?.Id=="stress-9999").IsSelected=true;
                        if(picker.IsDropDownOpen||((VoiceChoice)picker.SelectedItem).Id!="stress-9999")throw new Exception("Recycled last voice container committed wrong voice");
                        picker.IsDropDownOpen=false;picker.ItemsSource=savedVoices;picker.SelectedIndex=0;picker.SetCatalogue("Edge",picker.Items.OfType<VoiceChoice>());
                        window.WindowState=WindowState.Minimized;typeof(NativeReaderWindow).GetMethod("ShowFromTray",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);if(!window.IsVisible||window.WindowState==WindowState.Minimized)throw new Exception("Tray could not restore minimized main window");
                        var state=new ReaderTrayState(false,false,false,true);int shown=0,toggled=0,stopped=0,quit=0;
                        var tray=new ReaderTray(()=>shown++,()=>toggled++,()=>stopped++,()=>quit++,()=>state);
                        object Field(string name)=>typeof(ReaderTray).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tray)!;
                        void InvokeItem(string name)=>Field(name).GetType().GetMethod("PerformClick")!.Invoke(Field(name),null);
                        if(!tray.IsVisible)throw new Exception("Tray icon not visible");InvokeItem("show");InvokeItem("play");
                        state=new(true,true,false,true);tray.Refresh();if((string)Field("play").GetType().GetProperty("Text")!.GetValue(Field("play"))! != "继续朗读")throw new Exception("Tray pause state label incorrect");InvokeItem("stop");InvokeItem("exit");
                        if(shown!=1||toggled!=1||stopped!=1||quit!=1)throw new Exception("Tray menu actions not routed");
                        tray.Dispose();if(tray.IsVisible)throw new Exception("Tray icon not removed on disposal");
                        Console.WriteLine("PASS real category popup, category-only expansion, retained selection during filtering, committed leaf selection, collapse/empty search, tray visibility/menu states/actions and disposal");done.TrySetResult();
                    }catch(Exception ex){done.TrySetException(ex);}finally{window.Close();}
                };app.Run(window);
            }catch(Exception ex){done.TrySetException(ex);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(30));thread.Join(3000);
    }
}
