using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;
using Weftone.Engine.Effects;

namespace Weftone.Engine.Tests;

/// <summary>SPEC M5 竖切 1：EQ 处理器（RBJ biquad 确定性/增益/零分配）+ 通道分组渲染。</summary>
public class EffectRenderTests
{
    [Test]
    public void Eq_boosts_energy_and_is_bitwise_deterministic()
    {
        var input = new float[2 * 512];
        for (int i = 0; i < 512; i++)
        {
            input[2 * i] = MathF.Sin(2f * MathF.PI * 100f * i / 48_000f); // 低频
            input[2 * i + 1] = input[2 * i];
        }
        var first = (float[])input.Clone();
        new Eq3BandProcessor(6, 0, -3).Process(first, 512);
        var second = (float[])input.Clone();
        new Eq3BandProcessor(6, 0, -3).Process(second, 512);

        Assert.That(second, Is.EqualTo(first), "同输入同参数位级确定");
        double inEnergy = 0, outEnergy = 0;
        for (int i = 0; i < first.Length; i++)
        {
            inEnergy += Math.Abs(input[i]);
            outEnergy += Math.Abs(first[i]);
        }
        Assert.That(outEnergy, Is.GreaterThan(inEnergy * 1.3), "低架 +6dB 提升低频能量");
    }

    [Test]
    public void Channel_with_effect_renders_through_chain_and_zero_allocates()
    {
        // 带 EQ 的通道：voice 走缓冲→EQ→总线；与手动 EQ 后求和一致
        var plan = new RenderPlan
        {
            SampleRate = 48_000,
            Channels = 2,
            TotalSamples = 512,
            ChannelCount = 1,
            Voices =
            [
                new RenderVoice
                {
                    StartSample = 0, LengthSamples = 512, Kind = RenderVoiceKind.Sine,
                    Frequency = 100f, GainLeft = 0.5f, GainRight = 0.5f,
                    AssetIndex = -1, ChannelIndex = 0,
                },
            ],
            EffectNodes =
            [
                new Abstractions.Plugins.RenderEffectNode
                {
                    ChannelIndex = 0, Kind = "Eq3Band", Parameters = [6, 0, 0],
                },
            ],
        };
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        var buffer = new float[2 * 512];
        AudioRenderCallback callback = renderer.Render;
        RtAssert.Guard(callback, buffer);

        // 解析对照：不带 EQ 渲染，再手动过同一 EQ，应逐位一致
        var plainPlan = new RenderPlan
        {
            SampleRate = 48_000, Channels = 2, TotalSamples = 512, ChannelCount = 1,
            Voices = [new RenderVoice
            {
                StartSample = 0, LengthSamples = 512, Kind = RenderVoiceKind.Sine,
                Frequency = 100f, GainLeft = 0.5f, GainRight = 0.5f, AssetIndex = -1,
            }],
        };
        var plain = new PlanRenderer();
        plain.Load(plainPlan);
        var expected = new float[2 * 512];
        plain.Render(expected);
        new Eq3BandProcessor(6, 0, 0).Process(expected, 512);

        Assert.That(buffer, Is.EqualTo(expected), "效果链路径 = 离线同参 EQ 处理结果");
        for (int i = 0; i < 300; i++)
        {
            RtAssert.Guard(callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0), "效果渲染路径零分配");
    }

    [Test]
    public void Effect_channel_without_voices_outputs_silence()
    {
        var plan = new RenderPlan
        {
            SampleRate = 48_000, Channels = 2, TotalSamples = 256, ChannelCount = 1,
            Voices = [],
            EffectNodes = [new Abstractions.Plugins.RenderEffectNode
            {
                ChannelIndex = 0, Kind = "Eq3Band", Parameters = [6, 0, 0],
            }],
        };
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        var buffer = new float[2 * 256];
        renderer.Render(buffer);
        Assert.That(buffer, Is.All.EqualTo(0f));
    }

    [Test]
    public void Compressor_attenuates_loud_and_is_deterministic()
    {
        var loud = new float[2 * 512];
        for (int i = 0; i < 512; i++)
        {
            loud[2 * i] = loud[2 * i + 1] = 0.9f;
        }
        var first = (float[])loud.Clone();
        new CompressorProcessor(-20, 4, 1, 50).Process(first, 512);
        var second = (float[])loud.Clone();
        new CompressorProcessor(-20, 4, 1, 50).Process(second, 512);
        Assert.That(second, Is.EqualTo(first), "位级确定");
        Assert.That(Math.Abs(first[^1]), Is.LessThan(0.5f), "0.9 输入经压缩显著衰减");
        Assert.That(Math.Abs(first[^1]), Is.GreaterThan(0.1f), "不静音");
    }

    [Test]
    public void Delay_produces_echoes()
    {
        var impulse = new float[2 * 14_400];
        impulse[0] = impulse[1] = 1f;
        new DelayProcessor(100, 0.5, 1.0).Process(impulse, 14_400);
        // 100ms = 4800 样本处首回声（0.5 反馈 × 0.7071 ≈ 0.35）；9600 处第二回声
        Assert.That(Math.Abs(impulse[2 * 4800]), Is.GreaterThan(0.1f), "首回声位置");
        Assert.That(Math.Abs(impulse[2 * 9600]), Is.GreaterThan(0.01f), "反馈第二回声");
    }

    [Test]
    public void Reverb_spreads_energy_and_decays()
    {
        var impulse = new float[2 * 48_000];
        impulse[0] = impulse[1] = 1f;
        var wet = (float[])impulse.Clone();
        new ReverbProcessor(0.5, 0.5, 10).Process(wet, 48_000);
        double early = 0, late = 0;
        for (int i = 0; i < 24_000; i++) early += Math.Abs(wet[2 * i]);
        for (int i = 24_000; i < 48_000; i++) late += Math.Abs(wet[2 * i]);
        Assert.Multiple(() =>
        {
            Assert.That(early, Is.GreaterThan(1.0), "前半窗有能量");
            Assert.That(late, Is.GreaterThan(0.0), "尾部延续");
            Assert.That(late, Is.LessThan(early), "衰减");
        });
        var again = (float[])impulse.Clone();
        new ReverbProcessor(0.5, 0.5, 10).Process(again, 48_000);
        Assert.That(again, Is.EqualTo(wet), "位级确定");
    }
}
