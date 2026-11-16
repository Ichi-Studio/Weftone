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
}
