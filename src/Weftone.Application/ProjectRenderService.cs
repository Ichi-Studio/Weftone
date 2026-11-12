using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Domain;

namespace Weftone.Application;

public sealed record RenderedProject(
    float[] Interleaved, int SampleRate, int Channels, long Frames, double Peak, TimeSpan Duration);

/// <summary>导出用例：.weft → 资产准备 → 编译 → 离线渲染（ADR-0003 全离线）。</summary>
public sealed class ProjectRenderService(
    IProjectStore store,
    ISampleDecoder decoder,
    IResampler resampler,
    IPlanRenderer renderer)
{
    private const int BlockFrames = 512;

    public RenderedProject Render(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        LoadedProject loaded = store.Load(projectPath);
        List<PreparedAsset> assets = new AssetPreparer()
            .Prepare(loaded.Document, loaded.OpenAsset, decoder, resampler);
        RenderPlan plan = new DocumentCompiler().Compile(loaded.Document, assets);

        renderer.Load(plan);
        long frames = plan.TotalSamples;
        var interleaved = new float[frames * plan.Channels];
        var block = new float[BlockFrames * plan.Channels];
        double peak = 0;
        for (long done = 0; done < frames; done += BlockFrames)
        {
            int count = (int)Math.Min(BlockFrames, frames - done);
            Span<float> span = block.AsSpan(0, count * plan.Channels);
            renderer.Render(span);
            span.CopyTo(interleaved.AsSpan((int)(done * plan.Channels), span.Length));
            for (int i = 0; i < span.Length; i++)
            {
                double magnitude = Math.Abs(span[i]);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }
        }
        return new RenderedProject(interleaved, plan.SampleRate, plan.Channels, frames, peak,
            TimeSpan.FromSeconds((double)frames / plan.SampleRate));
    }
}
