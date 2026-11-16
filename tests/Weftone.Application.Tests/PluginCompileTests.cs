using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

/// <summary>SPEC M3 竖切 2：插件通道编译——槽表、声部字段、引用校验。</summary>
public class PluginCompileTests
{
    private static ProjectDocument NewPluginDocument()
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Plugins.Add(new PluginRef { Id = "test.sine", Path = @"C:\plug\test.clap" });
        document.Channels.Add(new Channel
        {
            Id = 1, Kind = ChannelKind.Plugin, PluginId = "test.sine", Volume = 0.5, Pan = 0.5,
        });
        document.Channels.Add(new Channel { Id = 2, Kind = ChannelKind.Sine });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 96, Key = 64, Velocity = 0.7, LengthTicks = 48 });
        pattern.Notes.Add(new Note { ChannelId = 2, Tick = 0, Key = 69, Velocity = 1.0, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        return document;
    }

    [Test]
    public void Plugin_channel_compiles_to_slot_and_event_voice()
    {
        RenderPlan plan = new DocumentCompiler().Compile(NewPluginDocument());
        Assert.Multiple(() =>
        {
            Assert.That(plan.PluginSlots, Has.Length.EqualTo(1), "每 plugin 通道一槽");
            Assert.That(plan.PluginSlots[0].PluginId, Is.EqualTo("test.sine"));
            Assert.That(plan.PluginSlots[0].FilePath, Is.EqualTo(@"C:\plug\test.clap"));
            Assert.That(plan.PluginSlots[0].ChannelId, Is.EqualTo(1));
            Assert.That(plan.PluginSlots[0].Params, Is.Empty);
            Assert.That(plan.Voices, Has.Length.EqualTo(2));
        });
        RenderVoice pluginVoice = plan.Voices.Single(v => v.Kind == RenderVoiceKind.Plugin);
        Assert.Multiple(() =>
        {
            Assert.That(pluginVoice.PluginSlot, Is.EqualTo(0));
            Assert.That(pluginVoice.NoteKey, Is.EqualTo(64));
            Assert.That(pluginVoice.Velocity, Is.EqualTo(0.7f).Within(1e-6f), "velocity 交插件");
            // 增益 = 通道 vol×pan 预乘（PanLaw(0.5)=(0.5,1.0)），不含 velocity
            Assert.That(pluginVoice.GainLeft, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(pluginVoice.GainRight, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(pluginVoice.GainCurveIndex, Is.EqualTo(-1), "插件声部本竖切静态增益");
        });
    }

    [Test]
    public void Plugin_channel_with_automation_lane_stays_static_this_slice()
    {
        ProjectDocument document = NewPluginDocument();
        AutomationLanes.Insert(document.Channels[0].VolumePoints, new AutomationPoint { Tick = 0, Value = 2.0 });
        RenderPlan plan = new DocumentCompiler().Compile(document);
        RenderVoice pluginVoice = plan.Voices.Single(v => v.Kind == RenderVoiceKind.Plugin);
        Assert.Multiple(() =>
        {
            Assert.That(pluginVoice.GainCurveIndex, Is.EqualTo(-1), "插件输出不走曲线（精度提升留竖切 4）");
            Assert.That(pluginVoice.GainLeft, Is.EqualTo(0.25f).Within(1e-6f), "仍按静态通道值预乘");
        });
    }

    [Test]
    public void Missing_plugin_reference_or_path_throws()
    {
        ProjectDocument document = NewPluginDocument();
        document.Channels[0].PluginId = "missing.id";
        Assert.That(() => new DocumentCompiler().Compile(document),
            Throws.InvalidOperationException.With.Message.Contains("未知插件"));

        ProjectDocument noPath = NewPluginDocument();
        noPath.Plugins[0].Path = "";
        Assert.That(() => new DocumentCompiler().Compile(noPath),
            Throws.InvalidOperationException.With.Message.Contains("路径"));
    }

    [Test]
    public void Plugin_param_lanes_compile_to_sorted_step_events()
    {
        ProjectDocument document = NewPluginDocument();
        Channel channel = document.Channels[0];
        List<AutomationPoint> lane = channel.GetPluginParamPoints(1);
        lane.Add(new AutomationPoint { Tick = 96, Value = 0.4 });
        lane.Add(new AutomationPoint { Tick = 288, Value = 0.8 });
        lane.Add(new AutomationPoint { Tick = 0, Value = 0.1 });
        lane.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.PluginParamEvents.Length, Is.EqualTo(3), "sample0 首值 + 两个非零点");
            Assert.That(plan.PluginParamEvents[0].Sample, Is.EqualTo(0));
            Assert.That(plan.PluginParamEvents[0].Value, Is.EqualTo(0.1), "首点前 clamp");
            Assert.That(plan.PluginParamEvents[1].Sample, Is.EqualTo(96 * 250));
            Assert.That(plan.PluginParamEvents[1].Value, Is.EqualTo(0.4));
            Assert.That(plan.PluginParamEvents[2].Sample, Is.EqualTo(288 * 250));
            Assert.That(plan.PluginParamEvents[2].Slot, Is.EqualTo(0));
        });
    }

    [Test]
    public void Two_plugin_channels_get_distinct_slots_in_document_order()
    {
        ProjectDocument document = NewPluginDocument();
        document.Plugins.Add(new PluginRef { Id = "p2", Path = "p2.clap" });
        document.Channels.Add(new Channel { Id = 3, Kind = ChannelKind.Plugin, PluginId = "p2" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 3, Tick = 0, Key = 60, Velocity = 1, LengthTicks = 96 });
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.That(plan.PluginSlots.Select(s => s.PluginId), Is.EqualTo(new[] { "test.sine", "p2" }));
        RenderVoice second = plan.Voices.Single(v => v.Kind == RenderVoiceKind.Plugin && v.NoteKey == 60);
        Assert.That(second.PluginSlot, Is.EqualTo(1));
    }
}
