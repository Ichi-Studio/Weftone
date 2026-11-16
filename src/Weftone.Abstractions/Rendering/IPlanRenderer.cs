namespace Weftone.Abstractions.Rendering;

/// <summary>计划渲染端口：Application 经此编排离线/实时渲染，不直连 Engine 实现。</summary>
public interface IPlanRenderer
{
    long Position { get; }

    /// <summary>装载并复位 transport（position/游标/声部池）；仅在后端停止时调用。
    /// plugins 与 plan 槽表配对换入（null = 清空实例表引用）。</summary>
    void Load(RenderPlan plan, IReadOnlyList<Plugins.IAudioPluginInstance>? plugins = null);

    /// <summary>
    /// RCU 交换（SPEC M2 竖切 4 / M3 D5）：播放中原子换入新 plan（含插件实例表，null = 沿用现表），
    /// transport 与活跃声部不重置，事件游标按当前绝对位置二分重定位；
    /// 音频线程绝不阻塞，旧 plan 由 GC 回收、旧实例由宿主侧退役队列释放。
    /// </summary>
    void SwapPlan(RenderPlan plan, IReadOnlyList<Plugins.IAudioPluginInstance>? plugins = null);

    void Render(Span<float> interleaved);
}
