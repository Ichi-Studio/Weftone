cmake_minimum_required(VERSION 3.16)
project(weftone_clap_bridge LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

add_library(weftone_clap_bridge SHARED weft_clap.c)

target_include_directories(weftone_clap_bridge PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../vendor/clap/include)

if(MSVC)
    target_compile_options(weftone_clap_bridge PRIVATE /W4)
else()
    target_compile_options(weftone_clap_bridge PRIVATE -Wall -Wextra)
endif()

set_target_properties(weftone_clap_bridge PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR})
foreach(_cfg DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(weftone_clap_bridge PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        RUNTIME_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        ARCHIVE_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR})
endforeach()
