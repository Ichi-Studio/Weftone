using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;
using Weftone.Engine;

namespace Weftone.Application.Tests;

public class EditSessionImportExportTests
{
    private static string TempWavPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "weftone-import-" + Guid.NewGuid().ToString("N") + ".wav");
        DemoProject.WriteKickWav(path);
        return path;
    }

    private static string TempProjectPath() =>
        Path.Combine(Path.GetTempPath(), "weftone-export-" + Guid.NewGuid().ToString("N") + ".weft");

    private static string TempOutWavPath() =>
        Path.Combine(Path.GetTempPath(), "weftone-out-" + Guid.NewGuid().ToString("N") + ".wav");

    private static EditSession NewSession() =>
        new(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());

    [Test]
    public void ImportSample_adds_sampler_channel_and_prepared_asset()
    {
        string wav = TempWavPath();
        try
        {
            using var session = NewSession();
            session.NewDemo();
            int channelsBefore = session.Document.Channels.Count;

            Channel channel = session.ImportSample(wav);

            Assert.Multiple(() =>
            {
                Assert.That(session.Document.Channels.Count, Is.EqualTo(channelsBefore + 1));
                Assert.That(channel.Kind, Is.EqualTo(ChannelKind.Sampler));
                Assert.That(session.Document.Assets.Any(a => a.Id == channel.AssetId), Is.True);
                Assert.That(session.IsDirty, Is.True);
            });
            session.Document.Patterns[0].Notes.Add(new Note
            {
                ChannelId = channel.Id, Tick = 0, Key = 60, Velocity = 1.0, LengthTicks = 96,
            });
            RenderPlan plan = session.CompilePattern(session.Document.Patterns[0].Id);
            Assert.That(plan.Voices.Any(v => v.Kind == Abstractions.Rendering.RenderVoiceKind.Sampler),
                Is.True, "导入后编译出 sampler 声部");
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Test]
    public void ImportSample_rejects_non_wav_and_keeps_document_clean()
    {
        string bad = Path.Combine(Path.GetTempPath(), "weftone-bad-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(bad, [1, 2, 3, 4]);
        try
        {
            using var session = NewSession();
            session.NewDemo();
            int channels = session.Document.Channels.Count;
            int assets = session.Document.Assets.Count;
            Assert.That(() => session.ImportSample(bad), Throws.InstanceOf<InvalidDataException>());
            Assert.Multiple(() =>
            {
                Assert.That(session.Document.Channels.Count, Is.EqualTo(channels), "失败导入不留痕");
                Assert.That(session.Document.Assets.Count, Is.EqualTo(assets));
            });
        }
        finally
        {
            File.Delete(bad);
        }
    }

    [Test]
    public void ImportSample_save_then_reopen_restores_channel_and_asset()
    {
        string wav = TempWavPath();
        string project = TempProjectPath();
        try
        {
            using var session = NewSession();
            session.NewDemo();
            Channel imported = session.ImportSample(wav);
            session.Save(project);

            using var reopened = NewSession();
            reopened.Open(project);
            Assert.Multiple(() =>
            {
                Assert.That(reopened.Document.Channels.Any(c => c.Id == imported.Id && c.Kind == ChannelKind.Sampler),
                    Is.True, "重开工程恢复 sampler 通道");
                Assert.That(reopened.Document.Assets.Any(a => a.Id == imported.AssetId), Is.True, "恢复资产引用");
                Assert.That(reopened.Compile().Assets.Length, Is.EqualTo(session.Compile().Assets.Length),
                    "资产均已就绪可编译");
            });
        }
        finally
        {
            File.Delete(wav);
            File.Delete(project);
        }
    }

    [Test]
    public void ExportWav_writes_full_timeline_float32_stereo()
    {
        string outWav = TempOutWavPath();
        try
        {
            using var session = NewSession();
            var document = session.Document;
            document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
            document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
            document.Patterns[0].Notes.Add(
                new Note { ChannelId = 1, Tick = 0, Key = 69, Velocity = 1.0, LengthTicks = 96 });
            document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });

            long frames = session.ExportWav(outWav);

            long expectedBytes = 44 + frames * 2 * sizeof(float);
            Assert.Multiple(() =>
            {
                Assert.That(frames, Is.GreaterThan(0));
                Assert.That(new FileInfo(outWav).Length, Is.EqualTo(expectedBytes), "44 头 + float32 立体声");
            });
        }
        finally
        {
            File.Delete(outWav);
        }
    }

    [Test]
    public void ExportWav_respects_master_volume()
    {
        string quiet = TempOutWavPath();
        string loud = TempOutWavPath();
        try
        {
            using var session = NewSession();
            var document = session.Document;
            document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
            document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
            document.Patterns[0].Notes.Add(
                new Note { ChannelId = 1, Tick = 0, Key = 69, Velocity = 1.0, LengthTicks = 96 });
            document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });

            session.ExportWav(quiet);
            session.Execute(new SetMasterVolumeCommand(1.0, 0.1));
            session.ExportWav(loud);

            Assert.That(PeakOf(loud), Is.LessThan(PeakOf(quiet) * 0.2), "master 0.1 显著衰减");
        }
        finally
        {
            File.Delete(quiet);
            File.Delete(loud);
        }
    }

    [Test]
    public void ExportWav_matches_headless_cli_render_bit_for_bit()
    {
        string project = TempProjectPath();
        string outWav = TempOutWavPath();
        try
        {
            using var session = NewSession();
            session.NewDemo();
            session.Save(project);
            session.ExportWav(outWav);

            // CLI 同路径：.weft → ProjectRenderService → 离线渲染
            var cli = new ProjectRenderService(
                new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
            RenderedProject rendered = cli.Render(project);

            using var decoder = new DrWavDecoder();
            using Stream stream = File.OpenRead(outWav);
            DecodedAudio exported = decoder.Decode(stream);

            Assert.Multiple(() =>
            {
                Assert.That(new FileInfo(outWav).Length,
                    Is.EqualTo(44 + rendered.Frames * 2 * sizeof(float)), "44 头 + float32 立体声");
                Assert.That(exported.Frames, Is.EqualTo(rendered.Frames), "验收 1：导出与 CLI 帧数一致");
                Assert.That(exported.SampleRate, Is.EqualTo(rendered.SampleRate));
            });
            for (long i = 0; i < rendered.Frames * 2; i++)
            {
                if (exported.Interleaved[i] != rendered.Interleaved[i])
                {
                    Assert.Fail($"验收 1：导出与 CLI 渲染在第 {i} 个样本不一致（spec §验收 1 要求一致）");
                }
            }
        }
        finally
        {
            File.Delete(project);
            File.Delete(outWav);
        }
    }

    private static double PeakOf(string path)
    {
        using var decoder = new DrWavDecoder();
        using Stream stream = File.OpenRead(path);
        DecodedAudio audio = decoder.Decode(stream);
        double peak = 0;
        foreach (float sample in audio.Interleaved)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }
        return peak;
    }
}
