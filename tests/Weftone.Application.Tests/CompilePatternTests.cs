using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

public class CompilePatternTests
{
    // 120 BPM @48k：num=48000*60，den=120*96 ⇒ 恰好 250 samples/tick
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        return document;
    }

    private static void AddNote(ProjectDocument document, long tick, long length, int key = 60)
    {
        document.Patterns[0].Notes.Add(
            new Note { ChannelId = 1, Tick = tick, Key = key, LengthTicks = length });
    }

    [Test]
    public void CompilePattern_sets_loop_range_rounded_up_to_bar()
    {
        ProjectDocument document = NewDocument();
        AddNote(document, 0, 250); // 结束 250 tick（<1 小节 384）
        RenderPlan plan = new DocumentCompiler().CompilePattern(document, 10, []);
        Assert.Multiple(() =>
        {
            Assert.That(plan.LoopStartSample, Is.EqualTo(0));
            Assert.That(plan.LoopEndSample, Is.EqualTo(384 * 250), "1 小节 = 384 tick");
            Assert.That(plan.TotalSamples, Is.EqualTo(plan.LoopEndSample));
            Assert.That(plan.Voices.Length, Is.EqualTo(1));
            Assert.That(plan.Voices[0].StartSample, Is.EqualTo(0));
        });
    }

    [Test]
    public void CompilePattern_rounds_to_next_bar_when_content_exceeds_one_bar()
    {
        ProjectDocument document = NewDocument();
        AddNote(document, 0, 96);
        AddNote(document, 384, 96); // 结束 480 tick → 2 小节 768
        RenderPlan plan = new DocumentCompiler().CompilePattern(document, 10, []);
        Assert.That(plan.LoopEndSample, Is.EqualTo(768 * 250));
        Assert.That(plan.Voices[1].StartSample, Is.EqualTo(384 * 250), "第二音符偏移保留");
    }

    [Test]
    public void CompilePattern_empty_pattern_loops_one_bar_minimum()
    {
        ProjectDocument document = NewDocument();
        RenderPlan plan = new DocumentCompiler().CompilePattern(document, 10, []);
        Assert.That(plan.LoopEndSample, Is.EqualTo(384 * 250), "空 Pattern 至少循环 1 小节");
    }

    [Test]
    public void CompilePattern_unknown_pattern_rejected()
    {
        ProjectDocument document = NewDocument();
        Assert.That(() => new DocumentCompiler().CompilePattern(document, 99, []),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Song_compile_has_no_loop()
    {
        ProjectDocument document = NewDocument();
        AddNote(document, 0, 96);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        RenderPlan plan = new DocumentCompiler().Compile(document, []);
        Assert.Multiple(() =>
        {
            Assert.That(plan.LoopEndSample, Is.EqualTo(0), "Song 时间轴线性，无循环");
            Assert.That(plan.TotalSamples, Is.EqualTo(96 * 250));
        });
    }
}
