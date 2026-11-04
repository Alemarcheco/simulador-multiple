#include "simcad_occt.h"

#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_GTransform.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <ShapeFix_Solid.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepFilletAPI_MakeChamfer.hxx>
#include <BRepFilletAPI_MakeFillet.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepOffsetAPI_MakeThickSolid.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <GProp_GProps.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <Interface_Static.hxx>
#include <Poly_Triangulation.hxx>
#include <STEPControl_Reader.hxx>
#include <STEPControl_Writer.hxx>
#include <Standard_Failure.hxx>
#include <Standard_Version.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <BRep_Builder.hxx>
#include <ShapeFix_Shape.hxx>
#include <gp_Ax1.hxx>
#include <gp_Ax2.hxx>
#include <gp_GTrsf.hxx>
#include <gp_Trsf.hxx>

#include <cmath>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace
{
thread_local std::string g_error;

struct Shape
{
    TopoDS_Shape shape;
};

sc_shape wrap(const TopoDS_Shape& s)
{
    if (s.IsNull())
    {
        g_error = "OpenCASCADE devolvió una forma vacía";
        return nullptr;
    }
    return new Shape{s};
}

const TopoDS_Shape& get(sc_shape s) { return static_cast<Shape*>(s)->shape; }

template <typename F>
auto guard(F&& f) -> decltype(f())
{
    g_error.clear();
    try
    {
        return f();
    }
    catch (const Standard_Failure& e)
    {
        g_error = std::string("OpenCASCADE: ") + (e.GetMessageString() ? e.GetMessageString() : e.DynamicType()->Name());
    }
    catch (const std::exception& e)
    {
        g_error = e.what();
    }
    catch (...)
    {
        g_error = "error desconocido en OpenCASCADE";
    }
    return decltype(f())();
}

template <typename Algo>
sc_shape finish(Algo& algo, const char* what)
{
    algo.Build();
    if (!algo.IsDone())
    {
        g_error = std::string(what) + " no se pudo calcular";
        return nullptr;
    }
    return wrap(algo.Shape());
}

TopoDS_Face polygon_face(const double* pts, int n, bool xz)
{
    BRepBuilderAPI_MakePolygon poly;
    for (int i = 0; i < n; i++)
    {
        double a = pts[2 * i], b = pts[2 * i + 1];
        poly.Add(xz ? gp_Pnt(a, 0, b) : gp_Pnt(a, b, 0));
    }
    poly.Close();
    if (!poly.IsDone()) throw Standard_Failure("perfil no válido");
    BRepBuilderAPI_MakeFace face(poly.Wire(), Standard_True);
    if (!face.IsDone()) throw Standard_Failure("el perfil no forma una cara plana");
    return face.Face();
}

// Repara pequeñas holguras que dejan los redondeos (caras que no comparten exactamente la arista).
TopoDS_Shape heal(const TopoDS_Shape& s)
{
    if (BRepCheck_Analyzer(s).IsValid()) return s;
    BRepBuilderAPI_Sewing sew(1e-4);
    sew.Add(s);
    sew.Perform();
    TopoDS_Shape sewn = sew.SewedShape();
    if (sewn.IsNull()) return s;
    // El cosido devuelve cáscaras: se vuelven a convertir en sólido para poder seguir haciendo booleanas.
    BRepBuilderAPI_MakeSolid solid;
    int shells = 0;
    for (TopExp_Explorer ex(sewn, TopAbs_SHELL); ex.More(); ex.Next(), shells++) solid.Add(TopoDS::Shell(ex.Current()));
    if (shells == 0 || !solid.IsDone()) return s;
    ShapeFix_Solid fix(solid.Solid());
    fix.Perform();
    TopoDS_Shape result = fix.Solid();
    return BRepCheck_Analyzer(result).IsValid() ? result : s;
}

// Selecciona aristas según el filtro.
std::vector<TopoDS_Edge> select_edges(const TopoDS_Shape& s, int filter)
{
    Bnd_Box box;
    BRepBndLib::Add(s, box);
    double xmin, ymin, zmin, xmax, ymax, zmax;
    box.Get(xmin, ymin, zmin, xmax, ymax, zmax);
    const double tol = 1e-4 * std::max(1.0, zmax - zmin);

    TopTools_IndexedMapOfShape edges;
    TopExp::MapShapes(s, TopAbs_EDGE, edges);
    std::vector<TopoDS_Edge> out;
    for (int i = 1; i <= edges.Extent(); i++)
    {
        TopoDS_Edge e = TopoDS::Edge(edges(i));
        if (BRep_Tool::Degenerated(e)) continue;
        BRepAdaptor_Curve c(e);
        gp_Pnt p0 = c.Value(c.FirstParameter()), p1 = c.Value(c.LastParameter()), pm = c.Value((c.FirstParameter() + c.LastParameter()) / 2);
        bool keep = false;
        switch (filter)
        {
        case SC_EDGES_VERTICAL:
            keep = c.GetType() == GeomAbs_Line && std::abs(p0.X() - p1.X()) < tol && std::abs(p0.Y() - p1.Y()) < tol && std::abs(p0.Z() - p1.Z()) > tol;
            break;
        case SC_EDGES_TOP:
            keep = std::abs(p0.Z() - zmax) < tol && std::abs(p1.Z() - zmax) < tol && std::abs(pm.Z() - zmax) < tol;
            break;
        case SC_EDGES_BOTTOM:
            keep = std::abs(p0.Z() - zmin) < tol && std::abs(p1.Z() - zmin) < tol && std::abs(pm.Z() - zmin) < tol;
            break;
        default:
            keep = true;
        }
        if (keep) out.push_back(e);
    }
    return out;
}
} // namespace

