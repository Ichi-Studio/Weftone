// weft_clap.c — CLAP 宿主桥（SPEC M3 D2=a）：安全扫描。
// 单一职责：加载 .clap → clap_entry → plugin factory → descriptor 拷贝出界；
// 不实例化插件（AGENTS.md §11：扫描期最小暴露）。崩溃隔离由上层独立进程保证。
#include <stdint.h>
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

// 与 C# StructLayout(ByValTStr) 逐字段对齐（容量均为 2 的幂，无尾部填充差异）
typedef struct {
    char id[256];
    char name[256];
    char vendor[256];
    char version[64];
    char description[512];
    char features[256]; // ';' 连接
} weft_clap_plugin_info;

static void weft_copy_str(char* dst, size_t cap, const char* src)
{
    if (!src) {
        dst[0] = '\0';
        return;
    }
    size_t n = strlen(src);
    if (n >= cap) {
        n = cap - 1;
    }
    memcpy(dst, src, n);
    dst[n] = '\0';
}

// 返回插件数（<= max）；加载/解析失败返回 -1。线程不安全：单进程单次扫描。
WEFT_EXPORT int32_t weft_clap_scan(const char* path, weft_clap_plugin_info* out, int32_t max)
{
    if (!path || !out || max <= 0) {
        return -1;
    }
    weft_lib_t lib = weft_load(path);
    if (!lib) {
        return -1;
    }
    int32_t result = -1;
    int inited = 0;

    const clap_plugin_entry_t* entry = (const clap_plugin_entry_t*)weft_sym(lib, "clap_entry");
    if (!entry || !entry->init || !entry->get_factory
        || entry->clap_version.major != 1) {
        goto done;
    }
    if (!entry->init || !entry->get_factory || !entry->init("io.github.ichi-studio.weftone")) {
        goto done;
    }
    inited = 1;

    const clap_plugin_factory_t* factory =
        (const clap_plugin_factory_t*)entry->get_factory(CLAP_PLUGIN_FACTORY_ID);
    if (!factory || !factory->get_plugin_count || !factory->get_plugin_descriptor) {
        goto done;
    }

    uint32_t count = factory->get_plugin_count(factory);
    int32_t copied = 0;
    for (uint32_t i = 0; i < count && copied < max; i++) {
        const clap_plugin_descriptor_t* desc = factory->get_plugin_descriptor(factory, i);
        if (!desc || !desc->id) {
            continue;
        }
        weft_clap_plugin_info* slot = &out[copied++];
        weft_copy_str(slot->id, sizeof slot->id, desc->id);
        weft_copy_str(slot->name, sizeof slot->name, desc->name);
        weft_copy_str(slot->vendor, sizeof slot->vendor, desc->vendor);
        weft_copy_str(slot->version, sizeof slot->version, desc->version);
        weft_copy_str(slot->description, sizeof slot->description, desc->description);
        size_t used = 0;
        if (desc->features) {
            for (const char* const* f = desc->features; *f && used + 1 < sizeof slot->features; ++f) {
                size_t n = strlen(*f);
                if (used > 0 && used + 1 < sizeof slot->features) {
                    slot->features[used++] = ';';
                }
                if (n >= sizeof slot->features - used) {
                    n = sizeof slot->features - used - 1;
                }
                memcpy(slot->features + used, *f, n);
                used += n;
            }
        }
        slot->features[used] = '\0';
    }
    result = copied;

done:
    if (inited && entry->deinit) {
        entry->deinit();
    }
    weft_unload(lib);
    return result;
}
