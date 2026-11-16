// weft_stretch.cpp — Signalsmith Stretch 离线 C shim（SPEC M4 D2/D3=a）。
// 离线预渲染路径（AssetPreparer），非 RT；输出帧数确定：floor(in_frames × duration_ratio)，
// 截断于 out_cap。同输入同参数输出位级确定（进程内）。
#include "signalsmith-stretch.h"

#include <cstdlib>

#if defined(_WIN32)
#define WEFT_EXPORT extern "C" __declspec(dllexport)
#else
#define WEFT_EXPORT extern "C" __attribute__((visibility("default")))
#endif

using Stretch = signalsmith::stretch::SignalsmithStretch<float>;

static constexpr int kBlockIn = 1024;
static constexpr int kMaxChannels = 2;
static constexpr int kOutBufFrames = kBlockIn * 2 + 2;

// interleaved 交错输入；durationRatio>1 = 输出更长（慢速）；不变调。
// 返回输出帧数（≤ out_cap），参数非法 -1。
WEFT_EXPORT int32_t weft_stretch_render(const float* input, int32_t in_frames, int32_t channels,
                                        double sample_rate, double duration_ratio,
                                        float* output, int32_t out_cap)
{
    if (!input || !output || in_frames <= 0 || channels < 1 || channels > kMaxChannels
        || !(sample_rate > 0) || !(duration_ratio > 0) || out_cap <= 0)
    {
        return -1;
    }

    double targetReal = (double)in_frames * duration_ratio;
    int32_t target = (int32_t)targetReal;
    if (target > out_cap)
    {
        target = out_cap;
    }

    // 短输入自适应：exact 要求 inputSamples >= outputSeekLength(=inputLatency+rate*outputLatency)
    // 块过大使短采样被"礼貌性全零"——按输入长度折半块到可容纳，仍不足则零 pad。
    Stretch stretch;
    int block = (int)(sample_rate * 0.001 * 60); // 60ms 起
    if (block < 64) block = 64;
    while (block > 64 && in_frames < block * 3)
    {
        block /= 2;
    }
    stretch.configure(channels, block, block / 4, false);
    stretch.setTransposeFactor(1.0); // 不变调

    float playbackRate = (float)in_frames / (float)target;
    int32_t seekLength = stretch.outputSeekLength(playbackRate);
    int32_t paddedFrames = in_frames;
    float* padding = nullptr;
    if (in_frames < seekLength + block)
    {
        paddedFrames = seekLength + block;
        padding = (float*)calloc((size_t)paddedFrames * channels, sizeof(float));
        if (!padding)
        {
            return -1;
        }
    }

    // 官方整段用法（cmd 示例）：exact 一步完成，含起播对齐与收尾 flush
    float* inChannel[kMaxChannels] = { nullptr, nullptr };
    float* outChannel[kMaxChannels] = { nullptr, nullptr };
    float* inDeinterleave = (float*)malloc(sizeof(float) * (size_t)paddedFrames * channels);
    float* outDeinterleave = (float*)malloc(sizeof(float) * (size_t)target * channels);
    if (!inDeinterleave || !outDeinterleave)
    {
        free(inDeinterleave);
        free(outDeinterleave);
        free(padding);
        return -1;
    }
    for (int32_t c = 0; c < channels; c++)
    {
        inChannel[c] = inDeinterleave + (size_t)paddedFrames * c;
        outChannel[c] = outDeinterleave + (size_t)target * c;
        for (int32_t i = 0; i < in_frames; i++)
        {
            inChannel[c][i] = input[(long)i * channels + c];
        }
    }
    stretch.exact(inChannel, paddedFrames, outChannel, target);
    for (int32_t c = 0; c < channels; c++)
    {
        for (int32_t i = 0; i < target; i++)
        {
            output[(long)i * channels + c] = outChannel[c][i];
        }
    }
    free(inDeinterleave);
    free(outDeinterleave);
    free(padding);
    return target;
}