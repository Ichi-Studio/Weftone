using System.Runtime.InteropServices;

namespace Weftone.Adapters.Codecs;

internal static unsafe class WeftCodecsNative
{
    private const string Lib = "weftone_codecs";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int weft_wav_decode(void* data, nuint size,
        out float* samples, out uint channels, out uint sampleRate, out ulong frames);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void weft_wav_free(float* samples);
}
