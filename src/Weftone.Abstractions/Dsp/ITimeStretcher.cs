namespace Weftone.Abstractions.Dsp;

/// <summary>
/// 离线时间拉伸端口（SPEC M4 D2/D3，ADR-0004 wheel-first）：仅 AssetPreparer
/// 预渲染路径使用，非 RT。实现须确定性：同输入同参数位级一致（进程内）。
/// </summary>
public interface ITimeStretcher
{
    /// <summary>整段拉伸：durationRatio&gt;1 = 更长（慢速），不变调；
    /// 返回交错 f32（帧数 = floor(frames × ratio)，输出缓冲充足时）。</summary>
    float[] Stretch(ReadOnlySpan<float> interleaved, int channels, int sampleRate, double durationRatio);
}
