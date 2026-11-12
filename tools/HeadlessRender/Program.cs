using System.Globalization;
using Weftone.Abstractions;
using Weftone.Adapters.Audio.Miniaudio;
using Weftone.Engine;
using Weftone.Tools.HeadlessRender;

const int SampleRate = 48_000;
const int Channels = 2;
const int BlockFrames = 256;

string command = "render";
float freq = 440f;
double seconds = 5;
string? outPath = null;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "render" or "play":
                command = args[i];
                break;
            case "--freq" or "-f":
                freq = float.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                break;
            case "--seconds" or "-s":
                seconds = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                break;
            case "--out" or "-o":
                outPath = Next(args, ref i);
                break;
            case "--help" or "-h":
                PrintUsage();
                return 0;
            default:
                Console.Error.WriteLine($"未知参数: {args[i]}");
                PrintUsage();
                return 2;
        }
    }
    if (seconds <= 0 || seconds > 3600)
    {
        Console.Error.WriteLine("--seconds 须在 (0, 3600]");
        return 2;
    }
}
catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
{
    Console.Error.WriteLine($"参数错误: {ex.Message}");
    PrintUsage();
    return 2;
}

return command switch
{
    "play" => PlayLive(freq, seconds),
    _ => RenderWav(freq, seconds, outPath ?? "out.wav"),
};

static string Next(string[] args, ref int i)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"参数 {args[i]} 缺少值");
    }
    i++;
    return args[i];
}

static int RenderWav(float freq, double seconds, string path)
{
    long totalFrames = (long)(SampleRate * seconds);
    var engine = new SineEngine(freq, SampleRate);
    var buffer = new float[BlockFrames * Channels];
    string fullPath = Path.GetFullPath(path);
    string? dir = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }
    using var stream = File.Create(fullPath);
    WavWriter.WriteHeader(stream, SampleRate, Channels, totalFrames * Channels);
    for (long rendered = 0; rendered < totalFrames; rendered += BlockFrames)
    {
        int frames = (int)Math.Min(BlockFrames, totalFrames - rendered);
        var block = buffer.AsSpan(0, frames * Channels);
        RtAssert.Guard(engine.Callback, block);
        WavWriter.WriteSamples(stream, block);
    }
    Console.WriteLine($"渲染完成: {path}（{seconds.ToString("0.##", CultureInfo.InvariantCulture)}s @ {SampleRate}Hz/{Channels}ch float32，RT 分配违规 {RtAssert.Violations}）");
    return RtAssert.Violations == 0 ? 0 : 1;
}

static int PlayLive(float freq, double seconds)
{
    var spec = new AudioDeviceSpec(SampleRate, Channels, BlockFrames);
    var engine = new SineEngine(freq, spec.SampleRate);
    using var backend = new MiniaudioBackend();
    try
    {
        backend.Open(spec, b => RtAssert.Guard(engine.Callback, b));
        backend.Start();
    }
    catch (DllNotFoundException)
    {
        Console.Error.WriteLine("未找到 native 库 weftone_miniaudio：请先构建 native（见 CONTRIBUTING.md），或设置 WEFTONE_NATIVE_DIR。");
        return 3;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"音频设备打开/启动失败: {ex.Message}");
        return 4;
    }

    try
    {
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
    }
    finally
    {
        backend.Stop();
    }
    Console.WriteLine($"播放完成: {seconds.ToString("0.##", CultureInfo.InvariantCulture)}s {freq.ToString("0.##", CultureInfo.InvariantCulture)}Hz，RT 分配违规 {RtAssert.Violations}");
    return RtAssert.Violations == 0 ? 0 : 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        weftone — Weftone M0 CLI

        用法:
          weftone render [--freq Hz] [--seconds s] [--out PATH]   离线渲染 WAV（默认）
          weftone play   [--freq Hz] [--seconds s]                实时播放（需 native shim）
          weftone --help

        默认: --freq 440 --seconds 5 --out out.wav
        """);
}
