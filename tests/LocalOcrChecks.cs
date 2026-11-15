using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightReader;

static class LocalOcrChecks
{
    public static async Task Run()
    {
        var languages=LocalOcr.Languages();
        Console.WriteLine("Local OCR languages: "+string.Join(", ",languages.Select(x=>x.Tag)));
        var ready=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            try{
                var visual=new DrawingVisual();
                using(var dc=visual.RenderOpen()){
                    dc.DrawRectangle(Brushes.White,null,new Rect(0,0,1100,280));
                    dc.DrawText(new FormattedText("你好，这是免费的本机文字识别。",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),36,Brushes.Black,1),new Point(30,35));
                    dc.DrawText(new FormattedText("Hello world. OCR test 12345.",CultureInfo.GetCultureInfo("en-US"),FlowDirection.LeftToRight,new Typeface("Arial"),34,Brushes.Black,1),new Point(30,125));
                }
                var bitmap=new RenderTargetBitmap(1100,280,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=new MemoryStream();encoder.Save(stream);ready.SetResult(stream.ToArray());
            }catch(Exception ex){ready.SetException(ex);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        var png=await ready.Task;
        Directory.CreateDirectory("artifacts");File.WriteAllBytes("artifacts/local-ocr-fixture.png",png);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var result=await new LocalOcr().Extract(png,"auto",default);
        Console.WriteLine("Local OCR text: "+result);
        Console.WriteLine($"Time: {watch.Elapsed.TotalSeconds:F2}s");
        if(!result.Contains("12345")||!result.Contains("Hello")||!result.Contains("免费"))throw new Exception("Local OCR bilingual fixture failed");
        Console.WriteLine("PASS real local bilingual OCR");
        if(LocalOcr.NormalizeLine("你 好 ， 世 界 。")!="你好，世界。"||LocalOcr.NormalizeLine("Hello world. 12345 ．")!="Hello world. 12345．")throw new Exception("OCR whitespace normalization failed");
        using var stopped=new CancellationTokenSource();stopped.Cancel();
        try{await new LocalOcr().Extract(png,"auto",stopped.Token);throw new Exception("Cancelled OCR continued");}catch(OperationCanceledException){Console.WriteLine("PASS OCR cancellation before work");}
        try{await new LocalOcr().Extract(png,"xx-INVALID",default);throw new Exception("Unknown OCR language accepted");}catch(InvalidOperationException){Console.WriteLine("PASS missing language produces visible error");}
    }
}
