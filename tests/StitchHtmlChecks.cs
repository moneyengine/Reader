using System.IO;
using System.Text.Json;
using System.Windows;
using LightReader;

static class StitchHtmlChecks
{
    public static Task Run(string folder)
    {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("HTML fixture requires an STA entry point.");
        Exception? failure=null;
        using var watchdog=new System.Threading.Timer(_=>{Console.Error.WriteLine("Static HTML fixture timed out");Environment.Exit(124);},null,TimeSpan.FromSeconds(90),Timeout.InfiniteTimeSpan);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new StitchDesignPreviewWindow();
        window.Loaded+=async(_,_)=>{
            try{
                await window.Initialize(folder);
                foreach(var page in new[]{"_1","_2","_3"}){
                    await window.Navigate(page);
                    await window.Evaluate("document.fonts.ready.then(() => true)");
                    using var layout=JsonDocument.Parse(await window.Evaluate("(() => { const box=e=>{let r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};}; return {viewport:{width:innerWidth,height:innerHeight},header:box(document.querySelector('header')),footer:box(document.querySelector('footer')),images:[...document.images].map(i=>({complete:i.complete,width:i.naturalWidth}))}; })()"));
                    if(Math.Abs(layout.RootElement.GetProperty("header").GetProperty("height").GetDouble()-40)>.1||Math.Abs(layout.RootElement.GetProperty("footer").GetProperty("height").GetDouble()-72)>.1)throw new Exception("Exported HTML header/footer dimensions changed");
                    if(layout.RootElement.GetProperty("images").EnumerateArray().Any(x=>x.GetProperty("width").GetInt32()==0))throw new Exception("Source design image missing");
                    Directory.CreateDirectory("artifacts");File.WriteAllText($"artifacts/stitch-html-{page}-metrics.json",layout.RootElement.GetRawText());await window.Capture($"artifacts/stitch-html-{page}.png");
                    Console.WriteLine("PASS offline exported HTML fixture "+page+": source header/footer and images rendered");
                }
            }catch(Exception ex){failure=ex;}finally{window.Close();app.Shutdown();}
        };
        app.Run(window);if(failure is not null)throw failure;return Task.CompletedTask;
    }
}
