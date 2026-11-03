# simcad_occt

Envoltorio C de [OpenCASCADE](https://dev.opencascade.org/) para el módulo Diseño 3D. Expone una API plana
(`simcad_occt.h`) que `Simulador.Cad3D.Geometry/Occt/OcctKernel.cs` llama por P/Invoke.

Si la biblioteca no está junto al ejecutable, la aplicación usa el motor de mallas (`MeshKernel`) y desactiva
redondeos, chaflanes, vaciados, revoluciones y STEP.

## Compilar

La integración continua lo hace sola en Windows (ver `.github/workflows/build.yml`). A mano:

**Windows** (OpenCASCADE de conda-forge):

```powershell
micromamba create -n occt -c conda-forge "occt=7.8.*=novtk*"
$prefix = "$env:MAMBA_ROOT_PREFIX/envs/occt/Library"
$env:OCCT_BIN_DIRS = "$prefix/bin"
cmake -S native/simcad_occt -B native/build -A x64 "-DOpenCASCADE_DIR=$prefix/lib/cmake/opencascade"
cmake --build native/build --config Release
cmake --install native/build --config Release --prefix native/bin
```

**Linux** (Ubuntu 24.04):

```bash
sudo apt install libocct-modeling-algorithms-dev libocct-data-exchange-dev libtbb-dev
cmake -S native/simcad_occt -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build && cmake --install native/build --prefix native/bin
```

Al compilar la solución, todo lo que hay en `native/bin` se copia junto a la aplicación y las pruebas.
En Windows la instalación copia también las DLL de OpenCASCADE que necesita el envoltorio.
