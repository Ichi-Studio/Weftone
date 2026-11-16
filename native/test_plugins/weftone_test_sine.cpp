// weftone_test_sine.cpp — Weftone 自研 CLAP 测试插件（SPEC M3 D4=a）。
// 竖切 1：factory/descriptor + 完整生命周期桩（扫描验证用）；
// 竖切 2 填 DSP：sine 振荡器 + gain/tune 参数。
// 许可：GPL-3.0-or-later（随主仓，不依赖任何专有代码）。
#include <cstdint>
#include <cstdlib>
#include <cstring>

#include "clap/clap.h"

static const char* const k_features[] = { "instrument", "synthesizer", NULL };

static const clap_plugin_descriptor_t k_descriptor = {
    CLAP_VERSION,
    "io.github.ichi-studio.weftone.test-sine",
    "Weftone Test Sine",
    "Ichi Studio",
    "https://github.com/Ichi-Studio/Weftone",
    "",
    "",
    "1.0.0",
    "Weftone 自研测试插件：确定性 sine 合成（CI 黄金基线用）",
    k_features,
};

typedef struct {
    clap_plugin_t plugin;
    const clap_host_t* host;
    double sample_rate;
} sine_plugin_t;

static bool sine_init(const struct clap_plugin* plugin)
{
    (void)plugin;
    return true;
}

static void sine_destroy(const struct clap_plugin* plugin)
{
    if (plugin) {
        free(plugin->plugin_data);
    }
}

static bool sine_activate(const struct clap_plugin* plugin, double sample_rate, uint32_t min_frames, uint32_t max_frames)
{
    (void)min_frames;
    (void)max_frames;
    sine_plugin_t* self = (sine_plugin_t*)plugin->plugin_data;
    self->sample_rate = sample_rate;
    return true;
}

static void sine_deactivate(const struct clap_plugin* plugin) { (void)plugin; }

static bool sine_start_processing(const struct clap_plugin* plugin) { (void)plugin; return true; }
static void sine_stop_processing(const struct clap_plugin* plugin) { (void)plugin; }
static void sine_reset(const struct clap_plugin* plugin) { (void)plugin; }

static clap_process_status sine_process(const struct clap_plugin* plugin, const clap_process_t* process)
{
    (void)plugin;
    // 竖切 2 实现；扫描期不触达
    for (uint32_t c = 0; c < process->audio_outputs_count; c++) {
        memset(process->audio_outputs[c].data32[0], 0, sizeof(float) * process->frames_count);
        memset(process->audio_outputs[c].data32[1], 0, sizeof(float) * process->frames_count);
    }
    return CLAP_PROCESS_CONTINUE;
}

static const void* sine_get_extension(const struct clap_plugin* plugin, const char* id)
{
    (void)plugin;
    (void)id;
    return nullptr;
}

static void sine_on_main_thread(const struct clap_plugin* plugin) { (void)plugin; }

static const clap_plugin_t* sine_create(const clap_host_t* host)
{
    sine_plugin_t* self = (sine_plugin_t*)calloc(1, sizeof(sine_plugin_t));
    if (!self) {
        return nullptr;
    }
    self->host = host;
    self->sample_rate = 48000.0;
    self->plugin.desc = &k_descriptor;
    self->plugin.plugin_data = self;
    self->plugin.init = sine_init;
    self->plugin.destroy = sine_destroy;
    self->plugin.activate = sine_activate;
    self->plugin.deactivate = sine_deactivate;
    self->plugin.start_processing = sine_start_processing;
    self->plugin.stop_processing = sine_stop_processing;
    self->plugin.reset = sine_reset;
    self->plugin.process = sine_process;
    self->plugin.get_extension = sine_get_extension;
    self->plugin.on_main_thread = sine_on_main_thread;
    return &self->plugin;
}

static uint32_t factory_count(const struct clap_plugin_factory* factory)
{
    (void)factory;
    return 1;
}

static const clap_plugin_descriptor_t* factory_descriptor(
    const struct clap_plugin_factory* factory, uint32_t index)
{
    (void)factory;
    return index == 0 ? &k_descriptor : NULL;
}

static const clap_plugin_t* factory_create(
    const struct clap_plugin_factory* factory, const clap_host_t* host, const char* plugin_id)
{
    (void)factory;
    if (strcmp(plugin_id, k_descriptor.id) != 0) {
        return nullptr;
    }
    return sine_create(host);
}

static const clap_plugin_factory_t k_factory = {
    factory_count,
    factory_descriptor,
    factory_create,
};

static bool entry_init(const char* process_id)
{
    (void)process_id;
    return true;
}

static void entry_deinit(void) {}

static const void* entry_factory(const char* factory_id)
{
    if (strcmp(factory_id, CLAP_PLUGIN_FACTORY_ID) == 0) {
        return &k_factory;
    }
    return nullptr;
}

extern "C" CLAP_EXPORT const clap_plugin_entry_t clap_entry = {
    CLAP_VERSION,
    entry_init,
    entry_deinit,
    entry_factory,
};
