namespace Weftone.Abstractions;

/// <summary>引擎填充交错的 float 输出缓冲；实现方会在音频实时线程上调用，回调内禁止分配/锁/异常。</summary>
public delegate void AudioRenderCallback(Span<float> interleavedOutput);

/// <summary>音频输出后端端口；所有实现不得把第三方类型泄漏给 Engine。</summary>
public interface IAudioBackend : IDisposable
{
    void Open(AudioDeviceSpec spec, AudioRenderCallback callback);

    void Start();

    void Stop();
}
