using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

public class DocumentCompilerTests
{
    private static ProjectDocument NewDocument(int tempo = 120)
    {
        var document = new ProjectDocument { Tempo = tempo, SampleRate = 48_000 };
        document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine });
        var pattern = new Pattern { Id = 10 };
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        return document;
    }

    private static Note NoteAt(long tick, int key = 69, long length = 96) => new()
    {
        ChannelId = 1, Tick = tick, Key = key, Velocity = 1.0, LengthTicks = length,
    };

    [Test]
    public void Tick_maps_to_sample_at_120bpm_exactly()
    {
        var document = NewDocument();
        document.Patterns[0].Notes.Add(NoteAt(96));
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.That(plan.Voices.Length, Is.EqualTo(1));
        Assert.That(plan.Voices[0].StartSample, Is.EqualTo(24_000));
        Assert.That(plan.Voices[0].LengthSamples, Is.EqualTo(24_000));
        Assert.That(plan.TotalSamples, Is.EqualTo(48_000));
    }

    [Test]
    public void Tick_mapping_truncates_deterministically_at_140bpm()
    {
        var document = NewDocument(tempo: 140);
        document.Patterns[0].Notes.Add(NoteAt(96));
        RenderPlan plan = new DocumentCompiler().Compile(document);
        long expected = 96L * 48_000 * 60 / (140L * 96);
        Assert.That(plan.Voices[0].StartSample, Is.EqualTo(expected));
    }

    [Test]
    public void Playlist_clip_offsets_absolute_position()
    {
        var document = NewDocument();
        document.Patterns[0].Notes.Add(NoteAt(0));
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 192 });
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.That(plan.Voices.Length, Is.EqualTo(2));
        Assert.That(plan.Voices[1].StartSample, Is.EqualTo(48_000));
    }

    [Test]
    public void Voice_frequency_follows_equal_temperament()
    {
        var document = NewDocument();
        document.Patterns[0].Notes.Add(NoteAt(0, key: 69));
        document.Patterns[0].Notes.Add(NoteAt(1, key: 81));
        document.Patterns[0].Notes.Add(NoteAt(2, key: 57));
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices[0].Frequency, Is.EqualTo(440f).Within(0.01f));
            Assert.That(plan.Voices[1].Frequency, Is.EqualTo(880f).Within(0.02f));
            Assert.That(plan.Voices[2].Frequency, Is.EqualTo(220f).Within(0.01f));
        });
    }

    [Test]
    public void Gain_and_pan_are_premultiplied_per_voice()
    {
        var document = NewDocument();
        document.Channels[0].Volume = 0.5;
        document.Channels[0].Pan = 1.0;
        document.Patterns[0].Notes.Add(new Note { ChannelId = 1, Tick = 0, Velocity = 0.8, LengthTicks = 96 });
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices[0].GainLeft, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(plan.Voices[0].GainRight, Is.EqualTo(0.4f).Within(1e-6f));
        });

        var centered = NewDocument();
        centered.Patterns[0].Notes.Add(NoteAt(0));
        RenderPlan centerPlan = new DocumentCompiler().Compile(centered);
        Assert.Multiple(() =>
        {
            Assert.That(centerPlan.Voices[0].GainLeft, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(centerPlan.Voices[0].GainRight, Is.EqualTo(1f).Within(1e-6f));
        });
    }

    [Test]
    public void Sampler_channel_compiles_with_rate_and_asset_index()
    {
        var document = NewDocument();
        document.Channels.Add(new Channel { Id = 2, Kind = ChannelKind.Sampler, AssetId = "a1" });
        document.Assets.Add(new AssetRef { Id = "a1", Path = "assets/a.wav" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 2, Tick = 0, Key = 72, LengthTicks = 96 });
        RenderPlan plan = new DocumentCompiler().Compile(document,
        [
            new PreparedAsset("a1", [0f, 0f, 0f], 1, 3),
        ]);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices[0].Kind, Is.EqualTo(RenderVoiceKind.Sampler));
            Assert.That(plan.Voices[0].AssetIndex, Is.EqualTo(0));
            Assert.That(plan.Voices[0].Frequency, Is.EqualTo(2f).Within(1e-4f), "key72 = 原速×2");
            Assert.That(plan.Assets.Length, Is.EqualTo(1));
            Assert.That(plan.Assets[0].Frames, Is.EqualTo(3));
        });
    }

    [Test]
    public void Sampler_channel_without_prepared_asset_rejects()
    {
        var document = NewDocument();
        document.Channels.Add(new Channel { Id = 2, Kind = ChannelKind.Sampler, AssetId = "a1" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 2, Tick = 0, LengthTicks = 96 });
        Assert.That(() => new DocumentCompiler().Compile(document),
            Throws.InvalidOperationException.With.Message.Contains("缺少已准备的资产"));
    }

    [Test]
    public void Empty_playlist_compiles_to_silent_plan()
    {
        var document = NewDocument();
        document.Playlist.Clear();
        document.Patterns[0].Notes.Add(NoteAt(0));
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices.Length, Is.EqualTo(0));
            Assert.That(plan.TotalSamples, Is.EqualTo(0));
        });
    }

    [Test]
    public void Compile_is_deterministic()
    {
        var document = NewDocument(tempo: 137);
        for (int i = 0; i < 50; i++)
        {
            document.Patterns[0].Notes.Add(NoteAt(i * 37 % 384, key: 60 + i % 12, length: 48 + i % 96));
        }
        var compiler = new DocumentCompiler();
        RenderPlan a = compiler.Compile(document);
        RenderPlan b = compiler.Compile(document);
        Assert.That(b.TotalSamples, Is.EqualTo(a.TotalSamples));
        Assert.That(b.Voices.Length, Is.EqualTo(a.Voices.Length));
        for (int i = 0; i < a.Voices.Length; i++)
        {
            Assert.That(b.Voices[i].StartSample, Is.EqualTo(a.Voices[i].StartSample), $"voice {i}");
            Assert.That(b.Voices[i].LengthSamples, Is.EqualTo(a.Voices[i].LengthSamples), $"voice {i}");
            Assert.That(b.Voices[i].Frequency, Is.EqualTo(a.Voices[i].Frequency), $"voice {i}");
            Assert.That(b.Voices[i].GainLeft, Is.EqualTo(a.Voices[i].GainLeft), $"voice {i}");
            Assert.That(b.Voices[i].GainRight, Is.EqualTo(a.Voices[i].GainRight), $"voice {i}");
            Assert.That(b.Voices[i].Kind, Is.EqualTo(a.Voices[i].Kind), $"voice {i}");
        }
    }
}
