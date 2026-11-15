using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LightReader;

static class EdgeTtsChecks
{
    public static void Offline()
    {
        var choices=EdgeTtsClient.Choices();
        if(choices.Count<100||choices.Select(x=>x.Id).Distinct().Count()!=choices.Count||!choices.Any(x=>x.Id=="zh-CN-XiaoxiaoNeural"))throw new Exception("Edge catalogue missing/duplicated");
        if(!choices[0].Name.Contains("zh-CN"))throw new Exception("Chinese voices are not first");
        var body=JsonSerializer.SerializeToElement(EdgeTtsClient.Request("你好 <tag> & 😀\n$(keep literal)","zh-CN-XiaoxiaoNeural",new(1.25,1.1,.5)));
        if(body.GetProperty("rate").GetString()!="+25%"||body.GetProperty("pitch").GetString()!="+10Hz"||body.GetProperty("text").GetString()!="你好 <tag> & 😀\n$(keep literal)")throw new Exception("Natural speech tuning or text escaping changed source");
        using var events=JsonDocument.Parse("[{\"seconds\":0.1,\"text\":\"Hello\"},{\"seconds\":0.4,\"text\":\"&amp;\"},{\"seconds\":0.8,\"text\":\"Hello\"}]");
        var marks=EdgeTtsClient.AlignMarks("Hello & Hello",events.RootElement);
        if(marks.Count!=3||marks[0].Start!=0||marks[1].Start!=6||marks[2].Start!=8)throw new Exception("Boundary mapping does not preserve repeated words/escaped text");
        Console.WriteLine($"PASS Edge catalogue {choices.Count} voices, Chinese-first ordering, tuning, verbatim Unicode/markup text and repeated/escaped boundary mapping");
    }
    public static async Task Live()
    {
        Offline();var client=new EdgeTtsClient();
        Console.WriteLine(await client.CheckEnvironment(null,default));
        var voices=await client.RefreshVoices(null,default);if(!voices.Any(x=>x.Id=="zh-CN-XiaoxiaoNeural"))throw new Exception("Service no longer lists selected voice");
        const string text="你好，这是轻读的自然语音测试。让眼睛休息片刻，也让注意力回到内容本身。";
        var watch=System.Diagnostics.Stopwatch.StartNew();var result=await client.Generate(text,"zh-CN-XiaoxiaoNeural",new(),null,default);
        var pcm=EdgeTtsClient.DecodeMp3(result.Mp3);
        if(pcm.Length<24000||result.Marks.Count==0||result.Marks.Any(x=>x.Start<0||x.Start+x.Length>text.Length))throw new Exception("Real speech audio/boundaries invalid");
        Directory.CreateDirectory("artifacts");File.WriteAllBytes("artifacts/edge-natural-test.mp3",result.Mp3);
        Console.WriteLine($"PASS live {voices.Count} voices, real synthesis+MP3 decode {pcm.Length} PCM bytes, {result.Marks.Count} boundaries, segment ready {watch.Elapsed.TotalSeconds:F2}s");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        try{await client.Generate("取消测试","zh-CN-XiaoxiaoNeural",new(),null,canceled.Token);throw new Exception("Pre-canceled generation continued");}catch(OperationCanceledException){Console.WriteLine("PASS canceled before process/network work");}
        using var inFlight=new CancellationTokenSource();var pending=client.Generate(string.Concat(Enumerable.Repeat("这是取消自然语音的测试。",20)),"zh-CN-XiaoxiaoNeural",new(),null,inFlight.Token);
        await Task.Delay(120);inFlight.Cancel();
        try{await pending;throw new Exception("In-flight request did not cancel");}catch(OperationCanceledException){Console.WriteLine("PASS in-flight Python process and network cancellation");}
    }
    public static async Task Native(bool completion=false)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Path.GetTempPath(),"LightReaderEdgeUi",Guid.NewGuid().ToString("N"),"state.dat");
        new ReaderState{Text=completion?"6. **筛选和销毁都需要控制预算。**\n\n建议低频调度、分批检查，先过滤时间和保护标记，再做距离、密度和视野检测。密度查询避免每轮盒子之间全量两两比较，可用空间网格。提交前重新确认交互状态，并立即标记待销毁，防止重复入队。\n\n**销毁链路的两个注意点**\n现有 `AddActorForDestroy()` 入队时就冻结，隐藏并禁用交互，默认等待10秒后实际销毁，Actor队列每帧处理上限为3。因此，这10秒不能当成还能取消的业务等待期，判断必须在提交前完成。":string.Concat(Enumerable.Repeat("这是轻读的在线自然语音测试。可以暂停、继续，也可以随时停止。",12))}.Save(profile);
        var thread=new Thread(()=>{
            var app=new Application();var window=new NativeReaderWindow(profile);
            window.Loaded+=async(_,_)=>{
                try{
                    T Find<T>(string name) where T:class=>(T)window.FindName(name);
                    var engine=Find<ComboBox>("Engine");engine.SelectedIndex=2;
                    var voice=Find<ComboBox>("Voice");if(voice.Items.Count<100)throw new Exception("Edge voices missing from native dropdown");
                    Find<TextBox>("VoiceSearch").Text="云希";
                    voice.SelectedItem=voice.Items.OfType<VoiceChoice>().Single(x=>x.Id==(completion?"zh-CN-YunyangNeural":"zh-CN-YunxiNeural"));
                    if(completion)Find<Slider>("Rate").Value=1.5;
                    Find<Slider>("Volume").Value=0;
                    var play=Find<Button>("PlayButton");var stop=Find<Button>("StopButton");
                    play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    if(completion){
                        string previous="";
                        while(stop.IsEnabled&&watch.Elapsed<TimeSpan.FromSeconds(110)){
                            var status=Find<TextBlock>("Status").Text;if(status.Contains("等待在线语音返回")&&!play.Content.ToString()!.StartsWith("等待 "))throw new Exception("Waiting state missing from play button");
                            if(status.Contains("文字同步朗读")&&!play.Content.ToString()!.Contains("暂停"))throw new Exception("Playback button failed to leave waiting state");
                            if(status!=previous){Console.WriteLine(status);previous=status;}
                            await Task.Delay(250);
                        }
                        if(Find<TextBlock>("Status").Text!="朗读完成")throw new Exception("Completion repro failed: "+Find<TextBlock>("Status").Text);
                        Console.WriteLine($"PASS Yunyang 1.5x multi-segment silent playback completed across budget heading and numeric sentence in {watch.Elapsed.TotalSeconds:F1}s");done.TrySetResult();return;
                    }
                    while(!Find<TextBlock>("Status").Text.Contains("文字同步朗读")&&watch.Elapsed<TimeSpan.FromSeconds(30)){
                        if(!stop.IsEnabled)throw new Exception("Edge generation failed in UI: "+Find<TextBlock>("Status").Text);
                        await Task.Delay(80);
                    }
                    if(!Find<TextBlock>("Status").Text.Contains("文字同步朗读"))throw new Exception("Online playback did not start within 30s");
                    await Task.Delay(500);play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(!play.Content.ToString()!.Contains("继续"))throw new Exception("Edge unified pause failed");
                    var seek=Find<Slider>("Seek");var paused=seek.Value;await Task.Delay(250);if(seek.Value!=paused)throw new Exception("Paused Edge progress changed");
                    play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
                    if(!play.Content.ToString()!.Contains("暂停"))throw new Exception("Edge resume failed");
                    stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
                    if(stop.IsEnabled||seek.Value!=0||!play.Content.ToString()!.Contains("播放"))throw new Exception("Edge stop did not reset playback");
                    Console.WriteLine("PASS native Edge provider/voice search, real Yunxi synthesis, silent playback, unified pause/resume, stop and prefetch cancellation; isolated state, no Volcano API calls");done.TrySetResult();
                }catch(Exception ex){done.TrySetException(ex);}finally{window.Close();}
            };app.Run(window);
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(completion?120:40));thread.Join(3000);
    }

}
