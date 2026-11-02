cmake_minimum_required(VERSION 3.16)
project(simcad_occt CXX)

# Envoltorio nativo de OpenCASCADE para Simulador.Cad3D.
# Configurar con -DOpenCASCADE_DIR=<carpeta con OpenCASCADEConfig.cmake> si CMake no lo encuentra solo.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
if(MSVC)
  add_compile_definitions(_USE_MATH_DEFINES)
endif()

find_package(OpenCASCADE REQUIRED)

add_library(simcad_occt SHARED simcad_occt.cpp simcad_occt.h)
target_include_directories(simcad_occt PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(simcad_occt PRIVATE
  TKernel TKMath TKG2d TKG3d TKGeomBase TKBRep TKGeomAlgo TKTopAlgo TKPrim
  TKBO TKBool TKFillet TKOffset TKShHealing TKMesh
  TKXSBase TKSTEP TKSTEPBase TKSTEPAttr TKSTEP209)

install(TARGETS simcad_occt RUNTIME DESTINATION . LIBRARY DESTINATION .)

# En Windows copia también las DLL de OpenCASCADE (y sus dependencias) junto al envoltorio.
if(WIN32)
  install(CODE [[
    file(GET_RUNTIME_DEPENDENCIES
      LIBRARIES $<TARGET_FILE:simcad_occt>
      RESOLVED_DEPENDENCIES_VAR deps
      UNRESOLVED_DEPENDENCIES_VAR unresolved
      PRE_EXCLUDE_REGEXES "api-ms-.*" "ext-ms-.*"
      POST_EXCLUDE_REGEXES ".*[/\\\\][Ww][Ii][Nn][Dd][Oo][Ww][Ss][/\\\\].*"
      DIRECTORIES $ENV{OCCT_BIN_DIRS})
    foreach(dep ${deps})
      file(INSTALL ${dep} DESTINATION ${CMAKE_INSTALL_PREFIX})
    endforeach()
    if(unresolved)
      message(STATUS "Dependencias sin resolver (normalmente del sistema): ${unresolved}")
    endif()
  ]])
endif()
