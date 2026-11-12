namespace Weftone.Abstractions.Rendering;

/// <summary>计划渲染端口：Application 经此编排离线/实时渲染，不直连 Engine 实现。</summary>
public interface IPlanRenderer
{
    long Position { get; }

    void Load(RenderPlan plan);

    void Render(Span<float> interleaved);
}
