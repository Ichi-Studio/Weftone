using Weftone.Abstractions.Plugins;

namespace Weftone.Adapters.Plugins.Clap;

/// <summary>
/// CLAP 插件实例（SPEC M3 D5）：create 时已 activate + start_processing；
/// Process 在音频线程调用——事件转 native 用实例预分配 scratch，零分配。
/// </summary>
public sealed class ClapPluginInstance : IAudioPluginInstance
{
    private const int MaxEventsPerBlock = 256;
    private const string TestSineId = "io.github.ichi-studio.weftone.test-sine";

    private nint _instance;
    private readonly double _sampleRate;
    private readonly ClapBridgeNative.NoteEventNative[] _scratch = new ClapBridgeNative.NoteEventNative[MaxEventsPerBlock];

    private ClapPluginInstance(nint instance, double sampleRate)
    {
        _instance = instance;
        _sampleRate = sampleRate;
    }

    public double SampleRate => _sampleRate;

    /// <summary>测试插件的便捷 id（与 native/test_plugins/weftone_test_sine.cpp 对齐）。</summary>
    public static string TestSinePluginId => TestSineId;

    public static ClapPluginInstance Create(string clapPath, string pluginId, double sampleRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(clapPath);
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        nint handle = ClapBridgeNative.CreateInstance(clapPath, pluginId, sampleRate);
        return handle == 0
            ? throw new InvalidOperationException($"无法创建 CLAP 实例：{pluginId}（{Path.GetFileName(clapPath)}）")
            : new ClapPluginInstance(handle, sampleRate);
    }

    public unsafe void Process(Span<float> left, Span<float> right, ReadOnlySpan<PluginNoteEvent> events)
    {
        nint instance = _instance;
        ObjectDisposedException.ThrowIf(instance == 0, this);
        if (left.Length != right.Length)
        {
            return;
        }
        int count = Math.Min(events.Length, _scratch.Length);
        for (int i = 0; i < count; i++)
        {
            PluginNoteEvent source = events[i];
            _scratch[i] = new ClapBridgeNative.NoteEventNative
            {
                Frame = source.FrameOffset,
                IsOn = source.Kind == PluginNoteEventKind.On ? 1 : 0,
                Key = source.Key,
                Channel = source.Channel,
                Velocity = source.Velocity,
            };
        }
        ClapBridgeNative.ProcessInstance(instance, left, right, _scratch.AsSpan(0, count));
    }

    public void Dispose()
    {
        nint instance = Interlocked.Exchange(ref _instance, 0);
        if (instance != 0)
        {
            ClapBridgeNative.DestroyInstance(instance);
        }
    }
}
