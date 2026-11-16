using System.Reflection;
using System.Runtime.InteropServices;
using Weftone.Abstractions.Dsp;

namespace Weftone.Adapters.Dsp;

/// <summary>
/// Signalsmith Stretch 适配（SPEC M4 D2=a）：weftone_dsp_stretch P/Invoke，
/// 离线整段拉伸；输出帧数 = floor(输入帧 × ratio)。
/// </summary>
public sealed class SignalsmithStretcher : ITimeStretcher
{
    private const string LibName = "weftone_dsp_stretch";
    private static int _installed;

    public unsafe float[] Stretch(ReadOnlySpan<float> interleaved, int channels, int sampleRate, double durationRatio)
    {
        if (interleaved.Length == 0 || channels is < 1 or > 2 || sampleRate <= 0 || durationRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interleaved));
        }
        EnsureInstalled();
        long frames = interleaved.Length / channels;
        long target = Math.Min((long)(frames * durationRatio), frames * 8 + 4096);
        var output = new float[target * channels];
        int produced;
        fixed (float* input = interleaved)
        fixed (float* outPtr = output)
        {
            produced = weft_stretch_render(input, (int)frames, channels,
                sampleRate, durationRatio, outPtr, (int)target);
        }
        if (produced < 0)
        {
            throw new InvalidOperationException("stretch 失败（参数非法）");
        }
        if (produced == target)
        {
            return output;
        }
        Array.Resize(ref output, produced * channels);
        return output;
    }

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }
        NativeLibrary.SetDllImportResolver(typeof(SignalsmithStretcher).Assembly, Resolve);
    }

    [DllImport(LibName)]
    private static extern unsafe int weft_stretch_render(
        float* input, int inFrames, int channels,
        double sampleRate, double durationRatio,
        float* output, int outCap);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
        {
            return 0;
        }
        foreach (string candidate in Candidates())
        {
            if (NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return handle;
            }
        }
        return 0;
    }

    private static IEnumerable<string> Candidates()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        string fileName = (windows ? "" : "lib") + LibName + (windows ? ".dll" : mac ? ".dylib" : ".so");

        string? envDir = Environment.GetEnvironmentVariable("WEFTONE_NATIVE_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            yield return Path.Combine(envDir, fileName);
        }
        yield return Path.Combine(AppContext.BaseDirectory, fileName);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string buildDir = Path.Combine(dir.FullName, "native/build");
            if (!Directory.Exists(buildDir))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(buildDir, fileName, SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }
}
