using System.Drawing;
using Forms = System.Windows.Forms;

namespace LightReader;

public sealed record ReaderTrayState(bool Active,bool Paused,bool Recognizing,bool HasText);

/// <summary>Windows notification-area entry; disposed with its owning reader window.</summary>
public sealed class ReaderTray : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ContextMenuStrip menu=new();
    readonly Forms.ToolStripMenuItem show=new("显示主窗口"),play=new("开始朗读"),stop=new("停止"),exit=new("退出轻读");
    readonly Func<ReaderTrayState> state;
    readonly Icon picture;
    bool disposed;
    public bool IsVisible=>!disposed&&icon.Visible;
    public ReaderTray(Action restore,Action togglePlayback,Action stopPlayback,Action quit,Func<ReaderTrayState> status)
    {
        state=status;picture=CreateIcon();
        icon=new Forms.NotifyIcon{Icon=picture,Text="轻读",ContextMenuStrip=menu,Visible=true};
        show.Click+=(_,_)=>restore();play.Click+=(_,_)=>togglePlayback();stop.Click+=(_,_)=>stopPlayback();exit.Click+=(_,_)=>quit();
        icon.DoubleClick+=(_,_)=>restore();
        menu.Items.Add(show);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(play);menu.Items.Add(stop);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(exit);
        menu.Opening+=(_,_)=>Refresh();Refresh();
    }
    public void Refresh()
    {
        if(disposed)return;var current=state();
        play.Text=current.Paused?"继续朗读":current.Active&&!current.Recognizing?"暂停朗读":"开始朗读";
        play.Enabled=current.HasText&&!current.Recognizing;stop.Enabled=current.Active;
        icon.Text="轻读 · "+(current.Recognizing?"正在识字":current.Paused?"已暂停":current.Active?"正在朗读":"等待朗读");
    }
    static Icon CreateIcon()
    {
        using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/LightReader;component/Assets/LightReader.ico"))!.Stream;
        return new Icon(stream,32,32);
    }
    public void Dispose(){if(disposed)return;disposed=true;icon.Visible=false;icon.Dispose();menu.Dispose();picture.Dispose();}
}
