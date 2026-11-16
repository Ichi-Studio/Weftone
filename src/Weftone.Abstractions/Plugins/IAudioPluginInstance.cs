namespace Weftone.Abstractions.Plugins;

public enum PluginEventKind : byte
{
    NoteOff = 0,
    NoteOn = 1,
    /// <summary>参数值变化（SPEC M3 竖切 4 D6：编译期确定性事件点）。</summary>
    ParamValue = 2,
}

/// <summary>插件事件（frame 相对当次 process 块起点，升序由编译产物保证）。</summary>
public readonly record struct PluginEvent(
    PluginEventKind Kind,
    int FrameOffset,
    int Key,
    int Channel,
    float Velocity,
    uint ParamId,
    double ParamValue);

/// <summary>插件参数描述（竖切 3）：id/name/值域/默认值/可自动化标志（自动化用，竖切 4）。</summary>
public sealed record PluginParameterInfo(
    uint Id, string Name, double MinValue, double MaxValue, double DefaultValue, bool IsAutomatable);

/// <summary>
/// 音频线程调用的插件实例端口（SPEC M3 D5）：实现必须 RT 安全（零分配），
/// 输出平面 float 缓冲由调用方提供；实例生命周期归引擎侧实例表（RCU 换表）。
/// 参数/状态（竖切 3）：SetParameter 于下次 process 头部派发（非 RT 调用）；
/// SaveState/LoadState 走 main-thread 约定（非 RT）。
/// </summary>
public interface IAudioPluginInstance : IDisposable
{
    /// <summary>创建采样率（采样率变化须重建实例）。</summary>
    double SampleRate { get; }

    /// <summary>插件报告的延迟样本数（M3 只报告不补偿）。</summary>
    uint LatencySamples { get; }

    /// <summary>参数描述（创建时快照）。</summary>
    IReadOnlyList<PluginParameterInfo> Parameters { get; }

    void Process(Span<float> left, Span<float> right, ReadOnlySpan<PluginEvent> events);

    void SetParameter(uint paramId, double value);

    /// <summary>导出二进制状态；插件无状态扩展返回 null。</summary>
    byte[]? SaveState();

    /// <summary>恢复状态；失败（chunk 失配等）返回 false，实例保持可用。</summary>
    bool LoadState(byte[] data);
}

/// <summary>插件实例工厂端口（SPEC M3 D5）：按 (clapPath, pluginId, sampleRate) 创建实例，实现经 adapter。</summary>
public interface IPluginInstanceFactory
{
    IAudioPluginInstance Create(string clapPath, string pluginId, double sampleRate);
}
