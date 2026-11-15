using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace LightReader;

public static class ClipboardImage
{
    const int Limit=32*1024*1024;
    public static bool HasImage(IDataObject? data)=>data is not null &&
        (data.GetDataPresent("PNG",false)||data.GetDataPresent("image/png",false)||data.GetDataPresent(DataFormats.Bitmap,true));

    public static byte[] ReadPng(IDataObject data)
    {
        foreach(var format in new[]{"PNG","image/png"}) {
            if(!data.GetDataPresent(format,false))continue;
            var value=data.GetData(format,false);
            if(value is byte[] bytes){ValidateSize(bytes.Length);return bytes.ToArray();}
            if(value is MemoryStream memory){ValidateSize(memory.Length);return memory.ToArray();}
            if(value is Stream stream){
                var position=stream.CanSeek?stream.Position:0;
                try{
                    if(stream.CanSeek){ValidateSize(stream.Length);stream.Position=0;}
                    using var output=new MemoryStream();var buffer=new byte[8192];int count;
                    while((count=stream.Read(buffer,0,buffer.Length))>0){ValidateSize(output.Length+count);output.Write(buffer,0,count);}
                    return output.ToArray();
                }finally{if(stream.CanSeek)stream.Position=position;}
            }
        }
        if(data.GetData(DataFormats.Bitmap,true) is not BitmapSource bitmap)
            throw new InvalidOperationException("剪贴板中的图片格式无法读取，请重新复制图片或使用图片文件识字。");
        if((long)bitmap.PixelWidth*bitmap.PixelHeight>120_000_000)throw new InvalidOperationException("剪贴板图片过大，请先裁剪正文区域。");
        bitmap=RepairBitmapAlpha(bitmap);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var encoded=new MemoryStream();encoder.Save(encoded);ValidateSize(encoded.Length);return encoded.ToArray();
    }
    public static BitmapSource RepairBitmapAlpha(BitmapSource bitmap)
    {
        // Some CF_BITMAP/DIB producers leave the unused alpha channel at zero.
        // Repair only that bitmap case, never discard intentional PNG transparency.
        if(bitmap.Format!=PixelFormats.Bgra32&&bitmap.Format!=PixelFormats.Pbgra32)return bitmap;
        int stride=checked(bitmap.PixelWidth*4);var pixels=new byte[checked(stride*bitmap.PixelHeight)];bitmap.CopyPixels(pixels,stride,0);
        bool allZero=true,varied=false;
        for(int i=0;i<pixels.Length;i+=4){if(pixels[i+3]!=0){allZero=false;break;}if(pixels[i]!=pixels[0]||pixels[i+1]!=pixels[1]||pixels[i+2]!=pixels[2])varied=true;}
        if(!allZero||!varied)return bitmap;
        for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
        var repaired=BitmapSource.Create(bitmap.PixelWidth,bitmap.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);repaired.Freeze();return repaired;
    }
    static void ValidateSize(long length){if(length==0||length>Limit)throw new InvalidOperationException("剪贴板图片为空或超过 32 MB，请重新复制较小的图片。");}
}
