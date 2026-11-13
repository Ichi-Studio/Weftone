namespace Weftone.Abstractions.Rendering;

public enum RenderVoiceKind : byte
{
    Sine = 0,
    Sampler = 1,
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
}
