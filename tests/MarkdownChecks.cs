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
            var codeText="正文。\n\n```hlsl\nfloat value = 1;\n```\n\n```text\n发现问题\n↓\n修复问题\n```\n\n继续正文。";
            var codeRuns=new List<(int Start,int Length,Run Run)>();var blocks=MarkdownReading.Render(codeText,18,Brushes.Black,_=>{},codeRuns);
            if(MarkdownReading.CountProgrammingBlocks(codeText)!=1)throw new Exception("Text flow counted as programming code");
            var labels=blocks.Blocks.OfType<Section>().SelectMany(section=>section.Blocks.OfType<Paragraph>()).SelectMany(p=>p.Inlines.OfType<Run>()).Where(run=>run.Tag as string=="code-label").ToList();
            if(labels.Count!=2||!labels[0].Text.Contains("已跳过")||!labels[1].Text.Contains("正常朗读"))throw new Exception("Code/flow header status wrong");
            foreach(var entry in codeRuns)if(codeText.Substring(entry.Start,entry.Length)!=entry.Run.Text)throw new Exception("Code headers damaged source mapping");
            Console.WriteLine("PASS code language/status headers, text flow status, programming count and exact source run offsets");
            var tableText="| A | B | C |\n| :--- | :---: | ---: |\n| repeated | **重复** | 1 |\n| repeated | | 2 |";
            var tableRuns=new List<(int Start,int Length,Run Run)>();var tableDocument=MarkdownReading.Render(tableText,18,Brushes.Black,_=>{},tableRuns);
            if(!MarkdownReading.IsMarkdown(tableText))throw new Exception("Pipe table not recognized as Markdown");
            var table=tableDocument.Blocks.OfType<Table>().Single();
            if(table.Columns.Count!=3||table.RowGroups.Single().Rows.Count!=3)throw new Exception("Table/empty cell structure incorrect");
            var mapping=new ReadingDocument(tableDocument,tableText,tableRuns);
            foreach(var entry in tableRuns)if(tableText.Substring(entry.Start,entry.Length)!=entry.Run.Text||mapping.SourcePosition(tableDocument,entry.Run.ContentStart)!=entry.Start)throw new Exception("Table original offsets incorrect for repeated content");
            Console.WriteLine("PASS pipe tables, empty cells, repeated text, emphasis, alignment and exact source mappings");
        }catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw failure;
    }
}
