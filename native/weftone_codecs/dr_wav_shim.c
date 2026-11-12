// weftone_codecs shim：dr_wav 解码为 f32 交错 PCM，供
// Weftone.Adapters.Codecs P/Invoke。一次性整文件解码（离线路径，非 RT）。
#include <stdint.h>
#include <stdlib.h>

#if defined(_WIN32)
  #include <windows.h>
  #define WEFT_API __declspec(dllexport)
#else
  #define WEFT_API __attribute__((visibility("default")))
#endif

#define DR_WAV_IMPLEMENTATION
#include "dr_wav.h"

WEFT_API int weft_wav_decode(const void* data, size_t size,
                             float** outSamples, uint32_t* outChannels,
                             uint32_t* outSampleRate, uint64_t* outFrames)
{
    if (data == NULL || outSamples == NULL || outChannels == NULL
        || outSampleRate == NULL || outFrames == NULL)
    {
        return -1;
    }
    *outSamples = NULL;

    drwav wav;
    if (!drwav_init_memory(&wav, data, size, NULL))
    {
        return -2;
    }
    uint64_t frames = wav.totalPCMFrameCount;
    uint32_t channels = wav.channels;
    uint32_t sampleRate = wav.sampleRate;
    if (frames == 0 || channels == 0 || sampleRate == 0
        || frames > SIZE_MAX / (sizeof(float) * channels))
    {
        drwav_uninit(&wav);
        return -3;
    }

    size_t floatCount = (size_t)frames * channels;
    float* buffer = (float*)malloc(floatCount * sizeof(float));
    if (buffer == NULL)
    {
        drwav_uninit(&wav);
        return -4;
    }
    uint64_t read = drwav_read_pcm_frames_f32(&wav, frames, buffer);
    drwav_uninit(&wav);
    if (read != frames)
    {
        free(buffer);
        return -5;
    }

    *outSamples = buffer;
    *outChannels = channels;
    *outSampleRate = sampleRate;
    *outFrames = frames;
    return 0;
}

WEFT_API void weft_wav_free(float* samples)
{
    free(samples);
}
