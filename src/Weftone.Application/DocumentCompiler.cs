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
        Dictionary<int, int> pluginSlotByChannel = BuildPluginSlots(document, out PlanPluginSlot[] pluginSlots);
        RenderPluginParamEvent[] pluginParamEvents =
            BuildPluginParamEvents(document, pluginSlotByChannel, totalTicks, num, den);
        var channelIndexById = new Dictionary<int, int>();
        Abstractions.Plugins.RenderEffectNode[] effectNodes =
            BuildEffectNodes(document, channelIndexById, totalTicks, num, den);
        var voices = new List<RenderVoice>();
        foreach (Clip clip in document.Playlist.OrderBy(c => c.StartTick))
        {
            if (!patternEnds.TryGetValue(clip.PatternId, out long patternEnd) || patternEnd <= 0)
            {
                continue;
            }
            Pattern pattern = document.Patterns.First(p => p.Id == clip.PatternId);
            AddPatternVoices(pattern, clip.StartTick, channelById, assetIndexById,
                curveIndexByChannel, pluginSlotByChannel, assets, document,
                channelIndexById, num, den, voices);
        }
        return BuildPlan(document, assets, voices, totalTicks, loop: false, num, den, curves,
            BuildMasterCurve(document, totalTicks, num, den), pluginSlots, pluginParamEvents,
            effectNodes, document.Channels.Count);
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
        Dictionary<int, int> pluginSlotByChannel = BuildPluginSlots(document, out PlanPluginSlot[] pluginSlots);
        RenderPluginParamEvent[] pluginParamEvents =
            BuildPluginParamEvents(document, pluginSlotByChannel, lengthTicks, num, den);
        var channelIndexById = new Dictionary<int, int>();
        Abstractions.Plugins.RenderEffectNode[] effectNodes =
            BuildEffectNodes(document, channelIndexById, lengthTicks, num, den);
        var voices = new List<RenderVoice>();
        AddPatternVoices(pattern, 0, channelById, assetIndexById, curveIndexByChannel, pluginSlotByChannel, assets, document, channelIndexById, num, den, voices);
        return BuildPlan(document, assets, voices, lengthTicks, loop: true, num, den, curves,
            BuildMasterCurve(document, lengthTicks, num, den), pluginSlots, pluginParamEvents,
            effectNodes, document.Channels.Count);
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
            // 原版（Speed=1）优先为默认映射；变体经 SelectVariantAssetIndex 解析
            if (assets[i].Speed == 1.0 || !assetIndexById.ContainsKey(assets[i].AssetId))
            {
                assetIndexById[assets[i].AssetId] = i;
            }
        }
        return assetIndexById;
    }

    /// <summary>M4 D3/D6：通道速度 → 最近档变体索引（无变体回退原版）。</summary>
    private static int SelectVariantAssetIndex(IReadOnlyList<PreparedAsset> assets,
        Dictionary<string, int> assetIndexById, string assetId, double stretchSpeed)
    {
        double step = AssetPreparer.NearestStretchStep(stretchSpeed);
        if (Math.Abs(step - 1.0) <= 1e-9)
        {
            return assetIndexById[assetId];
        }
        int baseIndex = assetIndexById[assetId];
        string baseAssetId = assets[baseIndex].AssetId;
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i].AssetId == baseAssetId && Math.Abs(assets[i].Speed - step) <= 1e-9)
            {
                return i;
            }
        }
        return baseIndex;
    }

    /// <summary>通道效果链 → plan 效果节点（SPEC M5 D2）：仅含带效果链的通道；
    /// channelIndexById 记录通道 id → 通道索引（voice.ChannelIndex 用）。</summary>
    private static Abstractions.Plugins.RenderEffectNode[] BuildEffectNodes(
        ProjectDocument document, Dictionary<int, int> channelIndexById,
        long totalTicks, long num, long den)
    {
        var nodes = new List<Abstractions.Plugins.RenderEffectNode>();
        int index = 0;
        foreach (Channel channel in document.Channels)
        {
            if (channel.Effects.Slots.Count == 0)
            {
                continue;
            }
            channelIndexById[channel.Id] = index++;
            foreach (EffectSlot slot in channel.Effects.Slots)
            {
                double[] parameters = CompileEffectParameters(slot);
                GainBreakpoint[][]? curves = CompileEffectParameterCurves(slot, parameters,
                    totalTicks, num, den);
                nodes.Add(new Abstractions.Plugins.RenderEffectNode
                {
                    ChannelIndex = channelIndexById[channel.Id],
                    Kind = slot.Kind.ToString(),
                    Parameters = parameters,
                    ParameterCurves = curves,
                    Bypassed = slot.Bypassed,
                });
            }
        }
        return [.. nodes];
    }

    /// <summary>效果参数 lane → 每参数断点曲线（SPEC M5 D3）：lane 非空的参数位得曲线
    /// （Sample 域 clamp 首末），否则 null（该位静态值）。参数名→序由 CompileEffectParameters 序。</summary>
    private static GainBreakpoint[][]? CompileEffectParameterCurves(EffectSlot slot,
        double[] staticParameters, long totalTicks, long num, long den)
    {
        string[] names = EffectParameterNames(slot.Kind);
        GainBreakpoint[][]? curves = null;
        foreach (string name in slot.AutomatedParameterNames)
        {
            List<AutomationPoint> lane = slot.GetParameterLane(name);
            if (lane.Count == 0)
            {
                continue;
            }
            int order = Array.IndexOf(names, name);
            if (order < 0 || order >= staticParameters.Length)
            {
                continue;
            }
            curves ??= new GainBreakpoint[staticParameters.Length][];
            long totalSamples = TickMath.TicksToSamples(totalTicks, num, den);
            double fallback = staticParameters[order];
            curves[order] = BuildEffectCurve(lane, fallback, totalTicks, totalSamples, num, den);
        }
        return curves;
    }

    /// <summary>效果参数曲线：并集点 + 首末 clamp（GainLeft 存参数值，GainRight 冗余同值）。</summary>
    private static GainBreakpoint[] BuildEffectCurve(List<AutomationPoint> lane, double fallback,
        long totalTicks, long totalSamples, long num, long den)
    {
        var breakpoints = new List<GainBreakpoint>(lane.Count + 2)
        {
            new GainBreakpoint { Sample = 0, GainLeft = (float)AutomationMath.Evaluate(lane, fallback, 0) },
        };
        foreach (AutomationPoint point in lane)
        {
            long sample = TickMath.TicksToSamples(point.Tick, num, den);
            if (sample <= 0 || (totalSamples > 0 && sample >= totalSamples))
            {
                continue;
            }
            breakpoints.Add(new GainBreakpoint { Sample = sample, GainLeft = (float)point.Value });
        }
        if (totalSamples > 0)
        {
            double endValue = AutomationMath.Evaluate(lane, fallback, totalTicks);
            if (breakpoints[^1].Sample >= totalSamples)
            {
                breakpoints[^1] = new GainBreakpoint { Sample = totalSamples, GainLeft = (float)endValue };
            }
            else
            {
                breakpoints.Add(new GainBreakpoint { Sample = totalSamples, GainLeft = (float)endValue });
            }
        }
        for (int i = 0; i < breakpoints.Count; i++)
        {
            breakpoints[i] = new GainBreakpoint
            {
                Sample = breakpoints[i].Sample,
                GainLeft = breakpoints[i].GainLeft,
                GainRight = breakpoints[i].GainLeft,
            };
        }
        return [.. breakpoints];
    }

    /// <summary>效果参数定序名（与 CompileEffectParameters 对齐）。</summary>
    private static string[] EffectParameterNames(EffectKind kind) => kind switch
    {
        EffectKind.Eq3Band => ["low", "mid", "high"],
        EffectKind.Compressor => ["threshold", "ratio", "attack", "release"],
        EffectKind.Delay => ["timeMs", "feedback", "mix"],
        EffectKind.Reverb => ["decaySeconds", "size", "preDelayMs"],
        _ => [],
    };

    /// <summary>效果槽参数 → plan 定序数组（Eq3Band: [low, mid, high] dB，缺省 0）。</summary>
    private static double[] CompileEffectParameters(EffectSlot slot)
    {
        double Get(string key) => slot.Parameters.TryGetValue(key, out double value) ? value : 0;
        return slot.Kind switch
        {
            EffectKind.Eq3Band => [Get("low"), Get("mid"), Get("high")],
            EffectKind.Compressor => [Get("threshold"), Get("ratio"), Get("attack"), Get("release")],
            EffectKind.Delay => [Get("timeMs"), Get("feedback"), Get("mix")],
            EffectKind.Reverb => [Get("decaySeconds"), Get("size"), Get("preDelayMs")],
            _ => [],
        };
    }

    /// <summary>资产切片点（SPEC M4 D4）；资产缺失返回空表。</summary>
    private static List<long> SlicePointsOf(ProjectDocument document, string? assetId)
    {
        if (assetId is null)
        {
            return [];
        }
        AssetRef? asset = document.Assets.FirstOrDefault(a => a.Id == assetId);
        return asset?.SlicePoints ?? [];
    }

    /// <summary>Plugin 通道 → 每通道一槽（各自独立实例）；引用缺失抛错（工程不完整）。</summary>
    private static Dictionary<int, int> BuildPluginSlots(ProjectDocument document, out PlanPluginSlot[] slots)
    {
        var slotByChannel = new Dictionary<int, int>();
        var list = new List<PlanPluginSlot>();
        foreach (Channel channel in document.Channels)
        {
            if (channel.Kind != ChannelKind.Plugin)
            {
                continue;
            }
            PluginRef reference = document.Plugins.FirstOrDefault(p => p.Id == channel.PluginId)
                ?? throw new InvalidOperationException(
                    $"plugin channel {channel.Id} 引用未知插件 {channel.PluginId}");
            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                throw new InvalidOperationException($"插件 {reference.Id} 缺少 .clap 路径");
            }
            slotByChannel[channel.Id] = list.Count;
            list.Add(new PlanPluginSlot(
                reference.Id, reference.Path, channel.Id,
                [.. channel.PluginParameters.Select(p => new PlanPluginParam(p.Id, p.Value))]));
        }
        slots = [.. list];
        return slotByChannel;
    }

    /// <summary>插件参数 lane → 事件点（SPEC M3 竖切 4 D6）：sample 0 注入首值（clamp），
    /// 其后每点一事件（阶梯语义——点间斜坡/逐块插值密度留 M5 打磨，SPEC 风险记录）；
    /// 按 Sample 升序、平局按 (Slot, ParamId) 稳定排序。</summary>
    private static RenderPluginParamEvent[] BuildPluginParamEvents(
        ProjectDocument document, Dictionary<int, int> pluginSlotByChannel, long totalTicks, long num, long den)
    {
        var events = new List<RenderPluginParamEvent>();
        foreach (Channel channel in document.Channels)
        {
            if (channel.Kind != ChannelKind.Plugin || !pluginSlotByChannel.TryGetValue(channel.Id, out int slot))
            {
                continue;
            }
            foreach (uint paramId in channel.AutomatedPluginParamIds)
            {
                List<AutomationPoint> lane = channel.GetPluginParamPoints(paramId);
                if (lane.Count == 0)
                {
                    continue;
                }
                events.Add(new RenderPluginParamEvent
                {
                    Slot = slot,
                    ParamId = paramId,
                    Sample = 0,
                    Value = lane[0].Value,
                });
                foreach (AutomationPoint point in lane)
                {
                    long sample = TickMath.TicksToSamples(point.Tick, num, den);
                    if (sample <= 0)
                    {
                        continue; // 首点已由 sample 0 事件覆盖
                    }
                    events.Add(new RenderPluginParamEvent
                    {
                        Slot = slot,
                        ParamId = paramId,
                        Sample = sample,
                        Value = point.Value,
                    });
                }
            }
        }
        return [.. events.OrderBy(e => e.Sample).ThenBy(e => e.Slot).ThenBy(e => e.ParamId)];
    }

    private static void AddPatternVoices(Pattern pattern, long startTickOffset,
        Dictionary<int, Channel> channelById, Dictionary<string, int> assetIndexById,
        Dictionary<int, int> curveIndexByChannel, Dictionary<int, int> pluginSlotByChannel,
        IReadOnlyList<PreparedAsset> assets, ProjectDocument document,
        Dictionary<int, int> channelIndexById, long num, long den, List<RenderVoice> voices)
    {
        foreach (Note note in pattern.Notes)
        {
            Channel channel = channelById[note.ChannelId];
            (float panLeft, float panRight) = PanLaw.Linear(channel.Pan);
            double gain = note.Velocity * channel.Volume;
            double frequency = 440.0 * Math.Pow(2.0, (note.Key - 69) / 12.0);
            int assetIndex = -1;
            long assetOffsetFrames = 0;
            if (channel.Kind == ChannelKind.Sampler)
            {
                if (channel.AssetId is null || !assetIndexById.TryGetValue(channel.AssetId, out assetIndex))
                {
                    throw new InvalidOperationException($"sampler channel {channel.Id} 缺少已准备的资产");
                }
                // MIDI 60 = 原速；key 偏移即播放速率；变体（Speed=s）以 s 倍速还原时长（M4）
                double speed = AssetPreparer.NearestStretchStep(channel.StretchSpeed);
                assetIndex = SelectVariantAssetIndex(assets, assetIndexById, channel.AssetId, channel.StretchSpeed);
                frequency = Math.Pow(2.0, (note.Key - 60) / 12.0) * speed;
                if (channel.SliceMode)
                {
                    // 切片演奏（SPEC M4 D5）：key=60+i → 第 i 个切片；切片帧域与拉伸档位互斥
                    int sliceIndex = note.Key - 60;
                    List<long> slices = SlicePointsOf(document, channel.AssetId);
                    if (sliceIndex < 0 || sliceIndex >= slices.Count)
                    {
                        continue; // 无对应切片：不出声（优雅跳过）
                    }
                    assetOffsetFrames = slices[sliceIndex];
                }
            }
            if (channel.Kind == ChannelKind.Plugin)
            {
                // 插件声部：note on/off 区间事件；增益=通道 vol/pan（velocity 由插件自理），
                // 自动化曲线本竖切不作用于插件输出（静态值，精度提升留竖切 4）
                voices.Add(new RenderVoice
                {
                    StartSample = TickMath.TicksToSamples(startTickOffset + note.Tick, num, den),
                    LengthSamples = Math.Max(1, TickMath.TicksToSamples(note.LengthTicks, num, den)),
                    Kind = RenderVoiceKind.Plugin,
                    GainLeft = (float)(channel.Volume * panLeft),
                    GainRight = (float)(channel.Volume * panRight),
                    PluginSlot = pluginSlotByChannel[channel.Id],
                    NoteKey = note.Key,
                    Velocity = (float)note.Velocity,
                    ChannelIndex = channelIndexById.TryGetValue(channel.Id, out int pluginFxIndex) ? pluginFxIndex : -1,
                });
                continue;
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
                AssetOffsetFrames = assetOffsetFrames,
                ChannelIndex = channelIndexById.TryGetValue(channel.Id, out int fxIndex) ? fxIndex : -1,
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
        GainBreakpoint[][] channelGainCurves, GainBreakpoint[]? masterGainCurve,
        PlanPluginSlot[] pluginSlots, RenderPluginParamEvent[] pluginParamEvents,
        Abstractions.Plugins.RenderEffectNode[] effectNodes, int channelCount)
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
            PluginSlots = pluginSlots,
            PluginParamEvents = pluginParamEvents,
            EffectNodes = effectNodes,
            ChannelCount = channelCount,
        };
    }
}
