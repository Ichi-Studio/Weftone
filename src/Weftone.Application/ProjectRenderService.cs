using Weftone.Abstractions;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;
using Weftone.Domain;

namespace Weftone.Application;

public sealed record RenderedProject(
    float[] Interleaved, int SampleRate, int Channels, long Frames, double Peak, TimeSpan Duration);

/// <summary>导出用例：.weft → 资产准备 → 编译 → 离线渲染（ADR-0003 全离线）。
/// 插件工程经临时实例表渲染（单线程，用后即毁）。</summary>
public sealed class ProjectRenderService(
    IProjectStore store,
    ISampleDecoder decoder,
    IResampler resampler,
    IPlanRenderer renderer,
    IPluginInstanceFactory? pluginFactory = null,
    Weftone.Abstractions.Dsp.ITimeStretcher? stretcher = null)
{
    private const int BlockFrames = 512;

    public RenderedProject Render(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        LoadedProject loaded = store.Load(projectPath);
        List<PreparedAsset> assets = new AssetPreparer()
            .Prepare(loaded.Document, loaded.OpenAsset, decoder, resampler, stretcher);
        RenderPlan plan = new DocumentCompiler().Compile(loaded.Document, assets);

        IReadOnlyList<IAudioPluginInstance>? table = null;
        PluginInstanceHost? host = null;
        if (plan.PluginSlots.Length > 0 && pluginFactory is not null)
        {
            host = new PluginInstanceHost(pluginFactory);
            table = host.Sync(plan);
        }
        try
        {
            return RenderPlan(plan);
        }
        finally
        {
            host?.Dispose();
        }

        RenderedProject RenderPlan(RenderPlan captured)
        {
            renderer.Load(captured, table);
            long frames = captured.TotalSamples;
            var interleaved = new float[frames * captured.Channels];
            var block = new float[BlockFrames * captured.Channels];
            double peak = 0;
            for (long done = 0; done < frames; done += BlockFrames)
            {
                int count = (int)Math.Min(BlockFrames, frames - done);
                Span<float> span = block.AsSpan(0, count * captured.Channels);
                renderer.Render(span);
                span.CopyTo(interleaved.AsSpan((int)(done * captured.Channels), span.Length));
                for (int i = 0; i < span.Length; i++)
                {
                    double magnitude = Math.Abs(span[i]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }
            }
            return new RenderedProject(interleaved, captured.SampleRate, captured.Channels, frames, peak,
                TimeSpan.FromSeconds((double)frames / captured.SampleRate));
        }
    }
}
