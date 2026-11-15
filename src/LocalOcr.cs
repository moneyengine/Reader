using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LightReader;

public record OcrLanguage(string Name,string Tag)
{
    public override string ToString()=>Name;
}
public sealed class LocalOcr
{
    public static List<OcrLanguage> Languages()=>OcrEngine.AvailableRecognizerLanguages.Select(x=>new OcrLanguage(x.DisplayName+" · "+x.LanguageTag,x.LanguageTag)).ToList();
    public static string NormalizeLine(string text)
    {
        text=System.Text.RegularExpressions.Regex.Replace(text,@"(?<=[\u3400-\u9fff\u3000-\u303f\uff00-\uffef])[ \t]+(?=[\u3400-\u9fff\u3000-\u303f\uff00-\uffef])","");
        return System.Text.RegularExpressions.Regex.Replace(text,@"[ \t]+(?=[，。！？；：、．,.!?;:）】》])","");
    }
    public async Task<string> Extract(byte[] image,string language,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(image.Length>32*1024*1024)throw new InvalidOperationException("图片超过 32 MB，请缩小图片后重试。");
        var available=OcrEngine.AvailableRecognizerLanguages;
        var selected=string.IsNullOrWhiteSpace(language)||language=="auto"
            ? available.FirstOrDefault(x=>x.LanguageTag.StartsWith("zh",StringComparison.OrdinalIgnoreCase))??available.FirstOrDefault()
            : available.FirstOrDefault(x=>x.LanguageTag.Equals(language,StringComparison.OrdinalIgnoreCase));
        if(selected is null)throw new InvalidOperationException("未安装可用的本机 OCR 语言。请安装 Windows 相应语言的文字识别组件，或选择其他已安装语言。");
        var engine=OcrEngine.TryCreateFromLanguage(new Language(selected.LanguageTag))??throw new InvalidOperationException("本机 OCR 引擎不可用，请检查 Windows OCR 语言组件。");
        using var stream=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(image);await writer.StoreAsync().AsTask(ct);await writer.FlushAsync().AsTask(ct);}
        stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        if((ulong)decoder.PixelWidth*decoder.PixelHeight>120_000_000)throw new InvalidOperationException("图片像素过多，请裁剪到正文区域后重试。");
        var max=OcrEngine.MaxImageDimension;
        var scale=Math.Min(1.0,Math.Min((double)max/decoder.PixelWidth,(double)max/decoder.PixelHeight));
        if(scale==1&&Math.Max(decoder.PixelWidth,decoder.PixelHeight)<1200)scale=Math.Min(2,(double)max/Math.Max(decoder.PixelWidth,decoder.PixelHeight));
        var transform=new BitmapTransform{ScaledWidth=Math.Max(1,(uint)(decoder.PixelWidth*scale)),ScaledHeight=Math.Max(1,(uint)(decoder.PixelHeight*scale))};
        using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,transform,ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(ct);
        using var opaque=Flatten(bitmap,false);
        var result=await engine.RecognizeAsync(opaque).AsTask(ct);
        if(result.Lines.Count==0){using var alternate=Flatten(bitmap,true);result=await engine.RecognizeAsync(alternate).AsTask(ct);}
        var text=string.Join(Environment.NewLine,result.Lines.Select(x=>NormalizeLine(x.Text)));
        if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException($"本机 OCR 未识别到文字（图片 {decoder.PixelWidth}×{decoder.PixelHeight}，语言 {selected.LanguageTag}）。请查看图片预览，确认复制的是正文；长截图可分区域截图重试。");
        return text;
    }
    static SoftwareBitmap Flatten(SoftwareBitmap source,bool alternate)
    {
        var buffer=new Windows.Storage.Streams.Buffer((uint)(source.PixelWidth*source.PixelHeight*4));source.CopyToBuffer(buffer);
        var pixels=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
        for(int i=0;i<pixels.Length;i+=4){var alpha=pixels[i+3];for(int c=0;c<3;c++)pixels[i+c]=alternate?(byte)(255-(pixels[i+c]*alpha+127)/255):(byte)((pixels[i+c]*alpha+255*(255-alpha)+127)/255);pixels[i+3]=255;}
        using var writer=new DataWriter();writer.WriteBytes(pixels);
        return SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,source.PixelWidth,source.PixelHeight,BitmapAlphaMode.Ignore);
    }
}
