namespace Simulador.Cad3D.Geometry;

/// <summary>Sólido opaco de un motor geométrico concreto.</summary>
public interface ISolid { }

/// <summary>
/// Motor de modelado de sólidos. Todas las medidas en milímetros.
/// Las primitivas se crean en su posición local (ver cada método) y luego se colocan con <see cref="Transform"/>.
/// </summary>
public interface IGeometryKernel
{
    string Name { get; }

    /// <summary>true si trabaja con geometría exacta (B-rep), p. ej. OpenCASCADE; false si trabaja con mallas.</summary>
    bool SupportsExactGeometry { get; }

    /// <summary>Caja con una esquina en el origen y la opuesta en <paramref name="size"/>.</summary>
    ISolid Box(Vec3 size);

    /// <summary>Cilindro o cono con la base centrada en el origen, eje +Z.</summary>
    ISolid Cylinder(double radiusBottom, double radiusTop, double height, int segments);

    /// <summary>Esfera centrada en el origen.</summary>
    ISolid Sphere(double radius, int segments);

    /// <summary>Extrusión de un perfil del plano XY desde z = 0 hasta z = <paramref name="height"/>.</summary>
    ISolid Extrude(IReadOnlyList<Vec2> profile, double height);

    /// <summary>
    /// Extrusión de una región con huecos (p. ej. una letra «o»): contorno exterior menos los contornos interiores.
    /// Por defecto resta cada hueco (algo más alto, para no restar caras coplanarias); el motor de mallas lo construye directamente.
    /// </summary>
    ISolid ExtrudeRegion(IReadOnlyList<Vec2> outer, IReadOnlyList<IReadOnlyList<Vec2>> holes, double height)
    {
        var result = Extrude(outer, height);
        foreach (var h in holes)
            result = Subtract(result, Transform(Extrude(h, height + 0.2), Transform3.Translate(new Vec3(0, 0, -0.1))));
        return result;
    }

    ISolid Transform(ISolid solid, Transform3 transform);
    ISolid Union(ISolid a, ISolid b);
    ISolid Subtract(ISolid a, ISolid b);
    ISolid Intersect(ISolid a, ISolid b);

    /// <summary>Malla cerrada lista para visualizar o exportar a STL/3MF.</summary>
    Mesh ToMesh(ISolid solid);
}
