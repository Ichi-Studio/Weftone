using Weftone.Abstractions.Rendering;
using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 编译器（ADR-0003）：ProjectDocument → RenderPlan，非实时线程执行，全量编译。
/// 同 Document ⇒ 同 RenderPlan（事件按 StartSample 稳定排序，逐位确定）。
/// </summary>
public sealed class DocumentCompiler
{
    public RenderPlan Compile(ProjectDocument document)
    {
        (long num, long den) = TickMath.SamplesPerTick(document.SampleRate, document.Tempo);

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
                voices.Add(new RenderVoice
                {
                    StartSample = TickMath.TicksToSamples(clip.StartTick + note.Tick, num, den),
                    LengthSamples = Math.Max(1, TickMath.TicksToSamples(note.LengthTicks, num, den)),
                    Kind = channel.Kind == ChannelKind.Sampler ? RenderVoiceKind.Sampler : RenderVoiceKind.Sine,
                    Frequency = (float)frequency,
                    GainLeft = (float)(gain * panLeft),
                    GainRight = (float)(gain * panRight),
                    AssetIndex = -1,
                });
            }
        }

        RenderVoice[] sorted = voices.OrderBy(v => v.StartSample).ToArray();
        return new RenderPlan
        {
            SampleRate = document.SampleRate,
            Channels = 2,
            MasterGain = 1f,
            TotalSamples = TickMath.TicksToSamples(totalTicks, num, den),
            Voices = sorted,
        };
    }
}
