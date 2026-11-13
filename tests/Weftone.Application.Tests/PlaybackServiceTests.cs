using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Application;

namespace Weftone.Application.Tests;

public class PlaybackServiceTests
{
    private sealed class FakeBackend : IAudioBackend
    {
        public int OpenCount;
        public int StartCount;
        public int StopCount;
        public int DisposeCount;
        public AudioDeviceSpec? LastSpec;

        public void Open(AudioDeviceSpec spec, AudioRenderCallback callback)
        {
            OpenCount++;
            LastSpec = spec;
        }

        public void Start() => StartCount++;
        public void Stop() => StopCount++;
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeRenderer : IPlanRenderer
    {
        public int LoadCount;
        public long Position { get; set; }
        public void Load(RenderPlan plan)
        {
            LoadCount++;
            Position = 0;
        }
        public void Render(Span<float> interleaved) { }
    }

    private static RenderPlan Plan(int sampleRate = 48_000) => new()
    {
        SampleRate = sampleRate,
        Channels = 2,
        TotalSamples = 4_800,
        Voices = [],
    };

    [Test]
    public void Play_loads_plan_then_opens_and_starts()
    {
        var backend = new FakeBackend();
        var renderer = new FakeRenderer();
        using var playback = new PlaybackService(() => backend, renderer);
        playback.Play(Plan());
        Assert.Multiple(() =>
        {
            Assert.That(renderer.LoadCount, Is.EqualTo(1));
            Assert.That(backend.OpenCount, Is.EqualTo(1));
            Assert.That(backend.StartCount, Is.EqualTo(1));
            Assert.That(playback.IsPlaying, Is.True);
            Assert.That(backend.LastSpec!.SampleRate, Is.EqualTo(48_000));
        });
    }

    [Test]
    public void Stop_stops_backend_and_clears_playing()
    {
        var backend = new FakeBackend();
        using var playback = new PlaybackService(() => backend, new FakeRenderer());
        playback.Play(Plan());
        playback.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(backend.StopCount, Is.EqualTo(1));
            Assert.That(playback.IsPlaying, Is.False);
        });
    }

    [Test]
    public void UpdatePlan_while_playing_does_stop_swap_start()
    {
        var backend = new FakeBackend();
        var renderer = new FakeRenderer();
        using var playback = new PlaybackService(() => backend, renderer);
        playback.Play(Plan());
        playback.UpdatePlan(Plan());
        Assert.Multiple(() =>
        {
            Assert.That(backend.StopCount, Is.EqualTo(1), "交换前先停止");
            Assert.That(renderer.LoadCount, Is.EqualTo(2));
            Assert.That(backend.StartCount, Is.EqualTo(2), "交换后恢复");
            Assert.That(playback.IsPlaying, Is.True);
        });
    }

    [Test]
    public void UpdatePlan_while_stopped_loads_without_starting()
    {
        var backend = new FakeBackend();
        var renderer = new FakeRenderer();
        using var playback = new PlaybackService(() => backend, renderer);
        playback.UpdatePlan(Plan());
        Assert.Multiple(() =>
        {
            Assert.That(renderer.LoadCount, Is.EqualTo(1));
            Assert.That(backend.StartCount, Is.EqualTo(0));
            Assert.That(playback.IsPlaying, Is.False);
        });
    }

    [Test]
    public void Play_with_new_sample_rate_reopens_backend()
    {
        var backends = new List<FakeBackend>();
        using var playback = new PlaybackService(
            () =>
            {
                var backend = new FakeBackend();
                backends.Add(backend);
                return backend;
            },
            new FakeRenderer());
        playback.Play(Plan(48_000));
        playback.Play(Plan(44_100));
        Assert.Multiple(() =>
        {
            Assert.That(backends.Count, Is.EqualTo(2), "采样率变化重建后端");
            Assert.That(backends[0].DisposeCount, Is.EqualTo(1), "旧后端释放");
            Assert.That(backends[1].LastSpec!.SampleRate, Is.EqualTo(44_100));
        });
    }

    [Test]
    public void Dispose_stops_and_disposes_backend()
    {
        var backend = new FakeBackend();
        var playback = new PlaybackService(() => backend, new FakeRenderer());
        playback.Play(Plan());
        playback.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(backend.StopCount, Is.EqualTo(1));
            Assert.That(backend.DisposeCount, Is.EqualTo(1));
        });
    }
}