SC_API int sc_version(void) { return OCC_VERSION_HEX; }
SC_API const char* sc_last_error(void) { return g_error.c_str(); }
SC_API void sc_release(sc_shape s) { delete static_cast<Shape*>(s); }
SC_API void sc_free(void* p) { std::free(p); }

SC_API sc_shape sc_box(double x, double y, double z)
{
    return guard([&] { return wrap(BRepPrimAPI_MakeBox(x, y, z).Shape()); });
}

SC_API sc_shape sc_cylinder(double r1, double r2, double h)
{
    return guard([&]() -> sc_shape {
        if (std::abs(r1 - r2) < 1e-9) return wrap(BRepPrimAPI_MakeCylinder(r1, h).Shape());
        return wrap(BRepPrimAPI_MakeCone(r1, r2, h).Shape());
    });
}

SC_API sc_shape sc_sphere(double r)
{
    return guard([&] { return wrap(BRepPrimAPI_MakeSphere(r).Shape()); });
}

SC_API sc_shape sc_extrude(const double* xy, int n, double h)
{
    return guard([&]() -> sc_shape {
        if (n < 3) { g_error = "el perfil necesita al menos 3 puntos"; return nullptr; }
        return wrap(BRepPrimAPI_MakePrism(polygon_face(xy, n, false), gp_Vec(0, 0, h)).Shape());
    });
}

SC_API sc_shape sc_revolve(const double* xz, int n, double angle_deg)
{
    return guard([&]() -> sc_shape {
        if (n < 3) { g_error = "el perfil necesita al menos 3 puntos"; return nullptr; }
        gp_Ax1 axis(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1));
        double a = angle_deg >= 360 ? 2 * M_PI : angle_deg * M_PI / 180.0;
        return wrap(BRepPrimAPI_MakeRevol(polygon_face(xz, n, true), axis, a).Shape());
    });
}

