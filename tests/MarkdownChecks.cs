using LightReader;
using System.Windows.Documents;
using System.Windows.Media;

static class MarkdownChecks
{
    public static void Run(){
        Exception? failure=null;
        var thread=new Thread(()=>{try{
            var runs=new List<(int Start,int Length,Run Run)>();
            var text="# 标题\n\n**加粗** 和 `代码`。\n\n- 第一条\n- 第二条\n\n> 引用\n\n[链接](https://example.com)";
            var doc=MarkdownReading.Render(text,18,Brushes.Black,_=>{},runs);
            if(!MarkdownReading.IsMarkdown(text)||!MarkdownReading.IsMarkdown("普通正文中的 **重点**。")||MarkdownReading.IsMarkdown("普通正文。\n下一行没有格式。"))throw new Exception("Markdown paste detection incorrect");
            if(!doc.Blocks.OfType<System.Windows.Documents.List>().Any()||!runs.Any(x=>x.Run.Text=="标"))throw new Exception("Markdown blocks missing");
            foreach(var run in runs)if(text.Substring(run.Start,run.Length)!=run.Run.Text)throw new Exception("Markdown source mapping incorrect: "+run.Run.Text);
            var sample="上限为3。"+new string(' ',400)+"因此，这十秒不能取消。";
            var chunks=TextChunks.Split(sample,150,true).ToArray();
            if(chunks.Length!=1||string.Concat(chunks)!=sample)throw new Exception("Filtered blanks still split sentence at punctuation");
            Console.WriteLine("PASS Markdown headings/emphasis/code/lists/quotes/links and original offsets; long filtered gap after numeric sentence");
        }catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw failure;
    }
}
