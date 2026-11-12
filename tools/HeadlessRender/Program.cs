using System.Globalization;
using Weftone.Abstractions;
using Weftone.Adapters.Audio.Miniaudio;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

const int SampleRate = 48_000;
const int Channels = 2;
const int BlockFrames = 256;

string command = "render";
string? subCommand = null;
string? projectPath = null;
float freq = 440f;
double seconds = 5;
string? outPath = null;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "render" or "play" when command != "project":
                command = args[i];
                break;
            case "project":
                command = "project";
                subCommand = Next(args, ref i);
                if (subCommand is not ("render" or "info"))
                {
                    Console.Error.WriteLine($"未知子命令: {subCommand}");
                    PrintUsage();
                    return 2;
                }
                break;
            case "demo":
                command = "demo";
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
                if (command == "project" && projectPath is null && !args[i].StartsWith('-'))
                {
                    projectPath = args[i];
                }
                else
                {
                    Console.Error.WriteLine($"未知参数: {args[i]}");
                    PrintUsage();
                    return 2;
                }
                break;
        }
    }
    if (seconds <= 0 || seconds > 3600)
    {
        Console.Error.WriteLine("--seconds 须在 (0, 3600]");
        return 2;
    }
    if (command == "project" && string.IsNullOrEmpty(projectPath))
    {
        Console.Error.WriteLine("project 命令需要工程路径");
        PrintUsage();
        return 2;
    }
}
catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
{
    Console.Error.WriteLine($"参数错误: {ex.Message}");
    PrintUsage();
    return 2;
}

try
{
    return command switch
    {
        "project" => subCommand == "info" ? ProjectInfo(projectPath!) : ProjectRender(projectPath!, outPath ?? "out.wav"),
        "demo" => WriteDemo(outPath ?? "demo.weft"),
        "play" => PlayLive(freq, seconds),
        _ => RenderWav(freq, seconds, outPath ?? "out.wav"),
    };
}
catch (InvalidProjectException ex)
{
    Console.Error.WriteLine($"工程包非法: {ex.Message}");
    return 4;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"数据非法: {ex.Message}");
    return 4;
}

static string Next(string[] args, ref int i)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"参数 {args[i]} 缺少值");
    }
    i++;
    return args[i];
}

static ProjectRenderService NewRenderService() => new(
    new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());

static int ProjectRender(string path, string output)
{
    RenderedProject result = NewRenderService().Render(path);
    string fullPath = Path.GetFullPath(output);
    string? dir = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }
    using FileStream stream = File.Create(fullPath);
    WavWriter.WriteHeader(stream, result.SampleRate, result.Channels, result.Frames * result.Channels);
    WavWriter.WriteSamples(stream, result.Interleaved);
    Console.WriteLine($"渲染完成: {fullPath}（{result.Duration:hh\\:mm\\:ss\\.fff} @ {result.SampleRate}Hz/{result.Channels}ch，峰值 {result.Peak:0.####}）");
    return 0;
}

static int ProjectInfo(string path)
{
    LoadedProject loaded = new WeftProjectStore().Load(path);
    ProjectDocument document = loaded.Document;
    var patternEnds = new Dictionary<int, long>();
    foreach (Pattern pattern in document.Patterns)
    {
        long end = 0;
        foreach (Note note in pattern.Notes)
        {
            end = Math.Max(end, note.Tick + note.LengthTicks);
        }
        patternEnds[pattern.Id] = end;
    }
    long totalTicks = 0;
    foreach (Clip clip in document.Playlist)
    {
        if (patternEnds.TryGetValue(clip.PatternId, out long end))
        {
            totalTicks = Math.Max(totalTicks, clip.StartTick + end);
        }
    }
    long totalSamples = totalTicks * document.SampleRate * 60L / ((long)document.Tempo * TimeBase.Ppq);
    Console.WriteLine($"工程: {Path.GetFullPath(path)}");
    Console.WriteLine($"  BPM={document.Tempo} 采样率={document.SampleRate} 声部={document.Channels.Count} pattern={document.Patterns.Count} clip={document.Playlist.Count} 资产={document.Assets.Count}");
    Console.WriteLine($"  时长 ≈ {TimeSpan.FromSeconds((double)totalSamples / document.SampleRate):hh\\:mm\\:ss\\.fff}（{totalTicks} tick）");
    foreach (Channel channel in document.Channels)
    {
        Console.WriteLine($"  - ch{channel.Id} {channel.Name} [{channel.Kind}] vol={channel.Volume:0.###} pan={channel.Pan:0.###}");
    }
    return 0;
}

static int WriteDemo(string output)
{
    string fullPath = Path.GetFullPath(output);
    string? dir = Path.GetDirectoryName(fullPath);
    string workDir = string.IsNullOrEmpty(dir) ? "." : dir;
    Directory.CreateDirectory(workDir);
    string kickPath = Path.Combine(Path.GetTempPath(), "weftone-demo-kick-" + Guid.NewGuid().ToString("N") + ".wav");
    try
    {
        DemoProject.WriteKickWav(kickPath);
        var store = new WeftProjectStore();
        ProjectDocument document = DemoProject.CreateDocument();
        store.Save(fullPath, document, new Dictionary<string, string> { ["assets/kick.wav"] = kickPath });
    }
    finally
    {
        File.Delete(kickPath);
    }
    Console.WriteLine($"演示工程已生成: {fullPath}（weftone project render {fullPath} 可渲染为 WAV）");
    return 0;
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
    Console.WriteLine($"渲染完成: {fullPath}（{seconds.ToString("0.##", CultureInfo.InvariantCulture)}s @ {SampleRate}Hz/{Channels}ch float32，RT 分配违规 {RtAssert.Violations}）");
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
        weftone — Weftone M1a CLI

        用法:
          weftone render [--freq Hz] [--seconds s] [--out PATH]     离线渲染正弦 WAV（默认）
          weftone play   [--freq Hz] [--seconds s]                   实时播放（需 native shim）
          weftone project render <path.weft> [--out PATH]            渲染工程为 WAV
          weftone project info <path.weft>                           查看工程概览
          weftone demo [--out demo.weft]                             生成演示工程
          weftone --help

        默认: --freq 440 --seconds 5 --out out.wav
        """);
}
