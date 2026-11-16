namespace Weftone.Abstractions.Rendering;

/// <summary>计划渲染端口：Application 经此编排离线/实时渲染，不直连 Engine 实现。</summary>
public interface IPlanRenderer
{
    long Position { get; }

    /// <summary>装载并复位 transport（position/游标/声部池）；仅在后端停止时调用。</summary>
    void Load(RenderPlan plan);

    /// <summary>
    /// RCU 交换（SPEC M2 竖切 4）：播放中原子换入新 plan，transport 与活跃声部不重置，
    /// 事件游标按当前绝对位置二分重定位；音频线程绝不阻塞，旧 plan 由 GC 回收。
    /// </summary>
    void SwapPlan(RenderPlan plan);

    void Render(Span<float> interleaved);
}
