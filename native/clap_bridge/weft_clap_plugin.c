// weft_clap_plugin.c — CLAP 实例桥（SPEC M3 D2/D5）：
// create（非 RT：加载→init→activate→start_processing，校验立体声输出端口）、
// process（RT：零分配——事件数组随实例预分配，音频缓冲由调用方传入）、
// destroy（非 RT）。宿主回调为最小桩（测试插件无需宿主扩展）。
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
typedef HMODULE weft_lib_t;
static weft_lib_t weft_load(const char* path) { return LoadLibraryA(path); }
static void* weft_sym(weft_lib_t lib, const char* name) { return (void*)(uintptr_t)GetProcAddress(lib, name); }
static void weft_unload(weft_lib_t lib) { FreeLibrary(lib); }
#else
#include <dlfcn.h>
typedef void* weft_lib_t;
static weft_lib_t weft_load(const char* path) { return dlopen(path, RTLD_NOW | RTLD_LOCAL); }
static void* weft_sym(weft_lib_t lib, const char* name) { return dlsym(lib, name); }
static void weft_unload(weft_lib_t lib) { dlclose(lib); }
#endif

#include "clap/clap.h"

#if defined(_WIN32)
#define WEFT_EXPORT __declspec(dllexport)
#else
#define WEFT_EXPORT __attribute__((visibility("default")))
#endif

#define WEFT_MAX_EVENTS 256

// 与 C# WeftNoteEventNative 布局对齐（4 字节对齐，size 20）
typedef struct {
    int32_t frame;
    int32_t is_on;
    int32_t key;
    int32_t channel;
    float velocity;
} weft_clap_note_t;

typedef struct {
    weft_lib_t lib;
    const clap_plugin_entry_t* entry;
    const clap_plugin_t* plugin;
    clap_host_t host;
    clap_input_events_t in_events;
    clap_output_events_t out_events;
    clap_event_note_t events[WEFT_MAX_EVENTS];
    uint32_t event_count;
    clap_process_t process;
    clap_audio_buffer_t audio_out;
    float* out_channels[2];
    int64_t steady_time;
} weft_clap_instance_t;

// ---- 宿主回调（最小桩；host_data 指回实例） ----
static const void* host_get_extension(const clap_host_t* host, const char* extension_id)
{
    (void)host;
    (void)extension_id;
    return NULL;
}

static void host_request_restart(const clap_host_t* host) { (void)host; }
static void host_request_process(const clap_host_t* host) { (void)host; }
static bool host_request_callback(const clap_host_t* host) { (void)host; return false; }

// ---- 事件流 ----
static uint32_t in_events_size(const clap_input_events_t* list)
{
    return ((const weft_clap_instance_t*)list->ctx)->event_count;
}

static const clap_event_header_t* in_events_get(const clap_input_events_t* list, uint32_t index)
{
    weft_clap_instance_t* self = (weft_clap_instance_t*)list->ctx;
    return index < self->event_count ? &self->events[index].header : NULL;
}

static bool out_events_try_push(const clap_output_events_t* list, const clap_event_header_t* event)
{
    (void)list;
    (void)event;
    return false;
}

