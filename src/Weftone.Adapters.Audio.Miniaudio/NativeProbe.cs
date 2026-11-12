using System.Reflection;
using System.Runtime.InteropServices;

namespace Weftone.Adapters.Audio.Miniaudio;

/// <summary>
/// 解析 weftone_miniaudio 库路径：环境变量 WEFTONE_NATIVE_DIR → 应用目录 →
/// 仓库布局 native/weftone_miniaudio/build（向上回溯查找）。
/// </summary>
internal static class NativeProbe
{
    private const string LibName = "weftone_miniaudio";
    private static int _installed;

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }
        NativeLibrary.SetDllImportResolver(typeof(NativeProbe).Assembly, Resolve);
    }

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
            string buildDir = Path.Combine(dir.FullName, "native", "weftone_miniaudio", "build");
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
