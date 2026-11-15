using System.IO;

namespace LightReader;

public static class ReaderDiagnostics
{
    static readonly object Gate=new();
    public static void Write(string stage)
    {
        try{
            lock(Gate){
                var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LightReader");
                Directory.CreateDirectory(folder);var file=Path.Combine(folder,"playback-diagnostics.log");
                if(File.Exists(file)&&new FileInfo(file).Length>1024*1024)File.WriteAllText(file,"");
                File.AppendAllText(file,$"{DateTimeOffset.Now:O} {stage}{Environment.NewLine}");
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
