// weftone_test_sine.cpp — Weftone 自研 CLAP 测试插件（SPEC M3 D4=a）。
// 竖切 2：16 复音确定性 sine 合成（双精度相位、立即 note off、L==R、振幅 0.25），
// 竖切 1 的 descriptors/生命周期桩 + audio-ports 立体声声明。
// 许可：GPL-3.0-or-later（随主仓，不依赖任何专有代码）。
#include <cmath>
#include <cstdint>
#include <cstdio>
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

#define WEFT_MAX_VOICES 16
// 参数 id（clap_id = uint32）
#define WEFT_PARAM_GAIN 1
#define WEFT_PARAM_TUNE 2

static const double k_two_pi = 6.283185307179586476925286766559;

typedef struct {
    bool active;
    int16_t key;
    int16_t channel;
    double phase;
    double phase_delta;
} sine_voice_t;

typedef struct {
    clap_plugin_t plugin;
    const clap_host_t* host;
    double sample_rate;
    double gain;    // 0..1，默认 0.25
    double tune;    // -24..24 半音，默认 0
    sine_voice_t voices[WEFT_MAX_VOICES];
} sine_plugin_t;

static double key_to_frequency(int key)
{
    return 440.0 * pow(2.0, (key - 69) / 12.0);
}

static void note_on(sine_plugin_t* self, int16_t key, int16_t channel, double velocity)
{
    (void)velocity;
    // 同 (channel,key) 已发声 → 重触发；否则取空位
    for (int i = 0; i < WEFT_MAX_VOICES; i++) {
        if (self->voices[i].active && self->voices[i].key == key && self->voices[i].channel == channel) {
            self->voices[i].phase = 0;
            return;
        }
    }
    for (int i = 0; i < WEFT_MAX_VOICES; i++) {
        if (!self->voices[i].active) {
            self->voices[i].active = true;
            self->voices[i].key = key;
            self->voices[i].channel = channel;
            self->voices[i].phase = 0;
            self->voices[i].phase_delta = k_two_pi * key_to_frequency(key) * pow(2.0, self->tune / 12.0) / self->sample_rate;
            return;
        }
    }
    // 池满：丢弃（测试插件，M3 无 stealing）
}

static void note_off(sine_plugin_t* self, int16_t key, int16_t channel)
{
    for (int i = 0; i < WEFT_MAX_VOICES; i++) {
        if (self->voices[i].active && self->voices[i].key == key && self->voices[i].channel == channel) {
            self->voices[i].active = false; // 立即截止（确定性优先，测试插件不做淡出）
            return;
        }
    }
}

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
    sine_plugin_t* self = (sine_plugin_t*)plugin->plugin_data;
    float* out[2] = { NULL, NULL };
    if (process->audio_outputs_count > 0 && process->audio_outputs[0].data32) {
        out[0] = process->audio_outputs[0].data32[0];
        out[1] = process->audio_outputs[0].data32[1];
    }
    if (out[0]) {
        memset(out[0], 0, sizeof(float) * process->frames_count);
        memset(out[1], 0, sizeof(float) * process->frames_count);
    }

    uint32_t next_event = 0;
    const uint32_t frames = process->frames_count;
    for (uint32_t n = 0; n < frames; n++) {
        while (next_event < process->in_events->size(process->in_events)) {
            const clap_event_header_t* header = process->in_events->get(process->in_events, next_event);
            if (header->time > n) {
                break;
            }
            if (header->time == n && header->type == CLAP_EVENT_NOTE_ON) {
                const clap_event_note_t* note = (const clap_event_note_t*)header;
                note_on(self, note->key, note->channel, note->velocity);
            } else if (header->time == n && header->type == CLAP_EVENT_NOTE_OFF) {
                const clap_event_note_t* note = (const clap_event_note_t*)header;
                note_off(self, note->key, note->channel);
            } else if (header->time == n && header->type == CLAP_EVENT_PARAM_VALUE) {
                const clap_event_param_value_t* param = (const clap_event_param_value_t*)header;
                if (param->param_id == WEFT_PARAM_GAIN) {
                    self->gain = param->value;
                } else if (param->param_id == WEFT_PARAM_TUNE) {
                    self->tune = param->value;
                    for (int i = 0; i < WEFT_MAX_VOICES; i++) {
                        if (self->voices[i].active) {
                            self->voices[i].phase_delta =
                                k_two_pi * key_to_frequency(self->voices[i].key)
                                    * pow(2.0, self->tune / 12.0) / self->sample_rate;
                        }
                    }
                }
            }
            next_event++;
        }
        if (!out[0]) {
            continue;
        }
        float sample = 0.0f;
        for (int i = 0; i < WEFT_MAX_VOICES; i++) {
            sine_voice_t* voice = &self->voices[i];
            if (!voice->active) {
                continue;
            }
            sample += (float)(sin(voice->phase) * self->gain);
            voice->phase += voice->phase_delta;
            if (voice->phase >= k_two_pi) {
                voice->phase -= k_two_pi;
            }
        }
        out[0][n] = sample;
        out[1][n] = sample;
    }
    return CLAP_PROCESS_CONTINUE;
}

// ---- audio-ports：1 立体声输出（M3 D3 固定 2in/2out 的乐器侧） ----
static uint32_t ports_count(const struct clap_plugin* plugin, bool is_input)
{
    (void)plugin;
    return is_input ? 0 : 1;
}

