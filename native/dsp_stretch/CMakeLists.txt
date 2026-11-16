cmake_minimum_required(VERSION 3.16)
project(weftone_dsp_stretch LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(weftone_dsp_stretch SHARED weft_stretch.cpp)

target_include_directories(weftone_dsp_stretch PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../vendor/signalsmith
    ${CMAKE_CURRENT_SOURCE_DIR}/../vendor)

if(MSVC)
    target_compile_options(weftone_dsp_stretch PRIVATE /W4)
else()
    target_compile_options(weftone_dsp_stretch PRIVATE -Wall -Wextra)
endif()

set_target_properties(weftone_dsp_stretch PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}
    ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR})
foreach(_cfg DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(weftone_dsp_stretch PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        RUNTIME_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR}
        ARCHIVE_OUTPUT_DIRECTORY_${_cfg} ${CMAKE_BINARY_DIR})
endforeach()
