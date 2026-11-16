cmake_minimum_required(VERSION 3.16)
project(weftone_test_plugins LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 测试插件输出为 .clap（平台共享库改名）；随 CI 构建，供扫描/出声测试与黄金基线使用。
# C++ 编译：MSVC 的 C 模式不支持静态初始化中取 const 对象地址（C2099）。
function(weft_add_clap_plugin target source)
    add_library(${target} MODULE ${source})
    target_include_directories(${target} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../vendor/clap/include)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra)
    endif()
    set_target_properties(${target} PROPERTIES
        OUTPUT_NAME ${target}
        SUFFIX ".clap"
        LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
        RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR})
    foreach(_cfg DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
        set_target_properties(${target} PROPERTIES
            LIBRARY_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
            RUNTIME_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR})
    endforeach()
endfunction()

weft_add_clap_plugin(weftone_test_sine weftone_test_sine.cpp)
weft_add_clap_plugin(weftone_crash weftone_crash.cpp)