SC_API sc_shape sc_transform(sc_shape s, const double* m)
{
    return guard([&]() -> sc_shape {
        // ¿Es una transformación rígida (rotación + traslación)? Entonces no deforma la geometría.
        double c0 = m[0] * m[0] + m[4] * m[4] + m[8] * m[8];
        double c1 = m[1] * m[1] + m[5] * m[5] + m[9] * m[9];
        double c2 = m[2] * m[2] + m[6] * m[6] + m[10] * m[10];
        double d01 = m[0] * m[1] + m[4] * m[5] + m[8] * m[9];
        double d02 = m[0] * m[2] + m[4] * m[6] + m[8] * m[10];
        double d12 = m[1] * m[2] + m[5] * m[6] + m[9] * m[10];
        double det = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
        bool rigid = std::abs(c0 - 1) < 1e-9 && std::abs(c1 - 1) < 1e-9 && std::abs(c2 - 1) < 1e-9 &&
                     std::abs(d01) < 1e-9 && std::abs(d02) < 1e-9 && std::abs(d12) < 1e-9 && det > 0;
        if (rigid)
        {
            gp_Trsf t;
            t.SetValues(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11]);
            return wrap(BRepBuilderAPI_Transform(get(s), t, Standard_True).Shape());
        }
        gp_GTrsf g;
        g.SetValue(1, 1, m[0]); g.SetValue(1, 2, m[1]); g.SetValue(1, 3, m[2]); g.SetValue(1, 4, m[3]);
        g.SetValue(2, 1, m[4]); g.SetValue(2, 2, m[5]); g.SetValue(2, 3, m[6]); g.SetValue(2, 4, m[7]);
        g.SetValue(3, 1, m[8]); g.SetValue(3, 2, m[9]); g.SetValue(3, 3, m[10]); g.SetValue(3, 4, m[11]);
        BRepBuilderAPI_GTransform gt(get(s), g, Standard_True);
        if (!gt.IsDone()) { g_error = "transformación no válida"; return nullptr; }
        TopoDS_Shape r = gt.Shape();
        return wrap(r);
    });
}

SC_API sc_shape sc_union(sc_shape a, sc_shape b)
{
    return guard([&] { BRepAlgoAPI_Fuse op(get(a), get(b)); return finish(op, "La unión"); });
}

SC_API sc_shape sc_cut(sc_shape a, sc_shape b)
{
    return guard([&] { BRepAlgoAPI_Cut op(get(a), get(b)); return finish(op, "La resta"); });
}

SC_API sc_shape sc_common(sc_shape a, sc_shape b)
{
    return guard([&] { BRepAlgoAPI_Common op(get(a), get(b)); return finish(op, "La intersección"); });
}

SC_API sc_shape sc_fillet(sc_shape s, double radius, int filter)
{
    return guard([&]() -> sc_shape {
        auto edges = select_edges(get(s), filter);
        if (edges.empty()) { g_error = "no hay aristas que redondear"; return nullptr; }
        BRepFilletAPI_MakeFillet f(get(s));
        for (auto& e : edges) f.Add(radius, e);
        f.Build();
        if (!f.IsDone()) { g_error = "El redondeo no se pudo calcular (prueba con un radio menor)"; return nullptr; }
        return wrap(heal(f.Shape()));
    });
}

SC_API sc_shape sc_chamfer(sc_shape s, double distance, int filter)
{
    return guard([&]() -> sc_shape {
        auto edges = select_edges(get(s), filter);
        if (edges.empty()) { g_error = "no hay aristas que achaflanar"; return nullptr; }
        BRepFilletAPI_MakeChamfer c(get(s));
        for (auto& e : edges) c.Add(distance, e);
        c.Build();
        if (!c.IsDone()) { g_error = "El chaflán no se pudo calcular (prueba con una distancia menor)"; return nullptr; }
        return wrap(heal(c.Shape()));
    });
}

SC_API sc_shape sc_shell_open_top(sc_shape s, double thickness)
{
    return guard([&]() -> sc_shape {
        const TopoDS_Shape& shape = get(s);
        Bnd_Box box;
        BRepBndLib::Add(shape, box);
        double xmin, ymin, zmin, xmax, ymax, zmax;
        box.Get(xmin, ymin, zmin, xmax, ymax, zmax);
        TopTools_ListOfShape remove;
        for (TopExp_Explorer ex(shape, TopAbs_FACE); ex.More(); ex.Next())
        {
            TopoDS_Face f = TopoDS::Face(ex.Current());
            BRepAdaptor_Surface surf(f);
            if (surf.GetType() != GeomAbs_Plane) continue;
            Bnd_Box fb;
            BRepBndLib::Add(f, fb);
            double a, b, c, d, e, zf;
            fb.Get(a, b, c, d, e, zf);
            if (std::abs(c - zmax) < 1e-3 * std::max(1.0, zmax - zmin) && std::abs(zf - zmax) < 1e-3 * std::max(1.0, zmax - zmin))
                remove.Append(f);
        }
        if (remove.IsEmpty()) { g_error = "el sólido no tiene una cara plana arriba"; return nullptr; }
        BRepOffsetAPI_MakeThickSolid thick;
        thick.MakeThickSolidByJoin(shape, remove, -thickness, 1e-3);
        return finish(thick, "El vaciado");
    });
}

