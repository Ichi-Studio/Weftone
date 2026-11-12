using System.Runtime.InteropServices;

namespace Weftone.Adapters.Audio.Miniaudio;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void WeftRenderCallbackNative(float* output, uint frames, uint channels, void* userData);

internal static unsafe class WeftAudioNative
{
    private const string Lib = "weftone_miniaudio";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int weft_audio_open(uint sampleRate, uint channels, uint bufferFrames,
        WeftRenderCallbackNative callback, void* userData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int weft_audio_start();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int weft_audio_stop();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int weft_audio_close();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint weft_audio_last_error();

    internal static string GetLastError() => Marshal.PtrToStringAnsi(weft_audio_last_error()) ?? "";
}
