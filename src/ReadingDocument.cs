using System.Windows.Documents;

namespace LightReader;

// Maps the editable projection back to source without serializing away Markdown syntax.
public sealed class ReadingDocument
{
    string visible;
    int[] starts,ends;
    sealed record Snapshot(string Visible,string Source,int[] Starts,int[] Ends);
    readonly List<Snapshot> history=new();
    public ReadingDocument(FlowDocument document,string source,List<(int Start,int Length,Run Run)> runs)
    {
        visible=Text(document);starts=new int[visible.Length];ends=new int[visible.Length];
        Array.Fill(starts,-1);
        var offsets=RunOffsets(document,visible).ToDictionary(x=>x.Run,x=>x.Offset);
        foreach(var entry in runs){
            if(!offsets.TryGetValue(entry.Run,out var offset))continue;
            for(int i=0;i<entry.Length&&offset+i<visible.Length;i++){
                starts[offset+i]=entry.Start+i;ends[offset+i]=entry.Start+i+1;
            }
        }
        int previous=0;
        for(int i=0;i<visible.Length;i++){
            if(starts[i]>=0){previous=ends[i];continue;}
            var next=i+1;while(next<visible.Length&&starts[next]<0)next++;
            var limit=next<visible.Length?starts[next]:source.Length;
            var newline=visible[i] is '\r' or '\n'?source.IndexOf(visible[i],Math.Min(previous,source.Length)):-1;
            starts[i]=newline>=previous&&newline<limit?newline:previous;
            ends[i]=newline>=previous&&newline<limit?newline+1:starts[i];previous=ends[i];
        }
        Remember(source);
    }
    static string Text(FlowDocument document)=>new TextRange(document.ContentStart,document.ContentEnd).Text;
    static IEnumerable<Run> Runs(BlockCollection blocks)
    {
        IEnumerable<Run> Inlines(InlineCollection inlines){foreach(var inline in inlines){if(inline is Run run)yield return run;else if(inline is Span span)foreach(var nested in Inlines(span.Inlines))yield return nested;}}
        foreach(var block in blocks){
            if(block is Paragraph paragraph){foreach(var run in Inlines(paragraph.Inlines))yield return run;}
            else if(block is Section section){foreach(var run in Runs(section.Blocks))yield return run;}
            else if(block is List list){foreach(var item in list.ListItems)foreach(var run in Runs(item.Blocks))yield return run;}
            else if(block is Table table){foreach(var group in table.RowGroups)foreach(var row in group.Rows)foreach(var cell in row.Cells)foreach(var run in Runs(cell.Blocks))yield return run;}
        }
    }
    // WPF TextRange expands partial table selections to entire cells. Locate individual
    // runs in the full projection instead, including generated labels in document order.
    static IEnumerable<(Run Run,int Offset)> RunOffsets(FlowDocument document,string text)
    {
        int cursor=0;
        foreach(var run in Runs(document.Blocks)){
            int offset=text.IndexOf(run.Text,cursor,StringComparison.Ordinal);
            if(offset<0)continue;
            yield return(run,offset);cursor=offset+run.Text.Length;
        }
    }
    public int SourcePosition(FlowDocument document,TextPointer pointer)
    {
        int index=visible.Length;
        foreach(var (run,offset) in RunOffsets(document,visible)){
            if(pointer.CompareTo(run.ContentStart)<=0){index=offset;break;}
            if(pointer.CompareTo(run.ContentEnd)<=0){index=offset+Math.Clamp(run.ContentStart.GetOffsetToPosition(pointer),0,run.Text.Length);break;}
        }
        index=Math.Clamp(index,0,starts.Length);
        return index<starts.Length?starts[index]:ends.LastOrDefault();
    }
    public (int Start,int End) SourceRange(FlowDocument document,TextPointer start,TextPointer end,int sourceLength)
    {
        int low=SourcePosition(document,start),high=SourcePosition(document,end);
        if(start.CompareTo(end)!=0){
            var runs=Runs(document.Blocks).ToList();
            if(runs.Count==0||start.CompareTo(runs[0].ContentStart)<=0&&end.CompareTo(runs[^1].ContentEnd)>=0)
                return (0,sourceLength);
        }
        low=Math.Clamp(low,0,sourceLength);return (low,Math.Clamp(high,low,sourceLength));
    }
    void Remember(string source)
    {
        history.Add(new(visible,source,starts,ends));
        if(history.Count>100)history.RemoveAt(0);
    }
    public string Update(FlowDocument document,string source,bool undoOrRedo=false)
    {
        var updated=Text(document);if(updated==visible)return source;
        if(undoOrRedo&&history.LastOrDefault(x=>x.Visible==updated) is {} snapshot){
            visible=snapshot.Visible;starts=snapshot.Starts;ends=snapshot.Ends;return snapshot.Source;
        }
        int prefix=0;while(prefix<visible.Length&&prefix<updated.Length&&visible[prefix]==updated[prefix])prefix++;
        int suffix=0;while(suffix<visible.Length-prefix&&suffix<updated.Length-prefix&&visible[^(suffix+1)]==updated[^(suffix+1)])suffix++;
        if(prefix==0&&visible.Length-suffix>=visible.TrimEnd('\r','\n').Length){
            // Whole-document replacement removes hidden Markdown too. The final CRLF
            // belongs to WPF's terminal paragraph, not to the user's new content.
            source=updated.EndsWith("\r\n",StringComparison.Ordinal)?updated[..^2]:updated;
            visible=updated;starts=new int[updated.Length];ends=new int[updated.Length];
            for(int i=0;i<updated.Length;i++){starts[i]=Math.Min(i,source.Length);ends[i]=Math.Min(i+1,source.Length);}
            Remember(source);return source;
        }
        int start=prefix<starts.Length?starts[prefix]:ends.LastOrDefault();
        start=Math.Clamp(start,0,source.Length);
        var inserted=updated.Substring(prefix,updated.Length-prefix-suffix);
        var removed=new List<(int Start,int End)>();
        for(int i=prefix;i<visible.Length-suffix;i++){
            var a=Math.Clamp(starts[i],start,source.Length);var b=Math.Clamp(ends[i],a,source.Length);
            if(a==b)continue;
            if(removed.Count>0&&a<=removed[^1].End)removed[^1]=(removed[^1].Start,Math.Max(b,removed[^1].End));
            else removed.Add((a,b));
        }
        int Remap(int offset)=>offset-removed.Sum(range=>Math.Clamp(offset-range.Start,0,range.End-range.Start))+inserted.Length;
        var newStarts=new int[updated.Length];var newEnds=new int[updated.Length];
        Array.Copy(starts,newStarts,prefix);Array.Copy(ends,newEnds,prefix);
        for(int i=0;i<inserted.Length;i++){newStarts[prefix+i]=start+i;newEnds[prefix+i]=start+i+1;}
        for(int i=0;i<suffix;i++){
            newStarts[updated.Length-suffix+i]=Remap(starts[visible.Length-suffix+i]);
            newEnds[updated.Length-suffix+i]=Remap(ends[visible.Length-suffix+i]);
        }
        visible=updated;starts=newStarts;ends=newEnds;
        // Delete visible characters only, retaining hidden emphasis/link delimiters between runs.
        foreach(var range in removed.AsEnumerable().Reverse())source=source.Remove(range.Start,range.End-range.Start);
        source=source.Insert(start,inserted);Remember(source);return source;
    }
}
