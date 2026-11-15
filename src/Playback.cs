using NAudio.Wave;

namespace LightReader;

public sealed class Playback : IDisposable
{
    readonly BufferedWaveProvider buffer = new(new WaveFormat(24000, 16, 1)) {
        BufferDuration = TimeSpan.FromMinutes(3), ReadFully = true
    };
    readonly WaveOutEvent output = new() { DesiredLatency = 100, NumberOfBuffers = 3 };
    bool started;
    public bool Paused { get; private set; }
    public bool HasAudio => started;
    public double PositionSeconds => output.GetPosition() / 48000.0;
    public float Volume { get => output.Volume; set => output.Volume = Math.Clamp(value,0,1); }
    public Playback() => output.Init(buffer);
    public Task Add(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (buffer.BufferedBytes + bytes.Length > buffer.BufferLength)
            throw new InvalidOperationException("暂停缓存已满。请停止后重新朗读，或缩短单次文本。");
        buffer.AddSamples(bytes, 0, bytes.Length);
        if (!started) { started = true; if (!Paused) output.Play(); }
        return Task.CompletedTask;
    }
    public void Pause() { Paused = true; output.Pause(); }
    public void Resume() { Paused = false; if (started) output.Play(); }
    public async Task Ready(CancellationToken ct)
    {
        // Bound prefetch to one text chunk; keep receiving that chunk while paused.
        while (Paused || buffer.BufferedDuration.TotalSeconds > 1.5) await Task.Delay(40, ct);
    }
    public async Task Drain(CancellationToken ct)
    {
        var last=buffer.BufferedBytes;var stalled=System.Diagnostics.Stopwatch.StartNew();
        while (Paused || buffer.BufferedBytes > 0){
            ct.ThrowIfCancellationRequested();
            if(Paused||buffer.BufferedBytes!=last){last=buffer.BufferedBytes;stalled.Restart();}
            else if(stalled.Elapsed>TimeSpan.FromSeconds(5))throw new InvalidOperationException("音频设备停止消耗数据，请检查输出设备后点击播放重试。");
            await Task.Delay(30,ct);
        }
        // WaveOut may still hold the final device buffers after the provider has drained.
        await Task.Delay(180, ct);
    }
    public void Stop() { output.Stop(); buffer.ClearBuffer(); }
    public void Dispose() { output.Stop(); output.Dispose(); }
}
