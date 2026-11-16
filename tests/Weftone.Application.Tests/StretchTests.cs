using System.Runtime.InteropServices;
using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Dsp;
using Weftone.Abstractions.Rendering;
using Weftone.Adapters.Dsp;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

/// <summary>SPEC M4 竖切 1：拉伸——档位取整、变体产出（fake）、编译器变体选择、
/// 真 Signalsmith 长度/确定性/静音（native 缺失时 Ignore）。</summary>
public class StretchTests
{
    private sealed class FakeStretcher : ITimeStretcher
    {
        public double LastRatio;

        public float[] Stretch(ReadOnlySpan<float> interleaved, int channels, int sampleRate, double durationRatio)
        {
            LastRatio = durationRatio;
            long frames = interleaved.Length / channels;
            long target = (long)(frames * durationRatio);
            var output = new float[target * channels];
            for (long i = 0; i < target * channels; i++)
            {
                output[i] = interleaved[(int)(i % interleaved.Length)] * 0.5f;
            }
            return output;
        }
    }

    private static ProjectDocument SamplerDocument(double stretchSpeed)
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Assets.Add(new AssetRef { Id = "a", Path = "assets/a.wav" });
        document.Channels.Add(new Channel
        {
            Id = 1, Kind = ChannelKind.Sampler, AssetId = "a", StretchSpeed = stretchSpeed,
        });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, Velocity = 1, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        return document;
    }

    private static List<PreparedAsset> PrepareFake(ProjectDocument document, FakeStretcher stretcher)
    {
        var audio = new DecodedAudio([0.1f, -0.2f, 0.3f, -0.4f, 0.5f, -0.6f], 1, 48_000, 6);        return new AssetPreparer().Prepare(document, _ => new MemoryStream([0]), new FixedDecoder(audio),
            new IdentityResampler(), stretcher);
    }

    private sealed class FixedDecoder(DecodedAudio audio) : ISampleDecoder
    {
        public DecodedAudio Decode(Stream stream) => audio;
        public void Dispose() { }
    }

    private sealed class IdentityResampler : IResampler
    {
        public DecodedAudio Resample(DecodedAudio input, int sampleRate) => input;
    }

    [TestCase(0.5, 0.5)]
    [TestCase(0.9, 1.0)]
    [TestCase(1.1, 1.0)]
    [TestCase(1.6, 1.5)]
    [TestCase(2.9, 2.0)]
    [TestCase(0.1, 0.5)]
    public void Nearest_step_is_clamped_and_deterministic(double speed, double expected)
        => Assert.That(AssetPreparer.NearestStretchStep(speed), Is.EqualTo(expected));

    [Test]
    public void Non_unity_speed_produces_variant_with_reciprocal_ratio()
    {
        var stretcher = new FakeStretcher();
        List<PreparedAsset> prepared = PrepareFake(SamplerDocument(1.5), stretcher);
        Assert.Multiple(() =>
        {
            Assert.That(prepared, Has.Count.EqualTo(2), "原版 + 变体");
            Assert.That(prepared[0].Speed, Is.EqualTo(1.0));
            Assert.That(prepared[1].Speed, Is.EqualTo(1.5));
            Assert.That(stretcher.LastRatio, Is.EqualTo(1.0 / 1.5).Within(1e-9), "变体时长 = 1/speed");
        });
    }

    [Test]
    public void Unity_speed_and_no_stretcher_produce_no_variants()
    {
        List<PreparedAsset> one = PrepareFake(SamplerDocument(1.0), new FakeStretcher());
        List<PreparedAsset> none = PrepareFake(SamplerDocument(1.9), null!);
        Assert.Multiple(() =>
        {
            Assert.That(one, Has.Count.EqualTo(1), "1.0 不产变体");
            Assert.That(none, Has.Count.EqualTo(1), "无 stretcher 注入不产变体（降级原路径）");
        });
    }

    [Test]
    public void Compiler_selects_variant_and_compensates_rate()
    {
        var stretcher = new FakeStretcher();
        ProjectDocument document = SamplerDocument(1.5);
        List<PreparedAsset> prepared = PrepareFake(document, stretcher);
        RenderPlan plan = new DocumentCompiler().Compile(document, prepared);
        RenderVoice voice = plan.Voices.Single(v => v.Kind == RenderVoiceKind.Sampler);
        Assert.Multiple(() =>
        {
            Assert.That(voice.AssetIndex, Is.EqualTo(1), "指向变体（索引 1）");
            Assert.That(voice.Frequency, Is.EqualTo(1.5f).Within(1e-6f), "rate=档位补偿，时长还原");
            Assert.That(plan.Assets[1].Frames, Is.EqualTo(4), "变体帧数 = 6/1.5（fake 比率实现）");
        });
    }

    [Test]
    public void Unity_speed_compilation_is_bitwise_unchanged()
    {
        ProjectDocument document = SamplerDocument(1.0);
        List<PreparedAsset> prepared = PrepareFake(document, new FakeStretcher());
        RenderPlan plain = new DocumentCompiler().Compile(document, PrepareFake(document, null!));
        RenderPlan withStretcher = new DocumentCompiler().Compile(document, prepared);
        RenderVoice a = plain.Voices.Single(v => v.Kind == RenderVoiceKind.Sampler);
        RenderVoice b = withStretcher.Voices.Single(v => v.Kind == RenderVoiceKind.Sampler);
        Assert.That((a.AssetIndex, a.Frequency), Is.EqualTo((b.AssetIndex, b.Frequency)), "1.0 路径位级不变");
    }

    // ---- 真 Signalsmith（native 依赖） ----

    private static string? FindStretchLib()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        string name = OperatingSystem.IsWindows() ? "weftone_dsp_stretch.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libweftone_dsp_stretch.dylib"
            : "libweftone_dsp_stretch.so";
        return dir is null ? null
            : Directory.EnumerateFiles(Path.Combine(dir.FullName, "native", "build"), name,
                SearchOption.AllDirectories).FirstOrDefault();
    }

    [Test]
    public void Signalsmith_length_determinism_and_silence()
    {
        if (FindStretchLib() is null)
        {
            Assert.Ignore("先构建 native（weftone_dsp_stretch）");
        }        var stretcher = new SignalsmithStretcher();
        const int frames = 4_800;
        var input = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            input[i] = MathF.Sin(2f * MathF.PI * 440f * i / 48_000f) * 0.5f;
        }

        float[] faster = stretcher.Stretch(input, 1, 48_000, 1.0 / 2.0);
        float[] slower = stretcher.Stretch(input, 1, 48_000, 2.0);
        float[] again = stretcher.Stretch(input, 1, 48_000, 2.0);
        var silent = new float[frames];

        Assert.Multiple(() =>
        {
            Assert.That(faster.Length, Is.EqualTo(frames / 2), "0.5×时长");
            Assert.That(slower.Length, Is.EqualTo(frames * 2), "2×时长");
            Assert.That(again, Is.EqualTo(slower), "同输入同参数位级确定");
        });

        float[] stretchedSilence = stretcher.Stretch(silent, 1, 48_000, 2.0);
        Assert.That(stretchedSilence.Max(Math.Abs), Is.LessThan(1e-3f), "静音输入保持静音");

        double peakIn = input.Select(Math.Abs).Max();
        double peakOut = slower.Select(Math.Abs).Max();
        Assert.That(peakOut, Is.GreaterThan(peakIn * 0.3).Within(1e-9), "能量保留（sine 拉长仍有声）");
    }
}
