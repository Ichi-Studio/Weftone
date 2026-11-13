using Weftone.Abstractions.Rendering;
using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 编译器（ADR-0003）：ProjectDocument → RenderPlan，非实时线程执行，全量编译。
/// 同 Document ⇒ 同 RenderPlan（事件按 StartSample 稳定排序，逐位确定）。
/// Compile 展开 Playlist 线性时间轴；CompilePattern 编译单个 Pattern 并带循环区间（M1b Pattern 模式）。
/// </summary>
public sealed class DocumentCompiler
{
    private const long BarTicks = (long)TimeBase.Ppq * 4;

    /// <summary>仅 sine 声部的工程可不带资产编译。</summary>
    public RenderPlan Compile(ProjectDocument document) => Compile(document, []);

    /// <summary>sampler 声部需要 PreparedAsset（已解码/重采样）；AssetIndex 指向 plan.Assets。</summary>
    public RenderPlan Compile(ProjectDocument document, IReadOnlyList<PreparedAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        (long num, long den) = TickMath.SamplesPerTick(document.SampleRate, document.Tempo);
        Dictionary<int, Channel> channelById = BuildChannelMap(document);
        Dictionary<string, int> assetIndexById = BuildAssetMap(assets);

        long totalTicks = 0;
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
        foreach (Clip clip in document.Playlist)
        {
            if (patternEnds.TryGetValue(clip.PatternId, out long patternEnd) && patternEnd > 0)
            {
                totalTicks = Math.Max(totalTicks, clip.StartTick + patternEnd);
            }
        }
        var curveIndexByChannel = new Dictionary<int, int>();
        GainBreakpoint[][] curves = BuildChannelCurves(document, totalTicks, num, den, curveIndexByChannel);
        var voices = new List<RenderVoice>();
        foreach (Clip clip in document.Playlist.OrderBy(c => c.StartTick))
        {
            if (!patternEnds.TryGetValue(clip.PatternId, out long patternEnd) || patternEnd <= 0)
            {
                continue;
            }
            Pattern pattern = document.Patterns.First(p => p.Id == clip.PatternId);
            AddPatternVoices(pattern, clip.StartTick, channelById, assetIndexById, curveIndexByChannel, num, den, voices);
        }
        return BuildPlan(document, assets, voices, totalTicks, loop: false, num, den, curves,
            BuildMasterCurve(document, totalTicks, num, den));
    }

    /// <summary>Pattern 模式：单个 Pattern 从 0 展开为声部，循环区间 = [0, 长度)，长度按小节向上取整（至少 1 小节）。</summary>
    public RenderPlan CompilePattern(ProjectDocument document, int patternId, IReadOnlyList<PreparedAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        (long num, long den) = TickMath.SamplesPerTick(document.SampleRate, document.Tempo);
        Dictionary<int, Channel> channelById = BuildChannelMap(document);
        Dictionary<string, int> assetIndexById = BuildAssetMap(assets);

        Pattern pattern = document.Patterns.FirstOrDefault(p => p.Id == patternId)
            ?? throw new InvalidOperationException($"pattern {patternId} 不存在");
        long lengthTicks = PatternLengthTicks(pattern);
        var curveIndexByChannel = new Dictionary<int, int>();
        GainBreakpoint[][] curves = BuildChannelCurves(document, lengthTicks, num, den, curveIndexByChannel);
        var voices = new List<RenderVoice>();
        AddPatternVoices(pattern, 0, channelById, assetIndexById, curveIndexByChannel, num, den, voices);
        return BuildPlan(document, assets, voices, lengthTicks, loop: true, num, den, curves,
            BuildMasterCurve(document, lengthTicks, num, den));
    }

    /// <summary>Pattern 循环长度：最大音符结束 tick 向上取整到小节，至少 1 小节（4 拍）。</summary>
    public static long PatternLengthTicks(Pattern pattern)
    {
        long end = 0;
        foreach (Note note in pattern.Notes)
        {
            end = Math.Max(end, note.Tick + note.LengthTicks);
        }
        return Math.Max(BarTicks, (end + BarTicks - 1) / BarTicks * BarTicks);
    }

    private static Dictionary<int, Channel> BuildChannelMap(ProjectDocument document)
    {
        var channelById = new Dictionary<int, Channel>(document.Channels.Count);
        foreach (Channel channel in document.Channels)
        {
            channelById[channel.Id] = channel;
        }
        return channelById;
    }

