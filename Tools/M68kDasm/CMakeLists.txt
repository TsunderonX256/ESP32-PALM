cmake_minimum_required(VERSION 3.20)
project(M68kDasm C)

add_executable(m68kdasm_tool
    main.c
    ../../Musashi-master/m68kdasm.c
)

target_include_directories(m68kdasm_tool PRIVATE
    ../../Musashi-master
)

target_compile_definitions(m68kdasm_tool PRIVATE
    M68K_DASM_ENABLE=OPT_ON
)

if (MSVC)
    target_compile_options(m68kdasm_tool PRIVATE /O2 /W3 /wd4244 /wd4267 /wd4996)
endif()
