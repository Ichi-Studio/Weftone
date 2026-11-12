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
}

/// <summary>扁平实时计划（ADR-0003）：预分配数组、无对象图、无第三方类型。</summary>
public sealed class RenderPlan
{
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public float MasterGain { get; init; } = 1f;
    public required long TotalSamples { get; init; }
    public required RenderVoice[] Voices { get; init; }
}
