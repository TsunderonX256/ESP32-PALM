cmake_minimum_required(VERSION 3.20)
project(PalmMusashi C)

# Keep this native profile in sync with PalmDesktopHarness/PalmConfig.vb.
# The VB harness chooses the ROM/state filename while this DLL chooses the
# hardware map; mismatches can produce confusing boot or persistence bugs.
set(PALM_PROFILE "IIIC_EXPERIMENTAL" CACHE STRING "Palm hardware profile: IIIX, M100_EXPERIMENTAL, or IIIC_EXPERIMENTAL")
set_property(CACHE PALM_PROFILE PROPERTY STRINGS IIIX M100_EXPERIMENTAL IIIC_EXPERIMENTAL)

add_library(PalmMusashi SHARED
    palm_musashi_bridge.c
    palm_core.h
    ../Musashi-master/m68kcpu.c
    ../Musashi-master/m68kops.c
    ../Musashi-master/softfloat/softfloat.c
)

target_include_directories(PalmMusashi PRIVATE
    ../Musashi-master
)

target_compile_definitions(PalmMusashi PRIVATE
    PALM_MUSASHI_BUILD
)

if (PALM_PROFILE STREQUAL "M100_EXPERIMENTAL")
    target_compile_definitions(PalmMusashi PRIVATE PALM_HARDWARE_PROFILE=PALM_PROFILE_M100_EXPERIMENTAL)
elseif (PALM_PROFILE STREQUAL "IIIC_EXPERIMENTAL")
    target_compile_definitions(PalmMusashi PRIVATE PALM_HARDWARE_PROFILE=PALM_PROFILE_IIIC_EXPERIMENTAL)
elseif (PALM_PROFILE STREQUAL "IIIX")
    target_compile_definitions(PalmMusashi PRIVATE PALM_HARDWARE_PROFILE=PALM_PROFILE_IIIX)
else()
    message(FATAL_ERROR "Unsupported PALM_PROFILE: ${PALM_PROFILE}")
endif()

if (MSVC)
    target_compile_options(PalmMusashi PRIVATE /O2 /W3 /wd4244 /wd4267 /wd4996)
endif()
