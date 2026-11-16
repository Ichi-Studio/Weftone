namespace Weftone.Abstractions.Plugins;

public enum PluginNoteEventKind : byte
{
    On = 0,
    Off = 1,
}

/// <summary>插件音符事件（frame 相对当次 process 块起点，升序由编译产物保证）。</summary>
public readonly record struct PluginNoteEvent(
    PluginNoteEventKind Kind, int FrameOffset, int Key, int Channel, float Velocity);

/// <summary>
/// 音频线程调用的插件实例端口（SPEC M3 D5）：实现必须 RT 安全（零分配），
/// 输出平面 float 缓冲由调用方提供；实例生命周期归引擎侧实例表（RCU 换表）。
/// </summary>
public interface IAudioPluginInstance : IDisposable
{
    /// <summary>创建采样率（采样率变化须重建实例）。</summary>
    double SampleRate { get; }

    void Process(Span<float> left, Span<float> right, ReadOnlySpan<PluginNoteEvent> events);
}

/// <summary>插件实例工厂端口（SPEC M3 D5）：按 (clapPath, pluginId, sampleRate) 创建实例，实现经 adapter。</summary>
public interface IPluginInstanceFactory
{
    IAudioPluginInstance Create(string clapPath, string pluginId, double sampleRate);
}
