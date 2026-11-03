// Envoltorio C de OpenCASCADE para el módulo Diseño 3D (Simulador múltiple).
// API plana para P/Invoke desde C#: los sólidos son punteros opacos que se liberan con sc_release.
// Todas las medidas en mm. Las funciones que fallan devuelven NULL (o 0) y dejan el motivo en sc_last_error().
#pragma once

#ifdef _WIN32
#define SC_API extern "C" __declspec(dllexport)
#else
#define SC_API extern "C" __attribute__((visibility("default")))
#endif

typedef void* sc_shape;

// Filtros de aristas para redondeos y chaflanes.
enum sc_edge_filter
{
    SC_EDGES_ALL = 0,       // todas las aristas
    SC_EDGES_VERTICAL = 1,  // aristas rectas paralelas a Z
    SC_EDGES_TOP = 2,       // aristas a la altura máxima del sólido
    SC_EDGES_BOTTOM = 3,    // aristas a la altura mínima del sólido
};

SC_API int sc_version(void);
SC_API const char* sc_last_error(void);
SC_API void sc_release(sc_shape s);

SC_API sc_shape sc_box(double x, double y, double z);
SC_API sc_shape sc_cylinder(double radius_bottom, double radius_top, double height);
SC_API sc_shape sc_sphere(double radius);
// Perfil cerrado en XY: xy = [x0, y0, x1, y1, ...], n puntos.
SC_API sc_shape sc_extrude(const double* xy, int n, double height);
// Revolución de un perfil del plano XZ (x = radio) alrededor del eje Z. angle_deg en (0, 360].
SC_API sc_shape sc_revolve(const double* xz, int n, double angle_deg);

// m = matriz 3x4 fila mayor: [m00 m01 m02 tx  m10 m11 m12 ty  m20 m21 m22 tz].
SC_API sc_shape sc_transform(sc_shape s, const double* m);
SC_API sc_shape sc_union(sc_shape a, sc_shape b);
SC_API sc_shape sc_cut(sc_shape a, sc_shape b);
SC_API sc_shape sc_common(sc_shape a, sc_shape b);

SC_API sc_shape sc_fillet(sc_shape s, double radius, int edge_filter);
SC_API sc_shape sc_chamfer(sc_shape s, double distance, int edge_filter);
// Vaciado: quita la cara superior y deja paredes de grosor thickness hacia dentro.
SC_API sc_shape sc_shell_open_top(sc_shape s, double thickness);

SC_API double sc_volume(sc_shape s);
// Bounds: out[6] = xmin, ymin, zmin, xmax, ymax, zmax.
SC_API int sc_bounds(sc_shape s, double* out);

// Teselado. Devuelve 1 si va bien; los buffers se liberan con sc_free.
// verts = [x, y, z, ...] (n_verts puntos), tris = [i0, i1, i2, ...] (n_tris triángulos), orientados hacia fuera.
SC_API int sc_mesh(sc_shape s, double linear_deflection, double angular_deflection_rad,
                   double** verts, int* n_verts, int** tris, int* n_tris);
SC_API void sc_free(void* p);

// STEP (AP214). names puede ser NULL.
SC_API int sc_write_step(const sc_shape* shapes, const char* const* names, int n, const char* path_utf8);
SC_API sc_shape sc_read_step(const char* path_utf8);
