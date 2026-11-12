cmake_minimum_required(VERSION 3.16)
project(weftone_miniaudio LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

add_library(weftone_miniaudio SHARED weft_audio.c)

target_include_directories(weftone_miniaudio PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../vendor/miniaudio)

if(MSVC)
    target_compile_options(weftone_miniaudio PRIVATE /W4)
else()
    target_compile_options(weftone_miniaudio PRIVATE -Wall -Wextra)
endif()

# 输出统一放 build 根，便于 C# NativeLibrary 探测；多配置生成器逐配置归一
set_target_properties(weftone_miniaudio PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR})
foreach(_cfg DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(weftone_miniaudio PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        RUNTIME_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        ARCHIVE_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR})
endforeach()
