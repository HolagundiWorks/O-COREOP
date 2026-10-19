cmake_minimum_required(VERSION 3.20)
project(BBSStudio LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

if(MSVC)
    add_compile_options(/W3 /permissive- /utf-8 /EHsc)
    add_compile_definitions(_CRT_SECURE_NO_WARNINGS UNICODE _UNICODE
                            WINVER=0x0A00 _WIN32_WINNT=0x0A00)
endif()

set(CORE_SOURCES
    src/core/Engine.cpp
    src/core/Json.cpp
    src/core/Project.cpp
    src/core/Export.cpp
    src/core/Parse.cpp)

set(API_SOURCES
    src/api/Bridge.cpp
    src/api/bbs_c_api.cpp)

# Shared engine DLL for the WinUI C# shell (P/Invoke).
add_library(bbs_engine SHARED ${CORE_SOURCES} ${API_SOURCES})
target_compile_definitions(bbs_engine PRIVATE BBS_ENGINE_EXPORTS)
target_include_directories(bbs_engine PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/src)
set_target_properties(bbs_engine PROPERTIES
    PREFIX ""   # MinGW would otherwise name it libbbs_engine.dll; the app loads bbs_engine.dll
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}")

# Engine smoke tests (console).
add_executable(bbs_tests tests/test_engine.cpp src/core/Engine.cpp src/core/Parse.cpp)
