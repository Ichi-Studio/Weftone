using Weftone.Abstractions;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;

namespace Weftone.Application;

/// <summary>
/// 实时播放用例（M1b D1=a 实时播放 / M2 竖切 4 RCU / M3 插件实例表）。
/// Load 只在后端停止后调用（ma_device_stop 同步 drain），音频线程渲染期间绝不 Load，零并发。
/// 回调一次性捕获，音频路径零分配（RtAssert 守护见 Engine 测试）。
/// 后端经工厂创建：工程采样率变化时重开（weft_audio_open 已开则拒绝）。
/// 插件：pluginFactory 注入时按 plan 槽表同步实例（跨编辑复用，SPEC M3 D5）。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private const int BufferFrames = 256;

    private readonly Func<IAudioBackend> _backendFactory;
    private readonly IPlanRenderer _renderer;
    private readonly AudioRenderCallback _callback;
    private readonly PluginInstanceHost? _plugins;
    private IAudioBackend? _backend;
    private int _openSampleRate;
    private bool _playing;
    private long _totalSamples;

    public PlaybackService(Func<IAudioBackend> backendFactory, IPlanRenderer renderer,
        IPluginInstanceFactory? pluginFactory = null)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(renderer);
        _backendFactory = backendFactory;
        _renderer = renderer;
        _callback = renderer.Render;
        _plugins = pluginFactory is null ? null : new PluginInstanceHost(pluginFactory);
    }

    public bool IsPlaying => _playing;

    /// <summary>是否带插件实例宿主（保存前 state 抓取判定，竖切 3）。</summary>
    public bool HasPluginHost => _plugins is not null;

    /// <summary>当前 plan 是否带循环区间（Pattern 模式）。</summary>
    public bool IsLooping { get; private set; }

    public long Position => _renderer.Position;

    public long TotalSamples => _totalSamples;

    /// <summary>注入工程 state chunk（竖切 3）：打开工程/恢复后、Play 前调用。</summary>
    public void RestorePluginStates(IReadOnlyDictionary<int, byte[]> states)
        => _plugins?.ApplyChannelStates(states);

    /// <summary>抓取当前实例 state（保存工程用）；无插件宿主时返回空表。</summary>
    public IReadOnlyDictionary<int, byte[]> CapturePluginStates(RenderPlan plan)
        => _plugins?.CaptureChannelStates(plan) ?? new Dictionary<int, byte[]>();

    public void Play(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Stop();
        _renderer.Load(plan, _plugins?.Sync(plan));
        _totalSamples = plan.TotalSamples;
        IsLooping = plan.LoopEndSample > plan.LoopStartSample;
        EnsureOpen(plan.SampleRate, plan.Channels);
        _backend!.Start();
        _playing = true;
    }

    public void Stop()
    {
        if (_playing)
        {
            _backend!.Stop();
            _playing = false;
        }
    }

    /// <summary>编辑后热更新（M2 竖切 4 RCU）：不停后端直接换 plan（含插件实例表），位置连续无静音缺口；
    /// 采样率变化无法在位交换，回退停止-交换-恢复。</summary>
    public void UpdatePlan(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        IReadOnlyList<Abstractions.Plugins.IAudioPluginInstance>? table = _plugins?.Sync(plan);
        if (_playing && _backend is not null && _openSampleRate != plan.SampleRate)
        {
            _backend.Stop();
            _renderer.Load(plan, table);
            EnsureOpen(plan.SampleRate, plan.Channels);
            _backend!.Start();
        }
        else
        {
            _renderer.SwapPlan(plan, table);
        }
        _totalSamples = plan.TotalSamples;
        IsLooping = plan.LoopEndSample > plan.LoopStartSample;
    }

    private void EnsureOpen(int sampleRate, int channels)
    {
        if (_backend is not null && _openSampleRate == sampleRate)
        {
            return;
        }
        _backend?.Dispose();
        _backend = _backendFactory();
        _backend.Open(new AudioDeviceSpec(sampleRate, channels, BufferFrames), _callback);
        _openSampleRate = sampleRate;
    }

    public void Dispose()
    {
        Stop();
        _backend?.Dispose();
        _backend = null;
    }
}