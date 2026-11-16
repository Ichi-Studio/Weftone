// weftone_crash.cpp — 故意崩溃的假 .clap（SPEC M3 竖切 1：崩溃隔离测试）。
// entry->init 即 abort()：宿主扫描进程若不隔离将被带走。
#include <cstdlib>

#include "clap/clap.h"

static bool crash_init(const char* process_id)
{
    (void)process_id;
    abort();
}

static void crash_deinit() {}

static const void* crash_factory(const char* factory_id)
{
    (void)factory_id;
    return nullptr;
}

extern "C" CLAP_EXPORT const clap_plugin_entry_t clap_entry = {
    CLAP_VERSION,
    crash_init,
    crash_deinit,
    crash_factory,
};