    private static Dictionary<string, int> BuildAssetMap(IReadOnlyList<PreparedAsset> assets)
    {
        var assetIndexById = new Dictionary<string, int>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            assetIndexById[assets[i].AssetId] = i;
        }
        return assetIndexById;
    }

    private static void AddPatternVoices(Pattern pattern, long startTickOffset,
        Dictionary<int, Channel> channelById, Dictionary<string, int> assetIndexById,
        Dictionary<int, int> curveIndexByChannel, long num, long den, List<RenderVoice> voices)
    {
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
            int curveIndex = curveIndexByChannel.TryGetValue(channel.Id, out int mapped) ? mapped : -1;
            voices.Add(new RenderVoice
            {
                StartSample = TickMath.TicksToSamples(startTickOffset + note.Tick, num, den),
                LengthSamples = Math.Max(1, TickMath.TicksToSamples(note.LengthTicks, num, den)),
                Kind = channel.Kind == ChannelKind.Sampler ? RenderVoiceKind.Sampler : RenderVoiceKind.Sine,
                Frequency = (float)frequency,
                // 曲线声部 GainLeft/Right 只持 velocity，通道 vol/pan 由曲线逐样本插值（SPEC M2）
                GainLeft = curveIndex >= 0 ? (float)note.Velocity : (float)(gain * panLeft),
                GainRight = curveIndex >= 0 ? (float)note.Velocity : (float)(gain * panRight),
                AssetIndex = assetIndex,
                GainCurveIndex = curveIndex,
            });
        }
    }

    /// <summary>自动化通道 → 增益曲线（SPEC M2）：vol/pan 两 lane 的 tick 并集逐点求值并预乘 L/R，
    /// 首（sample 0）末（totalSamples）clamp 断点由端点值/末点插值派生；无自动化通道不建曲线。</summary>
    private static GainBreakpoint[][] BuildChannelCurves(ProjectDocument document, long totalTicks,
        long num, long den, Dictionary<int, int> curveIndexByChannel)
    {
        var curves = new List<GainBreakpoint[]>();
        foreach (Channel channel in document.Channels)
        {
            if (channel.VolumePoints.Count == 0 && channel.PanPoints.Count == 0)
            {
                continue;
            }
            curves.Add(BuildCurve(channel.VolumePoints, channel.PanPoints,
                channel.Volume, channel.Pan, totalTicks, num, den));
            curveIndexByChannel[channel.Id] = curves.Count - 1;
        }
        return [.. curves];
    }

    private static GainBreakpoint[]? BuildMasterCurve(ProjectDocument document, long totalTicks, long num, long den)
        => document.MasterVolumePoints.Count == 0
            ? null
            : BuildCurve(document.MasterVolumePoints, [], document.MasterVolume, 0.0, totalTicks, num, den);

    private static GainBreakpoint[] BuildCurve(List<AutomationPoint> volume, List<AutomationPoint> pan,
        double staticVolume, double staticPan, long totalTicks, long num, long den)
    {
        long totalSamples = TickMath.TicksToSamples(totalTicks, num, den);
        var ticks = new List<long>(volume.Count + pan.Count + 1) { 0 };
        int v = 0, p = 0;
        while (v < volume.Count || p < pan.Count)
        {
            long tick;
            if (p >= pan.Count || (v < volume.Count && volume[v].Tick <= pan[p].Tick))
            {
                tick = volume[v].Tick;
                v++;
            }
            else
            {
                tick = pan[p].Tick;
                p++;
            }
            if (ticks[^1] != tick)
            {
                ticks.Add(tick);
            }
        }
        var breakpoints = new List<GainBreakpoint>(ticks.Count + 1);
        foreach (long tick in ticks)
        {
            long sample = TickMath.TicksToSamples(tick, num, den);
            if (totalSamples > 0 && sample >= totalSamples)
            {
                continue; // 时间轴外的点由末断点插值覆盖
            }
            GainBreakpoint bp = EvaluateBreakpoint(volume, pan, staticVolume, staticPan, tick, sample);
            if (breakpoints.Count > 0 && breakpoints[^1].Sample >= sample)
            {
                breakpoints[^1] = bp; // 不同 tick 截断到同 sample：后者覆盖（确定性）
            }
            else
            {
                breakpoints.Add(bp);
            }
        }
        if (totalSamples > 0)
        {
            GainBreakpoint end = EvaluateBreakpoint(volume, pan, staticVolume, staticPan, totalTicks, totalSamples);
            if (breakpoints.Count > 0 && breakpoints[^1].Sample >= totalSamples)
            {
                breakpoints[^1] = end;
            }
            else
            {
                breakpoints.Add(end);
            }
        }
        return [.. breakpoints];
    }

    private static GainBreakpoint EvaluateBreakpoint(List<AutomationPoint> volume, List<AutomationPoint> pan,
        double staticVolume, double staticPan, long tick, long sample)
    {
        double vol = AutomationMath.Evaluate(volume, staticVolume, tick);
        double p = AutomationMath.Evaluate(pan, staticPan, tick);
        (float panLeft, float panRight) = PanLaw.Linear(p);
        return new GainBreakpoint
        {
            Sample = sample,
            GainLeft = (float)(vol * panLeft),
            GainRight = (float)(vol * panRight),
        };
    }

    private static RenderPlan BuildPlan(ProjectDocument document, IReadOnlyList<PreparedAsset> assets,
        List<RenderVoice> voices, long totalTicks, bool loop, long num, long den,
        GainBreakpoint[][] channelGainCurves, GainBreakpoint[]? masterGainCurve)
    {
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
        long totalSamples = TickMath.TicksToSamples(totalTicks, num, den);
        return new RenderPlan
        {
            SampleRate = document.SampleRate,
            Channels = 2,
            MasterGain = (float)Math.Clamp(document.MasterVolume, 0, 2),
            TotalSamples = totalSamples,
            LoopStartSample = 0,
            LoopEndSample = loop ? totalSamples : 0,
            Voices = sorted,
            Assets = planAssets,
            ChannelGainCurves = channelGainCurves,
            MasterGainCurve = masterGainCurve,
        };
    }
}
