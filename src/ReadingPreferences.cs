namespace LightReader;

public sealed record ReadingPreferences(bool SkipCode=true,bool SkipLinks=true,bool SkipDecorations=true,bool KeepInlineTerms=true,bool FocusSentences=true)
{
    public bool Enabled(string key)=>key switch{nameof(SkipCode)=>SkipCode,nameof(SkipLinks)=>SkipLinks,nameof(SkipDecorations)=>SkipDecorations,nameof(KeepInlineTerms)=>KeepInlineTerms,nameof(FocusSentences)=>FocusSentences,_=>throw new ArgumentException("Unknown reading preference",nameof(key))};
    public ReadingPreferences Set(string key,bool value)=>key switch{nameof(SkipCode)=>this with{SkipCode=value},nameof(SkipLinks)=>this with{SkipLinks=value},nameof(SkipDecorations)=>this with{SkipDecorations=value},nameof(KeepInlineTerms)=>this with{KeepInlineTerms=value},nameof(FocusSentences)=>this with{FocusSentences=value},_=>throw new ArgumentException("Unknown reading preference",nameof(key))};
}
