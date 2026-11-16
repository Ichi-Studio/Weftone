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
    private readonly ClapBridgeNative.EventNative[] _scratch = new ClapBridgeNative.EventNative[MaxEventsPerBlock];

    private ClapPluginInstance(nint instance, double sampleRate)
    {
        _instance = instance;
        _sampleRate = sampleRate;
        Parameters = ClapBridgeNative.GetParameters(instance);
        LatencySamples = ClapBridgeNative.GetLatency(instance);
    }

    public double SampleRate => _sampleRate;

    public uint LatencySamples { get; }

    public IReadOnlyList<PluginParameterInfo> Parameters { get; }

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

    public unsafe void Process(Span<float> left, Span<float> right, ReadOnlySpan<PluginEvent> events)
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
            PluginEvent source = events[i];
            _scratch[i] = new ClapBridgeNative.EventNative
            {
                Frame = source.FrameOffset,
                Kind = (int)source.Kind,
                Key = source.Key,
                Channel = source.Channel,
                Velocity = source.Velocity,
                ParamId = source.ParamId,
                Value = source.ParamValue,
            };
        }
        ClapBridgeNative.ProcessInstance(instance, left, right, _scratch.AsSpan(0, count));
    }

    public void SetParameter(uint paramId, double value)
    {
        nint instance = _instance;
        ObjectDisposedException.ThrowIf(instance == 0, this);
        ClapBridgeNative.SetParameter(instance, paramId, value);
    }

    public byte[]? SaveState()
    {
        nint instance = _instance;
        ObjectDisposedException.ThrowIf(instance == 0, this);
        return ClapBridgeNative.SaveState(instance);
    }

    public bool LoadState(byte[] data)
    {
        nint instance = _instance;
        ObjectDisposedException.ThrowIf(instance == 0, this);
        return ClapBridgeNative.LoadState(instance, data);
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
