using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;

namespace Weftone.Application;

/// <summary>
/// 实时播放用例（M1b D1=a 实时播放 / D2=a 停止-交换-恢复）。
/// Load 只在后端停止后调用（ma_device_stop 同步 drain），音频线程渲染期间绝不 Load，零并发。
/// 回调一次性捕获，音频路径零分配（RtAssert 守护见 Engine 测试）。
/// 后端经工厂创建：工程采样率变化时重开（weft_audio_open 已开则拒绝）。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private const int BufferFrames = 256;

    private readonly Func<IAudioBackend> _backendFactory;
    private readonly IPlanRenderer _renderer;
    private readonly AudioRenderCallback _callback;
    private IAudioBackend? _backend;
    private int _openSampleRate;
    private bool _playing;
    private long _totalSamples;

    public PlaybackService(Func<IAudioBackend> backendFactory, IPlanRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(renderer);
        _backendFactory = backendFactory;
        _renderer = renderer;
        _callback = renderer.Render;
    }

    public bool IsPlaying => _playing;

    public long Position => _renderer.Position;

    public long TotalSamples => _totalSamples;

    public void Play(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Stop();
        _renderer.Load(plan);
        _totalSamples = plan.TotalSamples;
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

    /// <summary>编辑后热更新：停止-交换-恢复，保留播放状态（短暂静音换零并发）。</summary>
    public void UpdatePlan(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool wasPlaying = _playing;
        Stop();
        _renderer.Load(plan);
        _totalSamples = plan.TotalSamples;
        if (wasPlaying)
        {
            EnsureOpen(plan.SampleRate, plan.Channels);
            _backend!.Start();
            _playing = true;
        }
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