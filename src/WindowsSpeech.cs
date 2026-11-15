using System.Globalization;
using System.Security;
using System.Speech.Synthesis;
using Windows.Media.Core;

namespace LightReader;

public sealed class WindowsSpeech : IDisposable
{
    readonly SpeechSynthesizer synthesizer = new();
    Playback? modernPlayer;
    bool modernPaused, modernMode;
    public bool Paused => modernMode ? modernPaused : synthesizer.State == SynthesizerState.Paused;
    public static List<VoiceChoice> GetVoices()
    {
        using var synth = new SpeechSynthesizer();
        var legacy = synth.GetInstalledVoices().Where(v=>v.Enabled).Select(v=>new VoiceChoice($"{v.VoiceInfo.Name} · {v.VoiceInfo.Culture.Name} · SAPI",v.VoiceInfo.Name));
        var modern = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Select(v=>new VoiceChoice($"{v.DisplayName} · {v.Language} · Windows","winrt:"+v.Id));
        return modern.Concat(legacy).DistinctBy(v=>v.Id).ToList();
    }
    public async Task Speak(string text,string voice,CancellationToken ct,SpeechTuning? tuning=null,IProgress<ReaderProgress>? progress=null)
    {
        ct.ThrowIfCancellationRequested();
        var t=(tuning??new()).Clamp();
        if(voice.StartsWith("winrt:")) { await SpeakModern(text,voice[6..],t,progress,ct);return; }
        synthesizer.SelectVoice(voice);synthesizer.SetOutputToDefaultAudioDevice();
        synthesizer.Volume=(int)Math.Round(t.Volume*100);
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cursor=0;
        void Completed(object? sender,SpeakCompletedEventArgs e) {
            if(e.Error is not null)done.TrySetException(e.Error);
            else if(e.Cancelled)done.TrySetCanceled();else done.TrySetResult();
        }
        void Word(object? sender,SpeakProgressEventArgs e) {
            var pos=text.IndexOf(e.Text,cursor,StringComparison.Ordinal);
            if(pos<0)pos=Math.Clamp(e.CharacterPosition,0,Math.Max(0,text.Length-1));
            cursor=Math.Min(text.Length,pos+e.Text.Length);
            progress?.Report(new(pos,Math.Max(1,e.Text.Length)));
        }
        synthesizer.SpeakCompleted+=Completed;synthesizer.SpeakProgress+=Word;
        try {
            // Build SSML ourselves; user text is escaped and cannot become markup.
            var culture=synthesizer.Voice.Culture.Name;
            var percent=((t.Pitch-1)*100).ToString("+0;-0;0",CultureInfo.InvariantCulture)+"%";
            var rate=(t.Rate*100).ToString("0",CultureInfo.InvariantCulture)+"%";
            var ssml=$"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{culture}'><prosody rate='{rate}' pitch='{percent}'>{SecurityElement.Escape(text)}</prosody></speak>";
            synthesizer.SpeakSsmlAsync(ssml);
            using var registration=ct.Register(()=>{
                try {if(Paused)synthesizer.Resume();synthesizer.SpeakAsyncCancelAll();}
                finally {done.TrySetCanceled(ct);}
            });
            await done.Task;
        }finally {synthesizer.SpeakCompleted-=Completed;synthesizer.SpeakProgress-=Word;}
    }
    async Task SpeakModern(string text,string id,SpeechTuning t,IProgress<ReaderProgress>? progress,CancellationToken ct)
    {
        modernMode=true;
        using var synth=new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        synth.Voice=Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.First(v=>v.Id==id);
        synth.Options.SpeakingRate=t.Rate;synth.Options.AudioPitch=t.Pitch;synth.Options.AudioVolume=t.Volume;
        synth.Options.IncludeWordBoundaryMetadata=true;
        int offset=0;
        try {
            foreach(var chunk in TextChunks.Split(text,260)) {
                ct.ThrowIfCancellationRequested();
                while(modernPaused)await Task.Delay(30,ct);
                using var stream=await synth.SynthesizeTextToStreamAsync(chunk).AsTask(ct);
                var marks=stream.TimedMetadataTracks.SelectMany(track=>track.Cues).OfType<SpeechCue>()
                    .Where(c=>c.StartPositionInInput.HasValue&&c.EndPositionInInput.HasValue)
                    .Select(c=>new SpeechMark(c.StartTime.TotalSeconds,(int)c.StartPositionInInput.GetValueOrDefault(),Math.Max(1,(int)(c.EndPositionInInput.GetValueOrDefault()-c.StartPositionInInput.GetValueOrDefault()+1)))).OrderBy(m=>m.Seconds).ToList();
                using var reader=new Windows.Storage.Streams.DataReader(stream);
                await reader.LoadAsync((uint)stream.Size).AsTask(ct);
                var wav=new byte[checked((int)stream.Size)];reader.ReadBytes(wav);
                using var player=new Playback();modernPlayer=player;
                if(modernPaused)player.Pause();
                await player.Add(AudioGenerationClient.ToPcm(wav),ct);
                var drain=player.Drain(ct);int mark=0;
                progress?.Report(new(offset,1));
                while(!drain.IsCompleted) {
                    while(!modernPaused&&mark<marks.Count&&marks[mark].Seconds<=player.PositionSeconds) {
                        var current=marks[mark++];progress?.Report(new(offset+current.Start,current.Length));
                    }
                    await Task.Delay(30,ct);
                }
                await drain;modernPlayer=null;offset+=chunk.Length;
            }
        }finally {modernPlayer=null;modernMode=false;modernPaused=false;}
    }
    public void Pause() {if(modernMode){modernPaused=true;modernPlayer?.Pause();}else synthesizer.Pause();}
    public void Resume() {if(modernMode){modernPaused=false;modernPlayer?.Resume();}else synthesizer.Resume();}
    public void Dispose()
    {
        modernPlayer?.Stop();
        if(synthesizer.State==SynthesizerState.Paused)synthesizer.Resume();
        synthesizer.SpeakAsyncCancelAll();synthesizer.Dispose();
    }
}
