using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace LightReader;

public sealed class HistoryImages
{
    readonly string folder;
    public HistoryImages(string? statePath=null)=>folder=Path.Combine(statePath is null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LightReader"):Path.GetDirectoryName(statePath)!,"history-images");
    static bool Valid(string id)=>id.Length==64&&id.All(Uri.IsHexDigit);
    string FileName(string id)=>Valid(id)?Path.Combine(folder,id.ToLowerInvariant()+".dat"):throw new InvalidOperationException("历史图片 ID 无效。");
    public string Save(byte[] image)
    {
        if(image.Length==0||image.Length>32*1024*1024)throw new InvalidOperationException("历史图片为空或超过 32 MB。");
        var id=Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();var file=FileName(id);
        if(File.Exists(file))return id;
        Directory.CreateDirectory(folder);
        if(Directory.EnumerateFiles(folder,"*.dat").Sum(x=>new FileInfo(x).Length)+image.Length>160L*1024*1024)throw new InvalidOperationException("历史图片已超过 160 MB，请在历史窗口删除部分记录后重试。");
        File.WriteAllBytes(file+".tmp",ProtectedData.Protect(image,null,DataProtectionScope.CurrentUser));File.Move(file+".tmp",file,true);return id;
    }
    public byte[]? Load(string? id)
    {
        if(id is null)return null;var file=FileName(id);if(!File.Exists(file))return null;
        return ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser);
    }
    public void Clean(IEnumerable<string?> retained)
    {
        if(!Directory.Exists(folder))return;
        var keep=retained.Where(x=>x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Directory.EnumerateFiles(folder,"*.dat")){
            var id=Path.GetFileNameWithoutExtension(file);
            // Only remove hash-named assets from this dedicated store.
            if(Valid(id)&&!keep.Contains(id))File.Delete(file);
        }
    }
    public string Export(ReaderState state)
    {
        var root=JsonSerializerNode(state);var assets=new JsonObject();long size=0;
        foreach(var id in state.History.Select(x=>x.ImageId).Append(state.CurrentImageId).Where(x=>x is not null).Distinct()){
            var bytes=Load(id);if(bytes is null)continue;size+=bytes.Length;
            if(size>96L*1024*1024)throw new InvalidOperationException("导出图片总量超过 96 MB，请减少历史图片后重试。");
            assets[id!]=Convert.ToBase64String(bytes);
        }
        root["ImageFiles"]=assets;return root.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
    }
    static JsonObject JsonSerializerNode(ReaderState state)=>JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(state))!.AsObject();
    public static Dictionary<string,byte[]> ReadImportImages(string json)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(json);var result=new Dictionary<string,byte[]>();
        if(!doc.RootElement.TryGetProperty("ImageFiles",out var images))return result;
        long total=0;
        foreach(var item in images.EnumerateObject()){
            if(result.Count>=31||!Valid(item.Name))throw new InvalidOperationException("导入的历史图片目录无效。");
            var bytes=Convert.FromBase64String(item.Value.GetString()??"");total+=bytes.Length;
            if(bytes.Length>32*1024*1024||total>96L*1024*1024||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(item.Name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("导入图片大小或校验值无效。");
            result[item.Name]=bytes;
        }
        return result;
    }
}
