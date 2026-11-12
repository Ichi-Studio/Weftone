using Weftone.Abstractions;

namespace Weftone.Engine;

/// <summary>
/// 音频路径分配断言（AGENTS.md §9）：仅 RT_ASSERT 构建生效，
/// 违规只计数绝不抛异常——音频线程零异常抛出。
/// </summary>
public static class RtAssert
{
    private static long _violations;

    public static long Violations => Interlocked.Read(ref _violations);

    public static void Reset() => Interlocked.Exchange(ref _violations, 0);

    public static void Guard(AudioRenderCallback body, Span<float> buffer)
    {
#if RT_ASSERT
        long before = GC.GetAllocatedBytesForCurrentThread();
        body(buffer);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        if (delta != 0)
        {
            Interlocked.Increment(ref _violations);
        }
#else
        body(buffer);
#endif
    }
}
