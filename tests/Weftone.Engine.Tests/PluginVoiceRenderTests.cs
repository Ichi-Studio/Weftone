using NUnit.Framework;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

/// <summary>SPEC M3 竖切 2：插件声部渲染——note on/off 时序（帧偏移）、通道增益混入、
/// master 后乘、跨 chunk 连续、State 配对换入；RT 零分配。用可控 fake 实例隔离 native。</summary>
public class PluginVoiceRenderTests
{
    private const int SampleRate = 48_000;

    /// <summary>门控常数插件：On@f 起 L=R=1、Off@f 起归 0（跨块保持门控状态）；
    /// 事件可关记录（零分配断言用）。</summary>
    private sealed class FakePlugin : IAudioPluginInstance
    {
        public List<(int Frame, PluginNoteEventKind Kind, int Key, float Velocity)> Events { get; } = [];
        public bool Record = true;
        private bool _gate;
        public double SampleRate => 48_000;

        public void Process(Span<float> left, Span<float> right, ReadOnlySpan<PluginNoteEvent> events)
        {
            if (Record)
            {
                foreach (PluginNoteEvent e in events)
                {
                    Events.Add((e.FrameOffset, e.Kind, e.Key, e.Velocity));
                }
            }
            int cursor = 0;
            foreach (PluginNoteEvent e in events)
            {
                FillGate(left, right, cursor, e.FrameOffset);
                cursor = e.FrameOffset;
                _gate = e.Kind == PluginNoteEventKind.On;
            }
            FillGate(left, right, cursor, left.Length);
        }

        private void FillGate(Span<float> left, Span<float> right, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                left[i] = _gate ? 1f : 0f;
                right[i] = _gate ? 1f : 0f;
            }
        }

