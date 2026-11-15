namespace LightReader;

public static class SentenceFocus
{
    static bool Boundary(string text,int i)=>"。！？!?；;\r\n".Contains(text[i])||
        text[i]=='.'&&(i+1==text.Length||char.IsWhiteSpace(text[i+1]));
    public static (int Start,int End) Range(string text,int position)
    {
        if(text.Length==0)return (0,0);
        position=Math.Clamp(position,0,text.Length-1);
        var start=position;while(start>0&&!Boundary(text,start-1))start--;
        var end=position;while(end<text.Length&&!Boundary(text,end))end++;
        if(end<text.Length)end++;
        return (start,end);
    }
}
