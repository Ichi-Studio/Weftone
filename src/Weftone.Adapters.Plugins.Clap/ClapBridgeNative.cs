using System.Reflection;
using System.Runtime.InteropServices;
using Weftone.Abstractions.Plugins;

namespace Weftone.Adapters.Plugins.Clap;

/// <summary>weftone_clap_bridge 导入；结构布局与 native/weft_clap.c 逐字段对齐。
/// 公开给 weftone-plugin-scan 独立扫描进程使用（SPEC M3 D1=a）。</summary>
public static class ClapBridgeNative
{
    private const string LibName = "weftone_clap_bridge";
    private static int _installed;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct PluginInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Vendor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Features;
    }

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }
        NativeLibrary.SetDllImportResolver(typeof(ClapBridgeNative).Assembly, Resolve);
    }

    /// <summary>扫描单个 .clap；失败（加载/解析）返回 false。</summary>
    public static bool TryScan(string path, out PluginDescriptor[] descriptors)
    {
        EnsureInstalled();
        var buffer = new PluginInfo[16];
        int count = weft_clap_scan(path, buffer, buffer.Length);
        if (count < 0)
        {
            descriptors = [];
            return false;
        }
        descriptors = new PluginDescriptor[count];
        for (int i = 0; i < count; i++)
        {
            descriptors[i] = new PluginDescriptor(
                buffer[i].Id, buffer[i].Name, buffer[i].Vendor,
                buffer[i].Version, buffer[i].Description,
                buffer[i].Features.Length == 0
                    ? []
                    : buffer[i].Features.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }
        return true;
    }

    [DllImport(LibName)]
    private static extern int weft_clap_scan(string path, [Out] PluginInfo[] infos, int max);

    [DllImport(LibName)]
    private static extern nint weft_clap_plugin_create(string path, string pluginId, double sampleRate);

    [DllImport(LibName)]
    private static extern void weft_clap_plugin_destroy(nint instance);

    [DllImport(LibName)]
    private static unsafe extern void weft_clap_plugin_process(
        nint instance, float* left, float* right, int frames, NoteEventNative* events, int count);

    [DllImport(LibName)]
    private static extern uint weft_clap_plugin_param_count(nint instance);

    [DllImport(LibName)]
    private static extern bool weft_clap_plugin_param_info(nint instance, uint index,
        [Out] out ParamInfoNative info);

    [DllImport(LibName)]
    private static extern void weft_clap_plugin_set_param(nint instance, uint paramId, double value);

    [DllImport(LibName)]
    private static extern bool weft_clap_plugin_save_state(nint instance,
        out nint buffer, out int size);

    [DllImport(LibName)]
    private static extern bool weft_clap_plugin_load_state(nint instance, byte[] data, int size);

    [DllImport(LibName)]
    private static extern void weft_clap_free(nint pointer);

    [DllImport(LibName)]
    private static extern uint weft_clap_plugin_latency(nint instance);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ParamInfoNative
    {
        public uint Id;
        public int Automatable;
        public double MinValue;
        public double MaxValue;
        public double DefaultValue;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NoteEventNative
    {
        public int Frame;
        public int IsOn;
        public int Key;
        public int Channel;
        public float Velocity;
    }

    /// <summary>创建已激活并进入处理态的 CLAP 实例；失败返回 0（加载/id 不符/端口契约不符）。</summary>
    public static nint CreateInstance(string clapPath, string pluginId, double sampleRate)
    {
        EnsureInstalled();
        return weft_clap_plugin_create(clapPath, pluginId, sampleRate);
    }

    internal static unsafe void ProcessInstance(nint instance, Span<float> left, Span<float> right,
        ReadOnlySpan<NoteEventNative> events)
    {
        fixed (float* l = left)
        fixed (float* r = right)
        fixed (NoteEventNative* e = events)
        {
            weft_clap_plugin_process(instance, l, r, left.Length, e, events.Length);
        }
    }

    public static void DestroyInstance(nint instance) => weft_clap_plugin_destroy(instance);

    /// <summary>查询实例参数描述（竖切 3；创建时快照于 native 侧）。</summary>
    public static List<PluginParameterInfo> GetParameters(nint instance)
    {
        var result = new List<PluginParameterInfo>();
        uint count = weft_clap_plugin_param_count(instance);
        for (uint i = 0; i < count; i++)
        {
            if (weft_clap_plugin_param_info(instance, i, out ClapBridgeNative.ParamInfoNative info))
            {
                result.Add(new PluginParameterInfo(
                    info.Id, info.Name, info.MinValue, info.MaxValue, info.DefaultValue,
                    info.Automatable != 0));
            }
        }
        return result;
    }

    public static void SetParameter(nint instance, uint paramId, double value)
        => weft_clap_plugin_set_param(instance, paramId, value);

    public static byte[]? SaveState(nint instance)
    {
        if (!weft_clap_plugin_save_state(instance, out nint buffer, out int size) || buffer == 0)
        {
            return null;
        }
        try
        {
            var data = new byte[size];
            Marshal.Copy(buffer, data, 0, size);
            return data;
        }
        finally
        {
            weft_clap_free(buffer);
        }
    }

    public static bool LoadState(nint instance, byte[] data)
        => weft_clap_plugin_load_state(instance, data, data.Length);

    public static uint GetLatency(nint instance) => weft_clap_plugin_latency(instance);

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
