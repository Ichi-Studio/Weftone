// weftone_miniaudio shim：以稳定 C ABI 暴露 miniaudio 播放设备，
// 供 Weftone.Adapters.Audio.Miniaudio P/Invoke。M0 限定单设备、单回调。
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#if defined(_WIN32)
  #include <windows.h>
  #define WEFT_API __declspec(dllexport)
#else
  #define WEFT_API __attribute__((visibility("default")))
#endif

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

typedef void (*weft_render_cb)(float* out, uint32_t frames, uint32_t channels, void* user);

static ma_device g_device;
static weft_render_cb g_cb;
static void* g_user;
static uint32_t g_channels;
static int g_open;
static int g_started;
static char g_error[256];

#ifdef _WIN32
static int g_mmcss_done;
#endif

// AGENTS.md §9：Windows 音频线程挂 MMCSS "Pro Audio"；动态加载 avrt 避免链接依赖。
// M0 单设备即单回调线程，置位一次即可。
static void weft_mark_mmcss(void)
{
#ifdef _WIN32
    typedef HANDLE(WINAPI* av_set_fn)(LPCWSTR, LPDWORD);
    if (g_mmcss_done)
        return;
    g_mmcss_done = 1;
    HMODULE avrt = LoadLibraryW(L"avrt.dll");
    if (avrt == NULL)
        return;
    av_set_fn set_fn = (av_set_fn)(void (*)(void))GetProcAddress(avrt, "AvSetMmThreadCharacteristicsW");
    if (set_fn != NULL)
    {
        DWORD index = 0;
        set_fn(L"Pro Audio", &index);
    }
#endif
}

static void weft_data_callback(ma_device* device, void* output, const void* input, uint32_t frameCount)
{
    (void)device;
    (void)input;
    weft_mark_mmcss();
    if (g_cb != NULL)
    {
        g_cb((float*)output, frameCount, g_channels, g_user);
    }
    else
    {
        memset(output, 0, (size_t)frameCount * g_channels * sizeof(float));
    }
}

static void weft_set_error(const char* msg)
{
    snprintf(g_error, sizeof(g_error), "%s", msg);
}

WEFT_API int weft_audio_open(uint32_t sampleRate, uint32_t channels, uint32_t bufferFrames,
                             weft_render_cb callback, void* userData)
{
    if (g_open)
    {
        weft_set_error("device already open");
        return -1;
    }
    if (callback == NULL || sampleRate == 0 || channels == 0 || bufferFrames == 0)
    {
        weft_set_error("invalid arguments");
        return -1;
    }

    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.sampleRate = sampleRate;
    config.playback.format = ma_format_f32;
    config.playback.channels = channels;
    config.periodSizeInFrames = bufferFrames;
    config.dataCallback = weft_data_callback;

    ma_result result = ma_device_init(NULL, &config, &g_device);
    if (result != MA_SUCCESS)
    {
        snprintf(g_error, sizeof(g_error), "ma_device_init failed: %d", (int)result);
        return (int)result;
    }

    g_cb = callback;
    g_user = userData;
    g_channels = channels;
    g_open = 1;
    g_started = 0;
    g_error[0] = '\0';
    return 0;
}

WEFT_API int weft_audio_start(void)
{
    if (!g_open)
    {
        weft_set_error("device not open");
        return -1;
    }
    if (g_started)
        return 0;
    ma_result result = ma_device_start(&g_device);
    if (result != MA_SUCCESS)
    {
        snprintf(g_error, sizeof(g_error), "ma_device_start failed: %d", (int)result);
        return (int)result;
    }
    g_started = 1;
    return 0;
}

WEFT_API int weft_audio_stop(void)
{
    if (!g_open)
    {
        weft_set_error("device not open");
        return -1;
    }
    if (!g_started)
        return 0;
    ma_result result = ma_device_stop(&g_device);
    if (result != MA_SUCCESS)
    {
        snprintf(g_error, sizeof(g_error), "ma_device_stop failed: %d", (int)result);
        return (int)result;
    }
    g_started = 0;
    return 0;
}

WEFT_API int weft_audio_close(void)
{
    if (!g_open)
        return 0;
    ma_device_uninit(&g_device);
    g_open = 0;
    g_started = 0;
    g_cb = NULL;
    g_user = NULL;
    return 0;
}

WEFT_API const char* weft_audio_last_error(void)
{
    return g_error;
}
