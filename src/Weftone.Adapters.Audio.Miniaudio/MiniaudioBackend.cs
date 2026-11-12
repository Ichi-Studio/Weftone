using System.Runtime.InteropServices;
using Weftone.Abstractions;

namespace Weftone.Adapters.Audio.Miniaudio;

/// <summary>
/// weftone_miniaudio shim 的 IAudioBackend 实现。音频线程经反向 P/Invoke 进入
/// <see cref="AudioRenderCallback"/>；委托与 GCHandle 在 Open 时一次性建立，
/// 回调路径零分配。
/// </summary>
public sealed unsafe class MiniaudioBackend : IAudioBackend
{
    private readonly GCHandle _self;
    private WeftRenderCallbackNative? _trampoline;
    private AudioRenderCallback? _callback;
    private bool _open;
    private bool _disposed;

    public MiniaudioBackend() => _self = GCHandle.Alloc(this);

    public void Open(AudioDeviceSpec spec, AudioRenderCallback callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        NativeProbe.EnsureInstalled();
        _callback = callback;
        _trampoline = Trampoline;
        int rc = WeftAudioNative.weft_audio_open(
            (uint)spec.SampleRate, (uint)spec.Channels, (uint)spec.BufferFrames,
            _trampoline, (void*)GCHandle.ToIntPtr(_self));
        if (rc != 0)
        {
            throw new InvalidOperationException($"weft_audio_open 失败 rc={rc}: {WeftAudioNative.GetLastError()}");
        }
        _open = true;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int rc = WeftAudioNative.weft_audio_start();
        if (rc != 0)
        {
            throw new InvalidOperationException($"weft_audio_start 失败 rc={rc}: {WeftAudioNative.GetLastError()}");
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int rc = WeftAudioNative.weft_audio_stop();
        if (rc != 0)
        {
            throw new InvalidOperationException($"weft_audio_stop 失败 rc={rc}: {WeftAudioNative.GetLastError()}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        if (_open)
        {
            WeftAudioNative.weft_audio_close();
            _open = false;
        }
        _trampoline = null;
        _callback = null;
        if (_self.IsAllocated)
        {
            _self.Free();
        }
        _disposed = true;
    }

    private static void Trampoline(float* output, uint frames, uint channels, void* userData)
    {
        var self = (MiniaudioBackend)GCHandle.FromIntPtr((nint)userData).Target!;
        var buffer = new Span<float>(output, checked((int)frames * (int)channels));
        self._callback!(buffer);
    }
}
