using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LightReader;

/// <summary>Isolated source-fidelity fixture. It has no native command or data bridge.</summary>
public sealed class StitchDesignPreviewWindow : Window
{
    readonly WebView2 view=new();
    const string Origin="reader-design.local";
    public StitchDesignPreviewWindow(){Title="Stitch 静态设计稿验证 · 示例数据，不连接真实服务";Width=1280;Height=900;Content=view;Closed+=(_,_)=>view.Dispose();}
    public async Task Initialize(string folder)
    {
        var root=Path.GetFullPath(folder);
        if(!File.Exists(Path.Combine(root,"_1","index.html"))||!File.Exists(Path.Combine(root,"assets","stitch.css")))throw new InvalidOperationException("请先生成并编译静态设计稿验证资源。");
        var profile=Path.Combine(Path.GetDirectoryName(root)!,"stitch-webview-profile");
        var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:profile);
        await view.EnsureCoreWebView2Async(environment);
        var core=view.CoreWebView2;
        core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsWebMessageEnabled=false;
        core.NewWindowRequested+=(_,e)=>e.Handled=true;
        core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
        core.NavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||uri.Host!=Origin)e.Cancel=true;};
        core.SetVirtualHostNameToFolderMapping(Origin,root,CoreWebView2HostResourceAccessKind.DenyCors);
    }
    public async Task Navigate(string page)
    {
        if(page is not "_1" and not "_2" and not "_3")throw new ArgumentException("Unknown design page",nameof(page));
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender,CoreWebView2NavigationCompletedEventArgs e){if(e.IsSuccess)done.TrySetResult();else done.TrySetException(new InvalidOperationException("Static design navigation failed: "+e.WebErrorStatus));}
        view.CoreWebView2.NavigationCompleted+=Completed;
        try{view.CoreWebView2.Navigate($"https://{Origin}/{page}/index.html");await done.Task.WaitAsync(TimeSpan.FromSeconds(20));}
        finally{view.CoreWebView2.NavigationCompleted-=Completed;}
    }
    public Task<string> Evaluate(string script)=>view.CoreWebView2.ExecuteScriptAsync(script);
    public async Task Capture(string path){using var file=File.Create(path);await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
}
