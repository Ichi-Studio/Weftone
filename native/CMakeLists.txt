cmake_minimum_required(VERSION 3.16)
project(weftone_native LANGUAGES C)

add_subdirectory(weftone_miniaudio)
add_subdirectory(weftone_codecs)
add_subdirectory(clap_bridge)
add_subdirectory(test_plugins)
add_subdirectory(dsp_stretch)
