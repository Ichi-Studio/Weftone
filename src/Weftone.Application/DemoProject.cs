using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 内置演示工程（确定性合成，无外部资产）：sine 三和弦 + 合成 kick 采样，
/// 供 CLI demo、CI 全链冒烟与黄金音频基线共用同一份定义。
/// </summary>
public static class DemoProject
{
    public const int SampleRate = 48_000;
    public const int Tempo = 128;

    public static ProjectDocument CreateDocument()
    {
        var document = new ProjectDocument { Tempo = Tempo, SampleRate = SampleRate };
        document.Channels.Add(new Channel
        {
            Id = 1, Name = "Lead", Kind = ChannelKind.Sine, Volume = 0.6, Pan = -0.5,
        });
        document.Channels.Add(new Channel
        {
            Id = 2, Name = "Kick", Kind = ChannelKind.Sampler, Volume = 0.9, Pan = 0, AssetId = "asset-kick",
        });
        document.Assets.Add(new AssetRef { Id = "asset-kick", Path = "assets/kick.wav" });

        var pattern = new Pattern { Id = 10, Name = "Demo" };
        foreach (int key in (int[])[60, 64, 67])
        {
            pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = key, Velocity = 0.8, LengthTicks = 192 });
            pattern.Notes.Add(new Note { ChannelId = 1, Tick = 384, Key = key + 12, Velocity = 0.7, LengthTicks = 96 });
        }
        foreach (long tick in (long[])[0, 192, 384])
        {
            pattern.Notes.Add(new Note { ChannelId = 2, Tick = tick, Key = 60, Velocity = 1.0, LengthTicks = 48 });
        }
        document.Patterns.Add(pattern);

        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 480 });
        return document;
    }

    /// <summary>合成 kick 采样（150→50Hz 指数下滑 + 振幅衰减），写为 mono float32 WAV。</summary>
    public static void WriteKickWav(string path)
    {
        const int frames = 2_400;
        var samples = new float[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)frames;
            double frequency = 150.0 * Math.Pow(50.0 / 150.0, t);
            phase += 2.0 * Math.PI * frequency / SampleRate;
            double envelope = Math.Exp(-6.0 * t);
            samples[i] = (float)(Math.Sin(phase) * envelope);
        }
        using FileStream stream = File.Create(path);
        WavWriter.WriteHeader(stream, SampleRate, 1, frames);
        WavWriter.WriteSamples(stream, samples);
    }
}
