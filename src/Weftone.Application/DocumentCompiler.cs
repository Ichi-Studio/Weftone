using Weftone.Abstractions.Rendering;
using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 编译器（ADR-0003）：ProjectDocument → RenderPlan，非实时线程执行，全量编译。
/// 同 Document ⇒ 同 RenderPlan（事件按 StartSample 稳定排序，逐位确定）。
/// </summary>
public sealed class DocumentCompiler
{
    /// <summary>仅 sine 声部的工程可不带资产编译。</summary>
    public RenderPlan Compile(ProjectDocument document) => Compile(document, []);

    /// <summary>sampler 声部需要 PreparedAsset（已解码/重采样）；AssetIndex 指向 plan.Assets。</summary>
    public RenderPlan Compile(ProjectDocument document, IReadOnlyList<PreparedAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        (long num, long den) = TickMath.SamplesPerTick(document.SampleRate, document.Tempo);

        var assetIndexById = new Dictionary<string, int>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            assetIndexById[assets[i].AssetId] = i;
        }

        var patternEnds = new Dictionary<int, long>(document.Patterns.Count);
        foreach (Pattern pattern in document.Patterns)
        {
            long end = 0;
            foreach (Note note in pattern.Notes)
            {
                end = Math.Max(end, note.Tick + note.LengthTicks);
            }
            patternEnds[pattern.Id] = end;
        }

        var channelById = new Dictionary<int, Channel>(document.Channels.Count);
        foreach (Channel channel in document.Channels)
        {
            channelById[channel.Id] = channel;
        }

        var voices = new List<RenderVoice>();
        long totalTicks = 0;
        foreach (Clip clip in document.Playlist.OrderBy(c => c.StartTick))
        {
            if (!patternEnds.TryGetValue(clip.PatternId, out long patternEnd) || patternEnd <= 0)
            {
                continue;
            }
            totalTicks = Math.Max(totalTicks, clip.StartTick + patternEnd);
            Pattern pattern = document.Patterns.First(p => p.Id == clip.PatternId);
            foreach (Note note in pattern.Notes)
            {
                Channel channel = channelById[note.ChannelId];
                (float panLeft, float panRight) = PanLaw.Linear(channel.Pan);
                double gain = note.Velocity * channel.Volume;
                double frequency = 440.0 * Math.Pow(2.0, (note.Key - 69) / 12.0);
                int assetIndex = -1;
                if (channel.Kind == ChannelKind.Sampler)
                {
                    if (channel.AssetId is null || !assetIndexById.TryGetValue(channel.AssetId, out assetIndex))
                    {
                        throw new InvalidOperationException($"sampler channel {channel.Id} 缺少已准备的资产");
                    }
                    // MIDI 60 = 原速；key 偏移即播放速率
                    frequency = Math.Pow(2.0, (note.Key - 60) / 12.0);
                }
                voices.Add(new RenderVoice
                {
                    StartSample = TickMath.TicksToSamples(clip.StartTick + note.Tick, num, den),
                    LengthSamples = Math.Max(1, TickMath.TicksToSamples(note.LengthTicks, num, den)),
                    Kind = channel.Kind == ChannelKind.Sampler ? RenderVoiceKind.Sampler : RenderVoiceKind.Sine,
                    Frequency = (float)frequency,
                    GainLeft = (float)(gain * panLeft),
                    GainRight = (float)(gain * panRight),
                    AssetIndex = assetIndex,
                });
            }
        }

        RenderVoice[] sorted = voices.OrderBy(v => v.StartSample).ToArray();
        var planAssets = new PlanAsset[assets.Count];
        for (int i = 0; i < assets.Count; i++)
        {
            planAssets[i] = new PlanAsset
            {
                Samples = assets[i].Interleaved,
                Channels = assets[i].Channels,
                Frames = assets[i].Frames,
            };
        }
        return new RenderPlan
        {
            SampleRate = document.SampleRate,
            Channels = 2,
            MasterGain = 1f,
            TotalSamples = TickMath.TicksToSamples(totalTicks, num, den),
            Voices = sorted,
            Assets = planAssets,
        };
    }
}
