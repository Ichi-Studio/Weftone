using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

/// <summary>SPEC M2 竖切 4：RCU plan 交换——播放中编辑不停止、位置连续、无静音缺口。
/// 确定性交错 fuzz（固定种子）+ 多线程 chaos + 交换路径零分配。</summary>
public class RcuSwapTests
{
    private const int SampleRate = 48_000;
    private const int BlockFrames = 256;

    /// <summary>共享常数资产（不可变，跨 plan 复用；长度须超过一切渲染窗口）。</summary>
    private static readonly PlanAsset SharedOnes = CreateOnes(200_000);

    private static PlanAsset CreateOnes(int frames)
    {
        var samples = new float[frames];
        Array.Fill(samples, 1f);
        return new PlanAsset { Samples = samples, Channels = 1, Frames = frames };
    }

    private static RenderVoice ConstantVoice(long start, long length, float gain = 1f) => new()
    {
        StartSample = start,
        LengthSamples = length,
        Kind = RenderVoiceKind.Sampler,
        Frequency = 1f,
        GainLeft = gain,
        GainRight = gain,
        AssetIndex = 0,
    };

    /// <summary>plan 内容活跃段相同（公共长声部覆盖整个渲染窗口外），差异只在远期事件。</summary>
    private static RenderPlan VariantPlan(long total, int extraFutureVoices, long seedOffset = 0)
    {
        var voices = new List<RenderVoice> { ConstantVoice(0, total + 100_000) };
        for (int i = 0; i < extraFutureVoices; i++)
        {
            voices.Add(ConstantVoice(total + 1_000 + seedOffset + i * 64, 64));
        }
        return new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = total + 100_000,
            Voices = voices.OrderBy(v => v.StartSample).ToArray(),
            Assets = [SharedOnes],
        };
    }

    /// <summary>交换时机任意，活跃内容相同 ⇒ 输出必须与不交换逐位一致（交换完全透明）。</summary>
    [Test]
    public void Deterministic_interleaved_swaps_are_bitwise_transparent()
    {
        const int total = 4_096;
        var random = new Random(20261113);

        var baseline = new PlanRenderer();
        baseline.Load(VariantPlan(total, extraFutureVoices: 0));
        var baselineBuffer = new float[2 * total];
        for (int offset = 0; offset < total; offset += BlockFrames)
        {
            baseline.Render(baselineBuffer.AsSpan(offset * 2, BlockFrames * 2));
        }

        var swapped = new PlanRenderer();
        swapped.Load(VariantPlan(total, extraFutureVoices: 3, seedOffset: 7));
        var swapBuffer = new float[2 * total];
        int swaps = 0;
        for (int offset = 0; offset < total; offset += BlockFrames)
        {
            if (random.Next(2) == 0)
            {
                swapped.SwapPlan(VariantPlan(total, extraFutureVoices: 1 + random.Next(4), seedOffset: random.Next(64)));
                swaps++;
            }
            swapped.Render(swapBuffer.AsSpan(offset * 2, BlockFrames * 2));
        }
        Assert.That(swaps, Is.GreaterThan(4), "fuzz 确实发生了多次交换");
        Assert.That(swapBuffer, Is.EqualTo(baselineBuffer), "交换对活跃内容必须逐位透明");
    }

    /// <summary>播放中编辑：新增声部在 playhead 之后正常触发；position 推进与交换无关。</summary>
    [Test]
    public void Swapped_in_voice_triggers_after_playhead_and_position_advances()
    {
        const int total = 2_048;
        const int addedStart = 1_500;
        var renderer = new PlanRenderer();
        renderer.Load(VariantPlan(total, 0));
        var buffer = new float[2 * total];
        renderer.Render(buffer.AsSpan(0, 2 * 1_500));
        Assert.That(renderer.Position, Is.EqualTo(1_500));

        var edited = new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = total + 100_000,
            Voices =
            [
                ConstantVoice(0, total + 100_000),
                ConstantVoice(addedStart, 1_000, gain: 0.5f),
            ],
            Assets = [SharedOnes],
        };
        renderer.SwapPlan(edited);
        renderer.Render(buffer.AsSpan(1_500 * 2, 2 * (total - 1_500)));

        Assert.That(renderer.Position, Is.EqualTo(total), "位置推进不受交换影响");
        Assert.Multiple(() =>
        {
            Assert.That(buffer[2 * 1_400], Is.EqualTo(1f).Within(1e-6f), "交换前只有公共声部");
            Assert.That(buffer[2 * addedStart], Is.EqualTo(1.5f).Within(1e-6f), "换入声部从起点叠加");
            Assert.That(buffer[2 * (total - 1)], Is.EqualTo(1.5f).Within(1e-6f));
        });
    }

    /// <summary>静音消除：公共声部全程在飞，任意交换/换 plan 下每样本非零（无缺口）。</summary>
    [Test]
    public void Common_voice_never_goes_silent_across_swaps()
    {
        const int total = 3_072;
        var renderer = new PlanRenderer();
        renderer.Load(VariantPlan(total, 2));
        var buffer = new float[2 * BlockFrames];
        int silentSamples = 0;
        var random = new Random(99);
        for (int done = 0; done < total; done += BlockFrames)
        {
            if (random.Next(3) == 0)
            {
                renderer.SwapPlan(VariantPlan(total, random.Next(5)));
            }
            renderer.Render(buffer);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == 0f)
                {
                    silentSamples++;
                }
            }
        }
        Assert.That(silentSamples, Is.EqualTo(0), "播放中编辑不得产生静音缺口");
    }

    /// <summary>循环模式：交换后回绕重触发与游标重定位正常，第二圈与第一圈逐位一致。</summary>
    [Test]
    public void Loop_wrap_after_swap_repeats_first_loop_bitwise()
    {
        const int loopEnd = 1_024;
        RenderPlan LoopPlan() => new()
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = loopEnd,
            LoopStartSample = 0,
            LoopEndSample = loopEnd,
            Voices = [ConstantVoice(0, loopEnd)],
            Assets = [SharedOnes],
        };
        var renderer = new PlanRenderer();
        renderer.Load(LoopPlan());
        var first = new float[2 * loopEnd];
        var second = new float[2 * loopEnd];
        renderer.Render(first);
        renderer.SwapPlan(LoopPlan());
        renderer.Render(second);
        Assert.That(second, Is.EqualTo(first), "交换后回绕重定位，循环内容逐位一致");
    }

    /// <summary>多线程 chaos：音频线程持续渲染，编辑线程随机交换（不同 plan 实例/数组），
    /// 断言不崩溃、position 单调推进、公共声部无静音缺口——守 RCU 数据竞争。</summary>
    [Test]
    public async Task Concurrent_swap_and_render_keeps_position_monotonic_and_continuous()
    {
        const int total = 51_200; // 200 块
        var renderer = new PlanRenderer();
        renderer.Load(VariantPlan(total, 1));
        var buffer = new float[2 * BlockFrames];
        var silent = 0L;
        var exceptions = new List<Exception>();
        var editor = Task.Run(() =>
        {
            var random = new Random(7);
            try
            {
                for (int i = 0; i < 2_000; i++)
                {
                    renderer.SwapPlan(VariantPlan(total, random.Next(4), seedOffset: random.Next(32)));
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });
        long lastPosition = 0;
        for (int done = 0; done < total; done += BlockFrames)
        {
            renderer.Render(buffer);
            long position = renderer.Position;
            Assert.That(position, Is.GreaterThanOrEqualTo(lastPosition), "position 单调推进");
            lastPosition = position;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == 0f)
                {
                    Interlocked.Increment(ref silent);
                }
            }
        }
        await editor;
        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty);
            Assert.That(silent, Is.EqualTo(0), "并发交换期间公共声部持续有声");
            Assert.That(renderer.Position, Is.EqualTo(total));
        });
    }

    /// <summary>交换+渲染路径零分配（SwapPlan 只是 volatile 写，无分配）。</summary>
    [Test]
    public void Swap_and_render_does_not_allocate()
    {
        const int total = 2_048;
        var renderer = new PlanRenderer();
        renderer.Load(VariantPlan(total, 0));
        var buffer = new float[2 * BlockFrames];
        RtAssert.Reset();
        for (int done = 0; done < total; done += BlockFrames)
        {
            RtAssert.Guard(renderer.Render, buffer);
            renderer.SwapPlan(VariantPlan(total, 1, seedOffset: done));
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0),
            "RCU 交换路径出现托管分配（RT_ASSERT 构建下此断言有效）");
    }
}
