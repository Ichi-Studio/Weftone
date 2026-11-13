using System.Diagnostics;
using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.App.Controls;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;
using Weftone.Engine;

namespace Weftone.App.Tests;

/// <summary>
/// M1b 软门禁本地基准（AGENTS.md §9 / specs/M1b_UI.md 验收 4）：
/// 1 万音符下 Piano Roll 视口裁剪交互、全量编译、引擎块渲染与拖拽命令成本。
/// Explicit：仅本地按名运行，不入 CI 门禁；断言为防退化宽松上界。
/// </summary>
[Explicit("M1b 本地基准（软门禁），CI 不跑")]
public class M1bTenThousandNotesBenchmark
{
    private const int NoteCount = 10_000;
    private const int Bars = 64;
    private const long TotalTicks = (long)Bars * 4 * TimeBase.Ppq;

    private static Pattern TenThousandNotePattern()
    {
        var pattern = new Pattern { Id = 10, Name = "Bench" };
        for (int i = 0; i < NoteCount; i++)
        {
            pattern.Notes.Add(new Note
            {
                ChannelId = 1,
                Tick = (long)((double)i * TotalTicks / NoteCount),
                Key = i % 128,
                Velocity = 0.2 + 0.8 * ((i % 97) / 97.0),
                LengthTicks = TimeBase.Ppq,
            });
        }
        return pattern;
    }

    private static ProjectDocument DocumentWith(Pattern pattern)
    {
        var document = new ProjectDocument { Tempo = 128, SampleRate = 48_000 };
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = pattern.Id, StartTick = 0 });
        return document;
    }

    private static (double AvgMs, double MaxMs) Measure(int iterations, Action once)
    {
        once(); // 预热（JIT）
        var watch = Stopwatch.StartNew();
        double max = 0;
        for (int i = 0; i < iterations; i++)
        {
            long start = watch.ElapsedTicks;
            once();
            double ms = (watch.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
            if (ms > max)
            {
                max = ms;
            }
        }
        watch.Stop();
        return (watch.Elapsed.TotalMilliseconds / iterations, max);
    }

    [Test]
    public void Run_all_benchmarks()
    {
        Pattern pattern = TenThousandNotePattern();
        ProjectDocument document = DocumentWith(pattern);
        IReadOnlyList<Note> notes = pattern.Notes;
        double midY = PianoRollGeometry.KeyToY(64) + 7;
        double beyondX = PianoRollGeometry.TickToX(TotalTicks) + 100;

        // 1) 命中最坏情况（未命中=逆序全量扫 1 万）
        var (hitAvgMs, hitMaxMs) = Measure(1_000,
            () => PianoRollGeometry.HitTest(notes, beyondX, midY));
        // 2) Render 的视口裁剪扫描（640px 视口内每帧的 foreach + 边界判断）
        (long firstTick, long lastTick) = PianoRollGeometry.VisibleTickRange(0, 640);
        var (scanAvgMs, scanMaxMs) = Measure(1_000, () =>
        {
            int drawn = 0;
            foreach (Note note in notes)
            {
                if (note.Tick + note.LengthTicks < firstTick || note.Tick > lastTick)
                {
                    continue;
                }
                drawn++;
            }
            Assert.That(drawn, Is.GreaterThan(0), "视口内应有可见音符");
        });
        // 3) 全量编译（Pattern 循环计划，1 万声部排序）
        var compiler = new DocumentCompiler();
        var (compileAvgMs, compileMaxMs) = Measure(100,
            () => compiler.CompilePattern(document, 10, []));
        // 4) 引擎块渲染（10k 事件计划，256 帧块，30 秒时间轴）
        RenderPlan plan = compiler.Compile(document, []);
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] block = new float[2 * 256];
        long blocks = plan.TotalSamples / 256;
        double blockMaxMs = 0;
        double blockTotalMs = 0;
        var watch = Stopwatch.StartNew();
        for (long b = 0; b < blocks; b++)
        {
            long start = watch.ElapsedTicks;
            renderer.Render(block);
            double ms = (watch.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
            blockTotalMs += ms;
            if (ms > blockMaxMs)
            {
                blockMaxMs = ms;
            }
        }
        double blockAvgMs = blockTotalMs / blocks;
        // 5) 拖拽命令（FindNote 含 O(n) Contains；一次拖 100 步）
        Note dragged = pattern.Notes[5_000];
        var history = new CommandHistory();
        var (dragAvgMs, dragMaxMs) = Measure(20, () =>
        {
            history.BeginTransaction("移动");
            for (int step = 0; step < 100; step++)
            {
                history.Execute(new MoveNoteCommand(10, dragged, dragged.Tick, dragged.Key,
                    dragged.Tick + 24, dragged.Key), document);
            }
            history.EndTransaction();
        });

        TestContext.Progress.WriteLine($"""
            [M1b 基准] 1 万音符 / {Bars} 小节 / 48kHz：
              命中最坏（全量逆序扫） : avg {hitAvgMs:F4} ms, max {hitMaxMs:F4} ms
              视口裁剪扫描（640px）  : avg {scanAvgMs:F4} ms, max {scanMaxMs:F4} ms
              CompilePattern 全量    : avg {compileAvgMs:F2} ms, max {compileMaxMs:F2} ms
              引擎 256 帧块渲染      : avg {blockAvgMs:F4} ms, max {blockMaxMs:F4} ms（RT 预算 1.5 ms）
              拖拽 100 步事务        : avg {dragAvgMs:F2} ms/手势, max {dragMaxMs:F2} ms
            """);

        // 软门禁宽松上界（防数量级退化，远高于实际预期值）
        Assert.Multiple(() =>
        {
            Assert.That(hitAvgMs, Is.LessThan(1.0), "命中应远低于一帧");
            Assert.That(scanAvgMs, Is.LessThan(1.0), "裁剪扫描应远低于一帧");
            Assert.That(compileAvgMs, Is.LessThan(100.0), "全量编译软上界");
            Assert.That(blockAvgMs, Is.LessThan(1.5), "引擎块均值低于 RT 预算");
            Assert.That(dragAvgMs, Is.LessThan(50.0), "拖拽手势软上界");
        });
    }
}
