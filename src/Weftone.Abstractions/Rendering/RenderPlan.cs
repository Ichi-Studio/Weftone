namespace Weftone.Abstractions.Rendering;

public enum RenderVoiceKind : byte
{
    Sine = 0,
    Sampler = 1,
    /// <summary>CLAP 插件乐器（SPEC M3）：note on/off 区间事件驱动插件实例，PluginSlot 指向槽表。</summary>
    Plugin = 2,
}

/// <summary>单次触发事件：全部参数离线预乘，渲染期只读、零计算负担外置。</summary>
public struct RenderVoice
{
    public long StartSample;
    public long LengthSamples;
    public RenderVoiceKind Kind;
    public float Frequency;
    public float GainLeft;
    public float GainRight;
    public int AssetIndex;
    /// <summary>通道增益曲线索引（SPEC M2）：-1=静态 GainLeft/GainRight（无自动化通道，行为不变）；
    /// ≥0 时 GainLeft 持 velocity，通道 vol/pan 增益改由曲线逐样本插值。</summary>
    public int GainCurveIndex = -1;
    /// <summary>插件槽索引（SPEC M3）：Kind==Plugin 时有效，指向 RenderPlan.PluginSlots。</summary>
    public int PluginSlot = -1;
    /// <summary>MIDI key（0..127，60=中央 C），Kind==Plugin 时传给插件 note 事件。</summary>
    public int NoteKey;
    /// <summary>音符力度（0..1），Kind==Plugin 时传给插件 note 事件。</summary>
    public float Velocity;
    /// <summary>资产内起始帧（SPEC M4 竖切 2：切片起播偏移）；Kind==Sampler 时生效，0 = 从头播。</summary>
    public long AssetOffsetFrames;

    public RenderVoice()
    {
    }
}

/// <summary>增益断点（SPEC M2）：Sample 升序，GainLeft/Right 已按 vol×pan 预乘；渲染期分段线性插值。</summary>
public struct GainBreakpoint
{
    public long Sample;
    public float GainLeft;
    public float GainRight;
}

/// <summary>插件静态参数值（竖切 3）：实例创建/换表时经 SetParameter 应用（D6：自动化事件点留竖切 4）。</summary>
public sealed record PlanPluginParam(uint Id, double Value);

/// <summary>插件槽（SPEC M3 D5）：plan 声部的 PluginSlot 索引此处；
/// 实例由引擎侧实例表按 (PluginId, Path) 复用/创建（RCU 换表），plan 保持纯数据。</summary>
public sealed record PlanPluginSlot(
    string PluginId, string FilePath, int ChannelId, IReadOnlyList<PlanPluginParam> Params);

/// <summary>插件参数事件点（SPEC M3 竖切 4 D6）：lane 线性化产物，按 Sample 升序确定性派发。</summary>
public struct RenderPluginParamEvent
{
    public int Slot;
    public uint ParamId;
    public long Sample;
    public double Value;
}

/// <summary>采样资产数据：解码并重采样到工程采样率后的 f32 交错样本。</summary>
public sealed class PlanAsset
{
    public required float[] Samples { get; init; }
    public required int Channels { get; init; }
    public required long Frames { get; init; }
}

/// <summary>扁平实时计划（ADR-0003）：预分配数组、无富对象图、无第三方类型。</summary>
public sealed class RenderPlan
{
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public float MasterGain { get; init; } = 1f;
    public required long TotalSamples { get; init; }
    /// <summary>循环区间（Pattern 模式）：LoopEnd &gt; LoopStart 时启用，position 到 LoopEnd 回绕 LoopStart。</summary>
    public long LoopStartSample { get; init; }
    public long LoopEndSample { get; init; }
    public required RenderVoice[] Voices { get; init; }
    public PlanAsset[] Assets { get; init; } = [];
    /// <summary>通道增益曲线（SPEC M2）：每元素一条，Sample 升序且首末 clamp 到 [0, TotalSamples]；
    /// 空数组 = 全静态（现状行为）。voice.GainCurveIndex 索引。</summary>
    public GainBreakpoint[][] ChannelGainCurves { get; init; } = [];
    /// <summary>master volume 曲线（SPEC M2）：null = 静态 MasterGain（现状行为）；非 null 时 GainLeft==GainRight。</summary>
    public GainBreakpoint[]? MasterGainCurve { get; init; }
    /// <summary>插件槽表（SPEC M3）：voice.PluginSlot 的解析目标；空表 = 无插件声部。</summary>
    public PlanPluginSlot[] PluginSlots { get; init; } = [];
    /// <summary>插件参数事件点（SPEC M3 竖切 4）：按 Sample 升序；空 = 无参数自动化。</summary>
    public RenderPluginParamEvent[] PluginParamEvents { get; init; } = [];
}