SC_API double sc_volume(sc_shape s)
{
    return guard([&] {
        GProp_GProps props;
        BRepGProp::VolumeProperties(get(s), props);
        return props.Mass();
    });
}

SC_API int sc_bounds(sc_shape s, double* out)
{
    return guard([&] {
        Bnd_Box box;
        BRepBndLib::Add(get(s), box);
        if (box.IsVoid()) return 0;
        box.Get(out[0], out[1], out[2], out[3], out[4], out[5]);
        return 1;
    });
}

SC_API int sc_mesh(sc_shape s, double lin, double ang, double** verts, int* n_verts, int** tris, int* n_tris)
{
    return guard([&]() -> int {
        const TopoDS_Shape& shape = get(s);
        BRepMesh_IncrementalMesh mesher(shape, lin, Standard_False, ang, Standard_True);
        std::vector<double> v;
        std::vector<int> t;
        for (TopExp_Explorer ex(shape, TopAbs_FACE); ex.More(); ex.Next())
        {
            TopoDS_Face face = TopoDS::Face(ex.Current());
            TopLoc_Location loc;
            Handle(Poly_Triangulation) tri = BRep_Tool::Triangulation(face, loc);
            if (tri.IsNull()) continue;
            const gp_Trsf& tr = loc.Transformation();
            int base = static_cast<int>(v.size() / 3);
            for (int i = 1; i <= tri->NbNodes(); i++)
            {
                gp_Pnt p = tri->Node(i).Transformed(tr);
                v.push_back(p.X()); v.push_back(p.Y()); v.push_back(p.Z());
            }
            bool reversed = face.Orientation() == TopAbs_REVERSED;
            for (int i = 1; i <= tri->NbTriangles(); i++)
            {
                int a, b, c;
                tri->Triangle(i).Get(a, b, c);
                if (reversed) std::swap(b, c);
                t.push_back(base + a - 1); t.push_back(base + b - 1); t.push_back(base + c - 1);
            }
        }
        *n_verts = static_cast<int>(v.size() / 3);
        *n_tris = static_cast<int>(t.size() / 3);
        *verts = static_cast<double*>(std::malloc(std::max<size_t>(1, v.size()) * sizeof(double)));
        *tris = static_cast<int*>(std::malloc(std::max<size_t>(1, t.size()) * sizeof(int)));
        if (!v.empty()) std::memcpy(*verts, v.data(), v.size() * sizeof(double));
        if (!t.empty()) std::memcpy(*tris, t.data(), t.size() * sizeof(int));
        return 1;
    });
}

SC_API int sc_write_step(const sc_shape* shapes, const char* const* names, int n, const char* path)
{
    return guard([&]() -> int {
        STEPControl_Writer writer;
        Interface_Static::SetCVal("write.step.unit", "MM");
        Interface_Static::SetCVal("write.step.product.name", names && n == 1 && names[0] ? names[0] : "Simulador multiple");
        for (int i = 0; i < n; i++)
        {
            if (writer.Transfer(get(shapes[i]), STEPControl_AsIs) != IFSelect_RetDone)
            {
                g_error = "no se pudo convertir la pieza a STEP";
                return 0;
            }
        }
        if (writer.Write(path) != IFSelect_RetDone) { g_error = "no se pudo escribir el archivo STEP"; return 0; }
        return 1;
    });
}

SC_API sc_shape sc_read_step(const char* path)
{
    return guard([&]() -> sc_shape {
        STEPControl_Reader reader;
        if (reader.ReadFile(path) != IFSelect_RetDone) { g_error = "no se pudo leer el archivo STEP"; return nullptr; }
        reader.TransferRoots();
        TopoDS_Shape s = reader.OneShape();
        ShapeFix_Shape fix(s);
        fix.Perform();
        return wrap(fix.Shape());
    });
}
