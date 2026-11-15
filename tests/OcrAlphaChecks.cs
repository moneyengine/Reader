using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightReader;

static class OcrAlphaChecks
{
    public static async Task Run()
    {
        var ready=new TaskCompletionSource<List<(string,byte[])>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            try{
                var fixtures=new List<(string,byte[])>();
                byte[] Encode(BitmapSource bitmap){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var data=new MemoryStream();encoder.Save(data);return data.ToArray();}
                RenderTargetBitmap Render(bool background,bool whiteText){
                    var visual=new DrawingVisual();
                    using(var dc=visual.RenderOpen()){
                        if(background)dc.DrawRectangle(whiteText?Brushes.Black:Brushes.White,null,new Rect(0,0,1000,160));
                        dc.DrawText(new FormattedText("免费图片识字 OCR Test 12345",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),36,whiteText?Brushes.White:Brushes.Black,1),new Point(30,45));
                    }
                    var bitmap=new RenderTargetBitmap(1000,160,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;
                }
                fixtures.Add(("transparent black text",Encode(Render(false,false))));
                fixtures.Add(("transparent white text",Encode(Render(false,true))));
                fixtures.Add(("dark background",Encode(Render(true,true))));
                var normal=new FormatConvertedBitmap(Render(true,false),PixelFormats.Bgra32,null,0);
                int stride=normal.PixelWidth*4;var pixels=new byte[stride*normal.PixelHeight];normal.CopyPixels(pixels,stride,0);
                for(int i=3;i<pixels.Length;i+=4)pixels[i]=0;
                var broken=BitmapSource.Create(normal.PixelWidth,normal.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);
                var clipboard=new DataObject(DataFormats.Bitmap,broken);
                fixtures.Add(("CF_BITMAP zero alpha",ClipboardImage.ReadPng(clipboard)));
                ready.SetResult(fixtures);
            }catch(Exception ex){ready.SetException(ex);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        var inputs=await ready.Task;
        foreach(var (name,image) in inputs){
            var result=await new LocalOcr().Extract(image,"auto",default);
            if(!result.Contains("免费")||!result.Contains("12345"))throw new Exception(name+": failed recognition");
            Console.WriteLine("PASS "+name);
        }
    }
}