WEFT_EXPORT void* weft_clap_plugin_create(const char* path, const char* plugin_id, double sample_rate)
{
    if (!path || !plugin_id || sample_rate <= 0) {
        return NULL;
    }
    weft_clap_instance_t* self = (weft_clap_instance_t*)calloc(1, sizeof(weft_clap_instance_t));
    if (!self) {
        return NULL;
    }
    self->lib = weft_load(path);
    if (!self->lib) {
        free(self);
        return NULL;
    }
    int inited = 0;
    int activated = 0;
    const clap_plugin_t* plugin = NULL;
    int processing = 0;

    self->entry = (const clap_plugin_entry_t*)weft_sym(self->lib, "clap_entry");
    if (!self->entry || !self->entry->init || !self->entry->get_factory
        || self->entry->clap_version.major != 1) {
        goto fail;
    }
    if (!self->entry->init("io.github.ichi-studio.weftone")) {
        goto fail;
    }
    inited = 1;

    self->host.clap_version = CLAP_VERSION;
    self->host.host_data = self;
    self->host.name = "Weftone";
    self->host.vendor = "Ichi Studio";
    self->host.url = "https://github.com/Ichi-Studio/Weftone";
    self->host.version = "0.1";
    self->host.get_extension = host_get_extension;
    self->host.request_restart = host_request_restart;
    self->host.request_process = host_request_process;
    self->host.request_callback = host_request_callback;

    const clap_plugin_factory_t* factory =
        (const clap_plugin_factory_t*)self->entry->get_factory(CLAP_PLUGIN_FACTORY_ID);
    if (!factory || !factory->create_plugin) {
        goto fail;
    }
    plugin = factory->create_plugin(factory, &self->host, plugin_id);
    if (!plugin || !plugin->init || !plugin->process || !plugin->activate || !plugin->destroy) {
        goto fail;
    }
    if (!plugin->init(plugin)) {
        goto fail;
    }
    self->plugin = plugin;

    // 端口契约（M3 D3）：0 号输出端口必须为立体声
    const clap_plugin_audio_ports_t* ports =
        (const clap_plugin_audio_ports_t*)plugin->get_extension(plugin, CLAP_EXT_AUDIO_PORTS);
    clap_audio_port_info_t info;
    if (!ports || !ports->count || !ports->get
        || ports->count(plugin, false) < 1
        || !ports->get(plugin, 0, false, &info)
        || info.channel_count != 2) {
        goto fail;
    }

    if (!plugin->activate(plugin, sample_rate, 1, 4096)) {
        goto fail;
    }
    activated = 1;
    if (plugin->start_processing && !plugin->start_processing(plugin)) {
        goto fail;
    }
    processing = 1;

    self->in_events.ctx = self;
    self->in_events.size = in_events_size;
    self->in_events.get = in_events_get;
    self->out_events.ctx = self;
    self->out_events.try_push = out_events_try_push;

    self->audio_out.data32 = self->out_channels;
    self->audio_out.channel_count = 2;
    self->audio_out.latency = 0;
    self->audio_out.constant_mask = 0;

    self->process.steady_time = 0;
    self->process.transport = NULL;
    self->process.audio_inputs = NULL;
    self->process.audio_inputs_count = 0;
    self->process.audio_outputs = &self->audio_out;
    self->process.audio_outputs_count = 1;
    self->process.in_events = &self->in_events;
    self->process.out_events = &self->out_events;
    (void)activated;
    (void)processing;
    return self;

fail:
    if (plugin && plugin->destroy) {
        if (processing && plugin->stop_processing) {
            plugin->stop_processing(plugin);
        }
        if (activated && plugin->deactivate) {
            plugin->deactivate(plugin);
        }
        plugin->destroy(plugin);
    }
    if (inited && self->entry->deinit) {
        self->entry->deinit();
    }
    weft_unload(self->lib);
    free(self);
    return NULL;
}

// RT 路径：零分配；事件须按 frame 升序、超出容量的尾部丢弃
WEFT_EXPORT void weft_clap_plugin_process(void* handle, float* left, float* right, int32_t frames,
                                          const weft_clap_note_t* events, int32_t count)
{
    weft_clap_instance_t* self = (weft_clap_instance_t*)handle;
    if (!self || !self->plugin || frames <= 0) {
        return;
    }
    self->event_count = 0;
    for (int32_t i = 0; i < count && i < WEFT_MAX_EVENTS; i++) {
        clap_event_note_t* note = &self->events[self->event_count++];
        memset(note, 0, sizeof *note);
        note->header.time = (uint32_t)(events[i].frame < 0 ? 0 : events[i].frame);
        note->header.space_id = CLAP_CORE_EVENT_SPACE_ID;
        note->header.type = events[i].is_on ? CLAP_EVENT_NOTE_ON : CLAP_EVENT_NOTE_OFF;
        note->header.size = (uint32_t)sizeof(clap_event_note_t);
        note->note_id = -1;
        note->port_index = -1;
        note->channel = (int16_t)events[i].channel;
        note->key = (int16_t)events[i].key;
        note->velocity = events[i].velocity;
    }
    self->out_channels[0] = left;
    self->out_channels[1] = right;
    self->process.frames_count = (uint32_t)frames;
    self->plugin->process(self->plugin, &self->process);
    self->steady_time += frames;
    self->process.steady_time = self->steady_time;
}

WEFT_EXPORT void weft_clap_plugin_destroy(void* handle)
{
    weft_clap_instance_t* self = (weft_clap_instance_t*)handle;
    if (!self) {
        return;
    }
    if (self->plugin) {
        if (self->plugin->stop_processing) {
            self->plugin->stop_processing(self->plugin);
        }
        if (self->plugin->deactivate) {
            self->plugin->deactivate(self->plugin);
        }
        self->plugin->destroy(self->plugin);
    }
    if (self->entry && self->entry->deinit) {
        self->entry->deinit();
    }
    weft_unload(self->lib);
    free(self);
}