static bool ports_get(const struct clap_plugin* plugin, uint32_t index, bool is_input, clap_audio_port_info_t* info)
{
    (void)plugin;
    if (is_input || index != 0) {
        return false;
    }
    memset(info, 0, sizeof *info);
    info->id = 0;
    snprintf(info->name, CLAP_NAME_SIZE, "%s", "output");
    info->flags = CLAP_AUDIO_PORT_IS_MAIN;
    info->channel_count = 2;
    info->port_type = CLAP_PORT_STEREO;
    info->in_place_pair = CLAP_INVALID_ID;
    return true;
}

static const clap_plugin_audio_ports_t k_audio_ports = {
    ports_count,
    ports_get,
};


// ---- params：gain / tune（竖切 3） ----
static const clap_param_info_t k_params[] = {
    { WEFT_PARAM_GAIN, CLAP_PARAM_IS_AUTOMATABLE, NULL, "Gain", "", 0.0, 1.0, 0.25 },
    { WEFT_PARAM_TUNE, CLAP_PARAM_IS_AUTOMATABLE, NULL, "Tune", "", -24.0, 24.0, 0.0 },
};

static uint32_t params_count(const struct clap_plugin* plugin)
{
    (void)plugin;
    return 2;
}

static bool params_get_info(const struct clap_plugin* plugin, uint32_t index, clap_param_info_t* info)
{
    (void)plugin;
    if (index >= 2) {
        return false;
    }
    *info = k_params[index];
    return true;
}

static bool params_get_value(const struct clap_plugin* plugin, clap_id id, double* out)
{
    const sine_plugin_t* self = (const sine_plugin_t*)plugin->plugin_data;
    if (id == WEFT_PARAM_GAIN) {
        *out = self->gain;
        return true;
    }
    if (id == WEFT_PARAM_TUNE) {
        *out = self->tune;
        return true;
    }
    return false;
}

static bool params_value_to_text(const struct clap_plugin* plugin, clap_id id, double value, char* out, uint32_t size)
{
    (void)plugin;
    (void)id;
    snprintf(out, size, "%.3f", value);
    return true;
}

static bool params_text_to_value(const struct clap_plugin* plugin, clap_id id, const char* text, double* out)
{
    (void)plugin;
    (void)id;
    char* end = NULL;
    double value = strtod(text, &end);
    if (end == text) {
        return false;
    }
    *out = value;
    return true;
}

static void params_flush(const struct clap_plugin* plugin, const clap_input_events_t* in, const clap_output_events_t* out)
{
    (void)plugin;
    (void)in;
    (void)out;
}

static const clap_plugin_params_t k_params_ext = {
    params_count,
    params_get_info,
    params_get_value,
    params_value_to_text,
    params_text_to_value,
    params_flush,
};

static bool state_save(const struct clap_plugin* plugin, const clap_ostream_t* stream)
{
    const sine_plugin_t* self = (const sine_plugin_t*)plugin->plugin_data;
    double payload[2] = { self->gain, self->tune };
    return stream->write(stream, payload, sizeof payload) == (int64_t)sizeof payload;
}

static bool state_load(const struct clap_plugin* plugin, const clap_istream_t* stream)
{
    sine_plugin_t* self = (sine_plugin_t*)plugin->plugin_data;
    double payload[2] = { 0, 0 };
    if (stream->read(stream, payload, sizeof payload) != (int64_t)sizeof payload) {
        return false;
    }
    self->gain = payload[0];
    self->tune = payload[1];
    // tune 变化须重算在飞声部频率
    for (int i = 0; i < WEFT_MAX_VOICES; i++) {
        if (self->voices[i].active) {
            self->voices[i].phase_delta =
                k_two_pi * key_to_frequency(self->voices[i].key) * pow(2.0, self->tune / 12.0) / self->sample_rate;
        }
    }
    return true;
}

static const clap_plugin_state_t k_state_ext = { state_save, state_load };

// ---- latency：固定 32 样本（竖切 3 报告验证用；宿主不补偿） ----
static uint32_t latency_get(const struct clap_plugin* plugin)
{
    (void)plugin;
    return 32;
}

static const clap_plugin_latency_t k_latency_ext = { latency_get };

static const void* sine_get_extension(const struct clap_plugin* plugin, const char* id)
{
    (void)plugin;
    if (strcmp(id, CLAP_EXT_AUDIO_PORTS) == 0) {
        return &k_audio_ports;
    }
    if (strcmp(id, CLAP_EXT_PARAMS) == 0) {
        return &k_params_ext;
    }
    if (strcmp(id, CLAP_EXT_STATE) == 0) {
        return &k_state_ext;
    }
    if (strcmp(id, CLAP_EXT_LATENCY) == 0) {
        return &k_latency_ext;
    }
    return NULL;
}

static void sine_on_main_thread(const struct clap_plugin* plugin) { (void)plugin; }

static const clap_plugin_t* sine_create(const clap_host_t* host)
{
    sine_plugin_t* self = (sine_plugin_t*)calloc(1, sizeof(sine_plugin_t));
    if (!self) {
        return NULL;
    }
    self->host = host;
    self->sample_rate = 48000.0;
    self->gain = 0.25;
    self->tune = 0.0;
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
        return NULL;
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

static void entry_deinit() {}

static const void* entry_factory(const char* factory_id)
{
    if (strcmp(factory_id, CLAP_PLUGIN_FACTORY_ID) == 0) {
        return &k_factory;
    }
    return NULL;
}

extern "C" CLAP_EXPORT const clap_plugin_entry_t clap_entry = {
    CLAP_VERSION,
    entry_init,
    entry_deinit,
    entry_factory,
};