        public void Dispose() { }
    }

    private static RenderVoice PluginVoice(long start, long length, int key = 69, float velocity = 0.8f,
        float gainLeft = 1f, float gainRight = 1f, int slot = 0) => new()
    {
        StartSample = start,
        LengthSamples = length,
        Kind = RenderVoiceKind.Plugin,
        PluginSlot = slot,
        NoteKey = key,
        Velocity = velocity,
        GainLeft = gainLeft,
        GainRight = gainRight,
    };

    private static RenderPlan Plan(long total, params RenderVoice[] voices) => new()
    {
        SampleRate = SampleRate,
        Channels = 2,
        TotalSamples = total,
        Voices = voices,
        PluginSlots = [new PlanPluginSlot("fake", "fake.clap")],
    };

    [Test]
    public void Note_events_arrive_chunk_relative_and_gains_mix()
    {
        var plugin = new FakePlugin();
        var renderer = new PlanRenderer();
        renderer.Load(Plan(600, PluginVoice(100, 400, gainLeft: 0.5f, gainRight: 0.25f)), [plugin]);
        var buffer = new float[2 * 600];
        renderer.Render(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(plugin.Events[0], Is.EqualTo((100, PluginNoteEventKind.On, 69, 0.8f)), "on 帧相对块起点");
            Assert.That(plugin.Events[1], Is.EqualTo((244, PluginNoteEventKind.Off, 69, 0f)), "off@500 = chunk1(256)+244");
            Assert.That(buffer[2 * 100], Is.EqualTo(0.5f).Within(1e-6f), "通道增益左");
            Assert.That(buffer[2 * 100 + 1], Is.EqualTo(0.25f).Within(1e-6f), "通道增益右");
            Assert.That(buffer[2 * 99], Is.EqualTo(0f), "on 前静音");
            Assert.That(buffer[2 * 500], Is.EqualTo(0f), "off 后静音");
            Assert.That(buffer[2 * 499], Is.EqualTo(0.5f).Within(1e-6f), "off 前最后一帧有声");
        });
    }

    /// <summary>渲染块大于内部 256 帧 chunk：事件帧偏移须相对每个 chunk 起点。</summary>
    [Test]
    public void Events_are_relative_to_each_256_frame_chunk()
    {
        var plugin = new FakePlugin();
        var renderer = new PlanRenderer();
        renderer.Load(Plan(600, PluginVoice(300, 200)), [plugin]);
        var buffer = new float[2 * 600];
        renderer.Render(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(plugin.Events[0].Frame, Is.EqualTo(44), "300 = chunk1(256) + 44");
            Assert.That(plugin.Events[1].Frame, Is.EqualTo(244), "off@500 = chunk1 + 244");
            Assert.That(buffer[2 * 300], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(buffer[2 * 299], Is.EqualTo(0f));
            Assert.That(buffer[2 * 499], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(buffer[2 * 500], Is.EqualTo(0f));
        });
    }

    [Test]
    public void Master_gain_applies_after_plugin_output()
    {
        var plugin = new FakePlugin();
        var renderer = new PlanRenderer();
        var plan = new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = 300,
            Voices = [PluginVoice(0, 300)],
            PluginSlots = [new PlanPluginSlot("fake", "f.clap")],
            MasterGain = 0.5f,
        };
        var buffer = new float[2 * 300];
        renderer.Load(plan, [plugin]);
        renderer.Render(buffer);
        Assert.That(buffer[10], Is.EqualTo(0.5f).Within(1e-6f), "master 后乘生效");
    }

    /// <summary>同一实例的两个 note：事件按帧序到达（on/on/off/off），输出为实例混合单流。</summary>
    [Test]
    public void Two_notes_same_slot_deliver_sorted_events()
    {
        var plugin = new FakePlugin();
        var renderer = new PlanRenderer();
        renderer.Load(Plan(600, PluginVoice(0, 100, key: 60), PluginVoice(50, 100, key: 64)), [plugin]);
        var buffer = new float[2 * 600];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(plugin.Events.Select(e => (e.Frame, e.Kind, e.Key)), Is.EqualTo(new[]
            {
                (0, PluginNoteEventKind.On, 60),
                (50, PluginNoteEventKind.On, 64),
                (100, PluginNoteEventKind.Off, 60),
                (150, PluginNoteEventKind.Off, 64),
            }), "帧升序到达");
            Assert.That(buffer[2 * 60], Is.EqualTo(1f).Within(1e-6f), "门控常数：第二音维持开");
            Assert.That(buffer[2 * 150], Is.EqualTo(0f), "双关后静音");
        });
    }

    /// <summary>播放中编辑（SwapPlan 配对新表）：新 plan 的插件声部继续出声、位置连续。</summary>
    [Test]
    public void Swap_plan_pairs_new_table_and_keeps_position()
    {
        var first = new FakePlugin();
        var renderer = new PlanRenderer();
        renderer.Load(Plan(1_000, PluginVoice(0, 1_000)), [first]);
        var buffer = new float[2 * 1_000];
        renderer.Render(buffer.AsSpan(0, 2 * 500));

        var second = new FakePlugin();
        renderer.SwapPlan(Plan(1_000, PluginVoice(500, 500, key: 72)), [second]);
        renderer.Render(buffer.AsSpan(500 * 2, 2 * 500));

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Position, Is.EqualTo(1_000));
            Assert.That(second.Events.Select(e => (e.Frame, e.Kind)),
                Is.EqualTo(new (int, PluginNoteEventKind)[] { (0, PluginNoteEventKind.On) }),
                "swap 后 playhead 前的 note 不补发，新声部正常触发");
            Assert.That(buffer[2 * 600], Is.EqualTo(1f).Within(1e-6f), "换表后出声");
            Assert.That(buffer[2 * 499], Is.EqualTo(1f).Within(1e-6f), "swap 前连续");
        });
    }

    [Test]
    public void Plugin_render_does_not_allocate()
    {
        var plugin = new FakePlugin { Record = false };
        var renderer = new PlanRenderer();
        renderer.Load(Plan(2_000, PluginVoice(0, 100)), [plugin]);
        var buffer = new float[2 * 256];
        renderer.Render(buffer); // 事件块（on/off）先行完成，后续无事件
        Weftone.Abstractions.AudioRenderCallback callback = renderer.Render;
        for (int i = 0; i < 200; i++)
        {
            RtAssert.Guard(callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0), "插件声部渲染路径零分配");
    }
}
