using System.Numerics;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Import.Step;

/// <summary>
/// Convierte un archivo STEP (AP203/AP214/AP242, el formato 3D que publican los fabricantes) en una malla de
/// triángulos con colores, en milímetros. Lee sólidos B-rep (planos, cilindros, conos, esferas, toros, superficies de
/// revolución y extrusión, B-spline), sólidos facetados, conjuntos con sus posiciones y los colores de estilo.
/// </summary>
public sealed class StepReader
{
    public sealed record Result(DeviceMesh Mesh, string ProductName, List<string> Notes);

    private const uint DefaultColor = 0xFFB9C0C7;

    private readonly StepFile _f;
    private readonly List<string> _notes = [];
    private readonly Dictionary<int, uint> _colors = [];
    private readonly Dictionary<(int, bool), List<V3>> _edgeCache = [];
    private readonly Dictionary<int, Curve3?> _curveCache = [];
    private readonly Dictionary<int, Surface?> _surfCache = [];
    private readonly DeviceMesh _mesh = new();
    private double _angle = 1;      // unidades de ángulo → radianes (de la representación que se está leyendo)
    private double _tol = 0.05;     // tolerancia de cuerda (unidades de la representación que se está leyendo)
    private double _tolMm = 0.15;   // tolerancia de cuerda en mm
    private double _defaultScale = 1;
    private readonly Dictionary<int, (double Scale, double Angle)> _unitCache = [];
    private int _unsupportedSurf, _faces, _failedFaces;

    private StepReader(StepFile f) => _f = f;

    public static Result Read(string path) => Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    public static Result Read(string text, string name)
    {
        var f = StepFile.Parse(text);
        var r = new StepReader(f);
        r.Run();
        var product = f.Entities.Values.FirstOrDefault(e => e.Type == "PRODUCT") is { } p && p.Args.Count > 0 ? p.Args[0] as string ?? name : name;
        if (string.IsNullOrWhiteSpace(product)) product = name;
        return new Result(r._mesh.Seal(), product, r._notes);
    }

    // ------------------------------------------------------------------ recorrido general

    private void Run()
    {
        ReadUnits();
        ReadColors();
        ReadHidden();
        _hasSolids = _f.Entities.Values.Any(e => e.Type is "MANIFOLD_SOLID_BREP" or "BREP_WITH_VOIDS" or "FACETED_BREP");
        EstimateTolerance();

        // Representaciones con geometría y relaciones entre ellas (conjuntos).
        var reps = _f.Entities.Values.Where(IsRepresentation).ToList();
        var group = new Dictionary<int, int>();
        int Find(int x) { while (group.TryGetValue(x, out var p) && p != x) x = p; return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) group[a] = b; }
        foreach (var r in reps) group[r.Id] = r.Id;

        var children = new List<(int Parent, int Child, Frame T)>();
        var pdOfRep = new Dictionary<int, int>();
        foreach (var e in _f.Entities.Values.Where(e => e.Type == "SHAPE_DEFINITION_REPRESENTATION"))
            if (_f.Get(e.Args.ElementAtOrDefault(0)) is { } pds && _f.Get(e.Args.ElementAtOrDefault(1)) is { } rep
                && _f.Get(pds.Args.ElementAtOrDefault(2)) is { } pd)
                pdOfRep[rep.Id] = pd.Id;

        // Relaciones de producto (NAUO) para saber qué lado es el hijo.
        var nauoOfPds = new Dictionary<int, (int Parent, int Child)>();
        foreach (var e in _f.Entities.Values.Where(e => e.Type == "NEXT_ASSEMBLY_USAGE_OCCURRENCE"))
            if (_f.Get(e.Args.ElementAtOrDefault(3)) is { } parent && _f.Get(e.Args.ElementAtOrDefault(4)) is { } child)
                nauoOfPds[e.Id] = (parent.Id, child.Id);
        var cdsr = new Dictionary<int, int>();   // relación de representación → NAUO
        foreach (var e in _f.Entities.Values.Where(e => e.Type == "CONTEXT_DEPENDENT_SHAPE_REPRESENTATION"))
            if (_f.Get(e.Args.ElementAtOrDefault(0)) is { } rr && _f.Get(e.Args.ElementAtOrDefault(1)) is { } pds
                && _f.Get(pds.Args.ElementAtOrDefault(2)) is { } nauo)
                cdsr[rr.Id] = nauo.Id;

        foreach (var e in _f.Entities.Values)
        {
            if (!e.Is("REPRESENTATION_RELATIONSHIP") && !e.Is("SHAPE_REPRESENTATION_RELATIONSHIP")) continue;
            var a = e.Is("REPRESENTATION_RELATIONSHIP") ? e.ArgsOf("REPRESENTATION_RELATIONSHIP") : e.Args;
            if (_f.Get(a.ElementAtOrDefault(2)) is not { } rep1 || _f.Get(a.ElementAtOrDefault(3)) is not { } rep2) continue;
            if (!group.ContainsKey(rep1.Id)) group[rep1.Id] = rep1.Id;
            if (!group.ContainsKey(rep2.Id)) group[rep2.Id] = rep2.Id;
            if (!e.Is("REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION")) { Union(rep1.Id, rep2.Id); continue; }
            var tr = _f.Get(e.ArgsOf("REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION").ElementAtOrDefault(0));
            Frame m1 = Frame.Identity, m2 = Frame.Identity;
            double s1 = UnitsOf(rep1).Scale, s2 = UnitsOf(rep2).Scale;
            if (tr is not null && tr.Type == "ITEM_DEFINED_TRANSFORMATION")
            {
                m1 = Mm(Placement(tr.Args.ElementAtOrDefault(2)), s1);
                m2 = Mm(Placement(tr.Args.ElementAtOrDefault(3)), s2);
            }
            else if (tr is not null) m2 = Mm(Placement(new StepRef(tr.Id)), s2);
            int child = rep1.Id, parent = rep2.Id;
            Frame inChild = m1, inParent = m2;
            if (cdsr.TryGetValue(e.Id, out var nauoId) && nauoOfPds.TryGetValue(nauoId, out var pc)
                && pdOfRep.TryGetValue(rep2.Id, out var pd2) && pd2 == pc.Child)
            {
                (child, parent) = (rep2.Id, rep1.Id);
                (inChild, inParent) = (m2, m1);
            }
            children.Add((parent, child, inParent.Then(inChild.Inverted())));
        }

        // Las relaciones sin transformación pueden enlazar una forma con su B-rep: agruparlas.
        var byGroup = new Dictionary<int, List<StepEntity>>();
        foreach (var id in group.Keys.ToList())
            if (_f[id] is { } ent)
            {
                var g = Find(id);
                if (!byGroup.TryGetValue(g, out var l)) byGroup[g] = l = [];
                l.Add(ent);
            }
        var edges = children.Select(c => (Parent: Find(c.Parent), Child: Find(c.Child), c.T)).Where(c => c.Parent != c.Child).ToList();
        var isChild = edges.Select(c => c.Child).ToHashSet();
        var roots = byGroup.Keys.Where(g => !isChild.Contains(g)).ToList();
        if (roots.Count == 0) roots = byGroup.Keys.ToList();
        // Algunos programas repiten cada sólido en una representación suelta (de un «shape aspect») sin la
        // colocación del conjunto: si todo lo que tiene ya está en otra representación, no se dibuja otra vez.
        var itemsOf = byGroup.ToDictionary(kv => kv.Key, kv => kv.Value.SelectMany(Items).Select(i => i.Id).ToHashSet());
        if (roots.Count > 1)
            roots = roots.Where(r =>
            {
                var mine = itemsOf[r].Where(i => _f[i] is { } e && e.Type != "AXIS2_PLACEMENT_3D").ToList();
                return mine.Count == 0 || !mine.All(i => itemsOf.Any(kv => kv.Key != r && kv.Value.Contains(i)));
            }).DefaultIfEmpty(roots[0]).ToList();

        var emitted = 0;
        void Walk(int g, Frame t, int depth)
        {
            if (depth > 32 || !byGroup.TryGetValue(g, out var list)) return;
            foreach (var rep in list)
            {
                var u = UnitsOf(rep);
                foreach (var item in Items(rep)) emitted += EmitItem(item, t, u.Scale, u.Angle, depth);
            }
            foreach (var c in edges.Where(c => c.Parent == g)) Walk(c.Child, t.Then(c.T), depth + 1);
        }
        foreach (var r in roots) Walk(r, Frame.Identity, 0);

        if (emitted == 0)
        {
            // Sin representaciones reconocibles: dibujar todos los sólidos tal cual.
            foreach (var e in _f.Entities.Values.Where(e => e.Type is "MANIFOLD_SOLID_BREP" or "BREP_WITH_VOIDS" or "FACETED_BREP" or "SHELL_BASED_SURFACE_MODEL" or "CURVE_BOUNDED_SURFACE"))
                EmitItem(e, Frame.Identity, _defaultScale, _angle, 0);
        }
        DropStrayParts();
        if (_unsupportedSurf > 0) _notes.Add($"{_unsupportedSurf} cara(s) con superficies poco comunes se aproximaron como planas.");
        if (_failedFaces > 0) _notes.Add($"{_failedFaces} de {_faces} caras no se pudieron triangular.");
    }

    private static bool IsRepresentation(StepEntity e) =>
        e.Type.EndsWith("SHAPE_REPRESENTATION", StringComparison.Ordinal) || e.Type == "SHAPE_REPRESENTATION"
        || e.Is("SHAPE_REPRESENTATION") || e.Type is "MANIFOLD_SURFACE_SHAPE_REPRESENTATION" or "REPRESENTATION";

    private IEnumerable<StepEntity> Items(StepEntity rep)
    {
        var args = rep.Parts.Count > 0 ? (rep.Parts.TryGetValue("REPRESENTATION", out var ra) ? ra : rep.Args) : rep.Args;
        if (args.ElementAtOrDefault(1) is List<object?> items)
            foreach (var i in items)
                if (_f.Get(i) is { } e) yield return e;
    }

    /// <summary>Una placement en unidades del archivo con el origen pasado a mm.</summary>
    private static Frame Mm(Frame f, double scale) => new(f.O * scale, f.X, f.Y, f.Z);

    private static Frame Scaled(Frame t, double scale) =>
        t.Then(new Frame(V3.Zero, new V3(scale, 0, 0), new V3(0, scale, 0), new V3(0, 0, scale)));

    /// <summary>Dibuja un elemento de una representación: <paramref name="tMm"/> lleva sus mm a los del conjunto.</summary>
    private int EmitItem(StepEntity item, Frame tMm, double scale, double angle, int depth)
    {
        if (_hidden.Contains(item.Id)) return 0;
        if (item.Type is "MANIFOLD_SOLID_BREP" or "FACETED_BREP" or "BREP_WITH_VOIDS" or "SHELL_BASED_SURFACE_MODEL")
        {
            var start = _mesh.Triangles.Count;
            var n = EmitItemCore(item, tMm, scale, angle, depth);
            if (_mesh.Triangles.Count > start) _parts.Add((start, _mesh.Triangles.Count));
            return n;
        }
        // Las curvas y superficies sueltas de un archivo con sólidos suelen ser construcción auxiliar.
        if (_hasSolids && item.Type is "GEOMETRIC_SET" or "GEOMETRIC_CURVE_SET" or "CURVE_BOUNDED_SURFACE") return 0;
        return EmitItemCore(item, tMm, scale, angle, depth);
    }

    private readonly HashSet<int> _hidden = [];
    private readonly List<(int Start, int End)> _parts = [];
    private bool _hasSolids;

    /// <summary>Elementos en capas marcadas como invisibles (INVISIBILITY de capas o de estilos).</summary>
    private void ReadHidden()
    {
        foreach (var inv in _f.Entities.Values.Where(e => e.Type == "INVISIBILITY"))
            if (inv.Args.ElementAtOrDefault(0) is List<object?> list)
                foreach (var r in list)
                {
                    if (_f.Get(r) is not { } x) continue;
                    if (x.Type == "PRESENTATION_LAYER_ASSIGNMENT" && x.Args.ElementAtOrDefault(2) is List<object?> items)
                        foreach (var i in items) { if (_f.Get(i) is { } e) _hidden.Add(e.Id); }
                    else if (x.Type is "STYLED_ITEM" or "OVER_RIDING_STYLED_ITEM" && _f.Get(x.Args.ElementAtOrDefault(2)) is { } e2)
                        _hidden.Add(e2.Id);
                }
    }

    /// <summary>
    /// Quita piezas diminutas separadas del resto (marcas de referencia, pines de origen…) que los fabricantes dejan
    /// en algunos modelos y que agrandan la caja del aparato.
    /// </summary>
    private void DropStrayParts()
    {
        if (_parts.Count < 2) return;
        var boxes = _parts.Select(p => Box(p.Start, p.End)).ToList();
        var all = Box(0, _mesh.Triangles.Count);
        var diag = (all.Max - all.Min).Length();
        var drop = new HashSet<int>();
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if ((b.Max - b.Min).Length() > diag * 0.04) continue;
            var touches = false;
            for (var j = 0; j < boxes.Count && !touches; j++)
                if (j != i && !drop.Contains(j) && Overlaps(b, boxes[j], 0.5f)) touches = true;
            if (!touches) drop.Add(i);
        }
        if (drop.Count == 0) return;
        var keep = _mesh.Triangles.Where((_, k) => !drop.Any(d => k >= _parts[d].Start && k < _parts[d].End)).ToList();
        _mesh.Triangles.Clear();
        _mesh.Triangles.AddRange(keep);
        _notes.Add($"Se quitaron {drop.Count} pieza(s) diminuta(s) separadas del aparato.");
    }

    private (Vector3 Min, Vector3 Max) Box(int start, int end)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (var k = start; k < end; k++)
        {
            var (a, b, c, _) = _mesh.Triangles[k];
            min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
            max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
        }
        return (min, max);
    }

    private static bool Overlaps((Vector3 Min, Vector3 Max) a, (Vector3 Min, Vector3 Max) b, float m) =>
        a.Min.X <= b.Max.X + m && a.Max.X >= b.Min.X - m && a.Min.Y <= b.Max.Y + m && a.Max.Y >= b.Min.Y - m
        && a.Min.Z <= b.Max.Z + m && a.Max.Z >= b.Min.Z - m;

    private int EmitItemCore(StepEntity item, Frame tMm, double scale, double angle, int depth)
    {
        _angle = angle;
        _tol = _tolMm / scale;
        var t = Scaled(tMm, scale);
        switch (item.Type)
        {
            case "MANIFOLD_SOLID_BREP":
            case "FACETED_BREP":
                return EmitShell(_f.Get(item.Args.ElementAtOrDefault(1)), t, Color(item.Id));
            case "BREP_WITH_VOIDS":
                return EmitShell(_f.Get(item.Args.ElementAtOrDefault(1)), t, Color(item.Id));
            case "SHELL_BASED_SURFACE_MODEL":
            {
                var n = 0;
                if (item.Args.ElementAtOrDefault(1) is List<object?> shells)
                    foreach (var s in shells) n += EmitShell(_f.Get(s), t, Color(item.Id));
                return n;
            }
            case "GEOMETRIC_SET":
            case "GEOMETRIC_CURVE_SET":
            {
                var n = 0;
                if (item.Args.ElementAtOrDefault(1) is List<object?> elems)
                    foreach (var el in elems)
                        if (_f.Get(el) is { } x && x.Type is "CURVE_BOUNDED_SURFACE" or "RECTANGULAR_TRIMMED_SURFACE")
                            n += EmitItem(x, tMm, scale, angle, depth + 1);
                return n;
            }
            case "CURVE_BOUNDED_SURFACE":
                return EmitBoundedSurface(item, t, Color(item.Id) ?? DefaultColor);
            case "MAPPED_ITEM":
            {
                if (depth > 32 || _f.Get(item.Args.ElementAtOrDefault(1)) is not { } map) return 0;
                if (_f.Get(map.Args.ElementAtOrDefault(1)) is not { } rep) return 0;
                var cu = UnitsOf(rep);
                var origin = Mm(Placement(map.Args.ElementAtOrDefault(0)), cu.Scale);
                var target = Mm(Placement(item.Args.ElementAtOrDefault(2)), scale);
                var tt = tMm.Then(target.Then(origin.Inverted()));
                var n = 0;
                foreach (var i in Items(rep)) n += EmitItem(i, tt, cu.Scale, cu.Angle, depth + 1);
                return n;
            }
            default:
                return 0;
        }
    }

    private int EmitShell(StepEntity? shell, Frame t, uint? solidColor)
    {
        if (shell is null) return 0;
        var shellColor = Color(shell.Id) ?? solidColor;
        if (shell.Type == "ORIENTED_CLOSED_SHELL") shell = _f.Get(shell.Args.ElementAtOrDefault(2));
        if (shell?.Args.ElementAtOrDefault(1) is not List<object?> faces) return 0;
        var n = 0;
        foreach (var fr in faces)
        {
            if (_f.Get(fr) is not { } face) continue;
            var color = Color(face.Id) ?? shellColor ?? DefaultColor;
            n += EmitFace(face, t, color);
        }
        return n;
    }

    // ------------------------------------------------------------------ caras

    private int EmitFace(StepEntity face, Frame t, uint color)
    {
        _faces++;
        List<(List<V3> Pts, bool Outer, bool Forward)> loops = [];
        if (face.Args.ElementAtOrDefault(1) is not List<object?> bounds) return 0;
        foreach (var b in bounds)
        {
            if (_f.Get(b) is not { } bound) continue;
            var forward = bound.Args.ElementAtOrDefault(2) is not StepEnum { Value: "F" };
            var pts = LoopPoints(_f.Get(bound.Args.ElementAtOrDefault(1)));
            if (pts.Count >= 3) loops.Add((pts, bound.Type == "FACE_OUTER_BOUND", forward));
        }
        if (loops.Count == 0) return 0;
        var surf = face.Type is "ADVANCED_FACE" or "FACE_SURFACE" ? Surf(_f.Get(face.Args.ElementAtOrDefault(2))) : null;
        var sameSense = face.Args.ElementAtOrDefault(3) is not StepEnum { Value: "F" };
        List<(V3, V3, V3)> tris;
        try
        {
            tris = surf is null or PlaneSurface ? Planar(loops) : Curved(loops, surf, sameSense) ?? Planar(loops);
        }
        catch (Exception) { tris = []; }
        if (tris.Count == 0) { _failedFaces++; return 0; }
        foreach (var (a, b, c) in tris) _mesh.Add(F(t.Apply(a)), F(t.Apply(b)), F(t.Apply(c)), color);
        return 1;
    }

    private static Vector3 F(V3 p) => new((float)p.X, (float)p.Y, (float)p.Z);

    /// <summary>Superficie recortada por curvas (CURVE_BOUNDED_SURFACE), típica de los modelos «de superficies».</summary>
    private int EmitBoundedSurface(StepEntity e, Frame t, uint color)
    {
        _faces++;
        var surf = Surf(_f.Get(e.Args.ElementAtOrDefault(1)));
        var loops = new List<(List<V3> Pts, bool Outer, bool Forward)>();
        if (e.Args.ElementAtOrDefault(2) is List<object?> bounds)
            foreach (var b in bounds)
            {
                if (_f.Get(b) is not { } bc || bc.Args.ElementAtOrDefault(1) is not List<object?> segs) continue;
                var pts = new List<V3>();
                foreach (var sr in segs)
                {
                    if (_f.Get(sr) is not { } seg) continue;
                    var forward = seg.Args.ElementAtOrDefault(1) is not StepEnum { Value: "F" };
                    var part = SegmentPoints(_f.Get(seg.Args.ElementAtOrDefault(2)), surf);
                    if (!forward) part.Reverse();
                    for (var i = pts.Count == 0 ? 0 : 1; i < part.Count; i++) pts.Add(part[i]);
                }
                pts = Dedup(pts);
                if (pts.Count >= 3) loops.Add((pts, bc.Type == "OUTER_BOUNDARY_CURVE", true));
            }
        if (loops.Count == 0) { _failedFaces++; return 0; }
        List<(V3, V3, V3)> tris;
        try { tris = surf is null or PlaneSurface ? Planar(loops) : Curved(loops, surf, true) ?? Planar(loops); }
        catch (Exception) { tris = []; }
        if (tris.Count == 0) { _failedFaces++; return 0; }
        foreach (var (a, b, c) in tris) _mesh.Add(F(t.Apply(a)), F(t.Apply(b)), F(t.Apply(c)), color);
        return 1;
    }

    /// <summary>Puntos de un tramo de contorno: curva 3D recortada o curva en el plano (u, v) de la superficie.</summary>
    private List<V3> SegmentPoints(StepEntity? c, Surface? surf)
    {
        if (c is null) return [];
        if (c.Parts.TryGetValue("SURFACE_CURVE", out var scp)) return SegmentPoints(_f.Get(scp.ElementAtOrDefault(0)), surf);
        if (c.Type is "SURFACE_CURVE" or "SEAM_CURVE" or "INTERSECTION_CURVE") return SegmentPoints(_f.Get(c.Args.ElementAtOrDefault(1)), surf);
        if (c.Type == "PCURVE")
        {
            var s = Surf(_f.Get(c.Args.ElementAtOrDefault(1))) ?? surf;
            var dr = _f.Get(c.Args.ElementAtOrDefault(2));
            var c2 = dr?.Args.ElementAtOrDefault(1) is List<object?> items && items.Count > 0 ? _f.Get(items[0]) : null;
            if (s is null || c2 is null) return [];
            var uv = SegmentPoints(c2, null);
            return uv.Select(p => s.At(p.X, p.Y)).ToList();
        }
        if (c.Type == "TRIMMED_CURVE")
        {
            var basis = CurveOf(_f.Get(c.Args.ElementAtOrDefault(1)));
            if (basis is null) return [];
            var sense = c.Args.ElementAtOrDefault(4) is not StepEnum { Value: "F" };
            double t0 = TrimParam(c.Args.ElementAtOrDefault(2), basis), t1 = TrimParam(c.Args.ElementAtOrDefault(3), basis);
            if (basis is LineCurve) return [basis.At(t0), basis.At(t1)];
            if (basis.Period > 0)
            {
                if (sense) { while (t1 <= t0 + 1e-12) t1 += basis.Period; }
                else { while (t1 >= t0 - 1e-12) t1 -= basis.Period; }
            }
            var n = basis is EllipseCurve el
                ? Math.Clamp((int)Math.Ceiling(Math.Abs(t1 - t0) / Math.Clamp(2 * Math.Acos(Math.Clamp(1 - _tol / Math.Max(Math.Max(el.R1, el.R2), _tol), -1, 1)), Math.PI / 18, Math.PI / 6)), 2, 400)
                : Math.Clamp((basis is BSplineCurve b ? b.Spans : 4) * 3, 4, 120);
            var l = new List<V3>(n + 1);
            for (var i = 0; i <= n; i++) l.Add(basis.At(t0 + (t1 - t0) * i / n));
            return l;
        }
        var cv = CurveOf(c);
        if (cv is null) return [];
        if (cv is PolylineCurve pl) return pl.Points.ToList();
        var (r0, r1) = cv.Range;
        if (cv is LineCurve) return [cv.At(0), cv.At(1)];
        var m = Math.Clamp((cv is BSplineCurve bb ? bb.Spans : 8) * 3, 8, 200);
        var res = new List<V3>(m + 1);
        for (var i = 0; i <= m; i++) res.Add(cv.At(r0 + (r1 - r0) * i / m));
        return res;
    }

    private double TrimParam(object? trims, Curve3 basis)
    {
        if (trims is not List<object?> l) return 0;
        foreach (var x in l)
            if (x is StepTyped { Type: "PARAMETER_VALUE" } pv)
                return basis is EllipseCurve ? Num(pv) * _angle : Num(pv);
        foreach (var x in l)
            if (_f.Get(x) is { Type: "CARTESIAN_POINT" }) return basis.Param(Point(x));
        return 0;
    }

    /// <summary>Cara plana (o aproximada como plana): proyectar al plano medio y triangular con agujeros.</summary>
    private static List<(V3, V3, V3)> Planar(List<(List<V3> Pts, bool Outer, bool Forward)> loops)
    {
        var all = loops.SelectMany(l => l.Pts).ToList();
        var outerIdx = 0;
        double best = -1;
        for (var i = 0; i < loops.Count; i++)
        {
            var a = Newell(loops[i].Pts).Length;
            if (a > best) { best = a; outerIdx = i; }
        }
        var n = Newell(loops[outerIdx].Pts);
        if (n.Length < 1e-300) return [];
        var fr = Frame.From(loops[outerIdx].Pts[0], n, null);
        var ordered = new List<List<V3>> { loops[outerIdx].Pts };
        ordered.AddRange(loops.Where((_, i) => i != outerIdx).Select(l => l.Pts));
        var data = new List<double>();
        var holes = new List<int>();
        var pts3 = new List<V3>();
        for (var k = 0; k < ordered.Count; k++)
        {
            if (k > 0) holes.Add(pts3.Count);
            foreach (var p in ordered[k])
            {
                var d = p - fr.O;
                data.Add(d.Dot(fr.X)); data.Add(d.Dot(fr.Y));
                pts3.Add(p);
            }
        }
        var idx = Earcut.Triangulate(data, holes);
        var tris = new List<(V3, V3, V3)>(idx.Count / 3);
        for (var i = 0; i + 2 < idx.Count; i += 3) tris.Add((pts3[idx[i]], pts3[idx[i + 1]], pts3[idx[i + 2]]));
        return tris;
    }

    private static V3 Newell(List<V3> p)
    {
        double x = 0, y = 0, z = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            x += (a.Y - b.Y) * (a.Z + b.Z); y += (a.Z - b.Z) * (a.X + b.X); z += (a.X - b.X) * (a.Y + b.Y);
        }
        return new V3(x, y, z);
    }

    /// <summary>Cara curva: llevar los lazos al plano (u, v) de la superficie, triangular allí y refinar.</summary>
    private List<(V3, V3, V3)>? Curved(List<(List<V3> Pts, bool Outer, bool Forward)> loops, Surface s, bool sameSense)
    {
        double pu = s.PeriodU, pv = s.PeriodV;
        var uvLoops = new List<(List<(double U, double V)> Uv, List<V3> P, double DriftU, double DriftV, bool Forward)>();
        foreach (var l in loops)
        {
            var uv = new List<(double U, double V)>(l.Pts.Count);
            (double U, double V)? hint = null;
            var degenerate = new List<int>();
            for (var i = 0; i < l.Pts.Count; i++)
            {
                var q = s.Param(l.Pts[i], hint);
                if (pu > 0 && IsUDegenerate(s, q)) degenerate.Add(i);
                if (hint is { } h)
                {
                    if (pu > 0) q.U = Unwrap(q.U, h.U, pu);
                    if (pv > 0) q.V = Unwrap(q.V, h.V, pv);
                }
                uv.Add(q);
                hint = q;
            }
            // En los polos la u no está definida: tomar la del vecino.
            foreach (var i in degenerate)
            {
                var prev = uv[(i - 1 + uv.Count) % uv.Count]; var next = uv[(i + 1) % uv.Count];
                uv[i] = ((prev.U + Unwrap(next.U, prev.U, pu)) / 2, uv[i].V);
            }
            var du = pu > 0 ? Unwrap(uv[0].U, uv[^1].U, pu) - uv[0].U : 0;
            var dv = pv > 0 ? Unwrap(uv[0].V, uv[^1].V, pv) - uv[0].V : 0;
            // du = cuánto «sobra» al cerrar el lazo: la vuelta completa da ±periodo.
            var driftU = -du; var driftV = -dv;
            uvLoops.Add((uv, l.Pts, Math.Abs(driftU) > pu / 2 && pu > 0 ? Math.Sign(driftU) * pu : 0,
                         Math.Abs(driftV) > pv / 2 && pv > 0 ? Math.Sign(driftV) * pv : 0, l.Forward));
        }
        if (uvLoops.Any(l => l.DriftV != 0)) { _unsupportedSurf++; return null; }

        var winding = uvLoops.Where(l => l.DriftU != 0).ToList();
        var plain = uvLoops.Where(l => l.DriftU == 0).ToList();
        var polys = new List<(List<(double U, double V)> Uv, List<V3?> P)>();
        double uMin, uMax;
        if (winding.Count == 0)
        {
            var outer = plain.OrderByDescending(l => Math.Abs(Area(l.Uv))).First();
            polys.Add((outer.Uv, outer.P.Select(p => (V3?)p).ToList()));
            uMin = outer.Uv.Min(q => q.U); uMax = outer.Uv.Max(q => q.U);
            foreach (var h in plain.Where(l => !ReferenceEquals(l.Uv, outer.Uv))) polys.Add(Shift(h.Uv, h.P, uMin, uMax, pu));
        }
        else if (winding.Count == 2)
        {
            var a = Ascending(winding[0].Uv, winding[0].P, winding[0].DriftU, pu);
            var b = Ascending(winding[1].Uv, winding[1].P, winding[1].DriftU, pu);
            var uvs = new List<(double U, double V)>(a.Uv); var ps = new List<V3?>(a.P);
            for (var i = b.Uv.Count - 1; i >= 0; i--) { uvs.Add(b.Uv[i]); ps.Add(b.P[i]); }
            polys.Add((uvs, ps));
            uMin = 0; uMax = pu;
            foreach (var h in plain) polys.Add(Shift(h.Uv, h.P, uMin, uMax, pu));
        }
        else if (winding.Count == 1)
        {
            var w = winding[0];
            var reversed = (!w.Forward) ^ (!sameSense);
            var upper = (w.DriftU > 0) ^ reversed;
            if (s.PoleV(upper) is not { } vp) { _unsupportedSurf++; return null; }
            var a = Ascending(w.Uv, w.P, w.DriftU, pu);
            var uvs = new List<(double U, double V)>(a.Uv) { (pu, vp), (0, vp) };
            var ps = new List<V3?>(a.P) { null, null };
            polys.Add((uvs, ps));
            uMin = 0; uMax = pu;
            foreach (var h in plain) polys.Add(Shift(h.Uv, h.P, uMin, uMax, pu));
        }
        else { _unsupportedSurf++; return null; }

        // Triangular en (u, v).
        var data = new List<double>();
        var holes = new List<int>();
        var uvAll = new List<(double U, double V)>();
        var pAll = new List<V3?>();
        for (var k = 0; k < polys.Count; k++)
        {
            if (k > 0) holes.Add(uvAll.Count);
            foreach (var q in polys[k].Uv) { data.Add(q.U); data.Add(q.V); }
            uvAll.AddRange(polys[k].Uv);
            pAll.AddRange(polys[k].P);
        }
        var idx = Earcut.Triangulate(data, holes);
        if (idx.Count == 0) return null;
        var tris = new List<(V3, V3, V3)>(idx.Count);
        V3 P(int i) => pAll[i] ?? s.At(uvAll[i].U, uvAll[i].V);
        for (var i = 0; i + 2 < idx.Count; i += 3)
            Refine(s, uvAll[idx[i]], uvAll[idx[i + 1]], uvAll[idx[i + 2]], P(idx[i]), P(idx[i + 1]), P(idx[i + 2]), 0, tris);
        return tris;
    }

    private void Refine(Surface s, (double U, double V) a, (double U, double V) b, (double U, double V) c, V3 pa, V3 pb, V3 pc, int depth, List<(V3, V3, V3)> o)
    {
        if (depth >= 3) { o.Add((pa, pb, pc)); return; }
        var ab = ((a.U + b.U) / 2, (a.V + b.V) / 2);
        var bc = ((b.U + c.U) / 2, (b.V + c.V) / 2);
        var ca = ((c.U + a.U) / 2, (c.V + a.V) / 2);
        var mab = s.At(ab.Item1, ab.Item2); var mbc = s.At(bc.Item1, bc.Item2); var mca = s.At(ca.Item1, ca.Item2);
        var err = Math.Max((mab - (pa + pb) / 2).Length, Math.Max((mbc - (pb + pc) / 2).Length, (mca - (pc + pa) / 2).Length));
        if (err <= _tol) { o.Add((pa, pb, pc)); return; }
        Refine(s, a, ab, ca, pa, mab, mca, depth + 1, o);
        Refine(s, ab, b, bc, mab, pb, mbc, depth + 1, o);
        Refine(s, ca, bc, c, mca, mbc, pc, depth + 1, o);
        Refine(s, ab, bc, ca, mab, mbc, mca, depth + 1, o);
    }

    private bool IsUDegenerate(Surface s, (double U, double V) q) => (s.At(q.U, q.V) - s.At(q.U + 1, q.V)).Length < _tol * 0.01;

    private static double Unwrap(double x, double reference, double period)
    {
        if (period <= 0) return x;
        while (x - reference > period / 2) x -= period;
        while (reference - x > period / 2) x += period;
        return x;
    }

    private static double Area(List<(double U, double V)> p)
    {
        double a = 0;
        for (var i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.U * r.V - r.U * q.V; }
        return a / 2;
    }

    /// <summary>Lazo que da la vuelta en u: ordenado de u = 0 a u = periodo, con los extremos interpolados.</summary>
    private static (List<(double U, double V)> Uv, List<V3?> P) Ascending(List<(double U, double V)> uv, List<V3> p, double drift, double period)
    {
        var u = uv.ToList(); var pts = p.Select(x => (V3?)x).ToList();
        if (drift < 0) { u.Reverse(); pts.Reverse(); }
        // Desenrollar de forma que suba y empezar en el mínimo de u (módulo el periodo).
        for (var i = 1; i < u.Count; i++) u[i] = (Unwrap(u[i].U, u[i - 1].U, period), u[i].V);
        var start = 0; var best = double.MaxValue;
        for (var i = 0; i < u.Count; i++)
        {
            var m = ((u[i].U % period) + period) % period;
            if (m < best) { best = m; start = i; }
        }
        var ru = new List<(double U, double V)>(); var rp = new List<V3?>();
        for (var k = 0; k < u.Count; k++)
        {
            var i = (start + k) % u.Count;
            ru.Add(u[i]); rp.Add(pts[i]);
        }
        var off = Math.Floor(ru[0].U / period) * period;
        for (var i = 0; i < ru.Count; i++) ru[i] = (ru[i].U - off, ru[i].V);
        for (var i = 1; i < ru.Count; i++) ru[i] = (Unwrap(ru[i].U, ru[i - 1].U, period), ru[i].V);
        // Punto inicial en u = 0 y final en u = periodo (mismo v, interpolado entre el último y el primero).
        var first = ru[0]; var last = (U: ru[^1].U - period, ru[^1].V);
        var v0 = Math.Abs(first.U - last.U) < 1e-12 ? first.V : last.V + (first.V - last.V) * (0 - last.U) / (first.U - last.U);
        if (first.U > 1e-9) { ru.Insert(0, (0, v0)); rp.Insert(0, null); }
        ru.Add((period, v0)); rp.Add(null);
        return (ru, rp);
    }

    private static (List<(double U, double V)>, List<V3?>) Shift(List<(double U, double V)> uv, List<V3> p, double uMin, double uMax, double period)
    {
        var list = uv.ToList();
        if (period > 0)
        {
            var c = list.Average(q => q.U);
            var mid = (uMin + uMax) / 2;
            var k = Math.Round((mid - c) / period);
            if (k != 0) list = list.Select(q => (q.U + k * period, q.V)).ToList();
        }
        return (list, p.Select(x => (V3?)x).ToList());
    }

    // ------------------------------------------------------------------ bordes

    private List<V3> LoopPoints(StepEntity? loop)
    {
        var pts = new List<V3>();
        if (loop is null) return pts;
        if (loop.Type == "POLY_LOOP")
        {
            if (loop.Args.ElementAtOrDefault(1) is List<object?> l) foreach (var p in l) pts.Add(Point(p));
            return Dedup(pts);
        }
        if (loop.Type != "EDGE_LOOP" || loop.Args.ElementAtOrDefault(1) is not List<object?> edges) return pts;
        foreach (var oe in edges)
        {
            if (_f.Get(oe) is not { } o) continue;
            var edge = _f.Get(o.Args.ElementAtOrDefault(3));
            var forward = o.Args.ElementAtOrDefault(4) is not StepEnum { Value: "F" };
            if (edge is null) continue;
            var seg = EdgePoints(edge, forward);
            for (var i = pts.Count == 0 ? 0 : 1; i < seg.Count; i++) pts.Add(seg[i]);
        }
        return Dedup(pts);
    }

    private List<V3> Dedup(List<V3> pts)
    {
        var r = new List<V3>(pts.Count);
        var eps = _tol * 1e-3;
        foreach (var p in pts) if (r.Count == 0 || (r[^1] - p).Length > eps) r.Add(p);
        while (r.Count > 1 && (r[0] - r[^1]).Length <= eps) r.RemoveAt(r.Count - 1);
        return r;
    }

    private List<V3> EdgePoints(StepEntity edge, bool forward)
    {
        if (_edgeCache.TryGetValue((edge.Id, forward), out var cached)) return cached;
        List<V3> pts;
        if (_edgeCache.TryGetValue((edge.Id, !forward), out var other)) { pts = other.ToList(); pts.Reverse(); }
        else
        {
            pts = SampleEdge(edge);
            if (!forward) pts.Reverse();
        }
        _edgeCache[(edge.Id, forward)] = pts;
        return pts;
    }

    private List<V3> SampleEdge(StepEntity edge)
    {
        var p0 = VertexPoint(edge.Args.ElementAtOrDefault(1));
        var p1 = VertexPoint(edge.Args.ElementAtOrDefault(2));
        var sameSense = edge.Args.ElementAtOrDefault(4) is not StepEnum { Value: "F" };
        var c = CurveOf(_f.Get(edge.Args.ElementAtOrDefault(3)));
        var closed = (p0 - p1).Length < _tol * 1e-2;
        switch (c)
        {
            case null:
            case LineCurve:
                return [p0, p1];
            case PolylineCurve pl:
            {
                var l = pl.Points.ToList();
                if (l.Count >= 2 && (l[0] - p0).Length > (l[^1] - p0).Length) l.Reverse();
                if (l.Count > 0) { l[0] = p0; l[^1] = p1; }
                return l;
            }
            case EllipseCurve el:
            {
                var t0 = el.Param(p0); var t1 = el.Param(p1);
                if (sameSense) { while (t1 <= t0 + 1e-9) t1 += 2 * Math.PI; if (closed) t1 = t0 + 2 * Math.PI; }
                else { while (t1 >= t0 - 1e-9) t1 -= 2 * Math.PI; if (closed) t1 = t0 - 2 * Math.PI; }
                var r = Math.Max(el.R1, el.R2);
                var step = r > _tol ? 2 * Math.Acos(Math.Clamp(1 - _tol / r, -1, 1)) : Math.PI / 4;
                step = Math.Clamp(step, Math.PI / 18, Math.PI / 6);
                var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(t1 - t0) / step));
                var l = new List<V3>(n + 1) { p0 };
                for (var i = 1; i < n; i++) l.Add(el.At(t0 + (t1 - t0) * i / n));
                l.Add(p1);
                return l;
            }
            default:
            {
                var (r0, r1) = c.Range;
                double t0, t1;
                if (closed && c is BSplineCurve) { t0 = sameSense ? r0 : r1; t1 = sameSense ? r1 : r0; }
                else { t0 = c.Param(p0); t1 = c.Param(p1); }
                var spans = c is BSplineCurve b ? b.Spans : 4;
                var frac = Math.Abs(t1 - t0) / Math.Max(r1 - r0, 1e-300);
                var n = Math.Clamp((int)Math.Ceiling(spans * 2 * frac), 3, 80);
                // Afinar si la cuerda se aleja demasiado.
                var l = new List<V3>(n + 1) { p0 };
                for (var i = 1; i < n; i++) l.Add(c.At(t0 + (t1 - t0) * i / n));
                l.Add(p1);
                return l;
            }
        }
    }

    private V3 VertexPoint(object? v) => _f.Get(v) is { Type: "VERTEX_POINT" } vp ? Point(vp.Args.ElementAtOrDefault(1)) : V3.Zero;

    // ------------------------------------------------------------------ geometría

    private Curve3? CurveOf(StepEntity? e)
    {
        if (e is null) return null;
        if (_curveCache.TryGetValue(e.Id, out var c)) return c;
        c = BuildCurve(e);
        _curveCache[e.Id] = c;
        return c;
    }

    private Curve3? BuildCurve(StepEntity e)
    {
        switch (e.Type)
        {
            case "LINE":
            {
                var p = Point(e.Args.ElementAtOrDefault(1));
                var vec = _f.Get(e.Args.ElementAtOrDefault(2));
                var d = vec is null ? new V3(1, 0, 0) : Dir(vec.Args.ElementAtOrDefault(1)) * Num(vec.Args.ElementAtOrDefault(2), 1);
                return new LineCurve(p, d);
            }
            case "CIRCLE":
            {
                var r = Num(e.Args.ElementAtOrDefault(2));
                return new EllipseCurve(Placement(e.Args.ElementAtOrDefault(1)), r, r);
            }
            case "ELLIPSE":
                return new EllipseCurve(Placement(e.Args.ElementAtOrDefault(1)), Num(e.Args.ElementAtOrDefault(2)), Num(e.Args.ElementAtOrDefault(3)));
            case "POLYLINE":
                return e.Args.ElementAtOrDefault(1) is List<object?> l ? new PolylineCurve(l.Select(Point).ToList()) : null;
            case "SURFACE_CURVE":
            case "SEAM_CURVE":
            case "INTERSECTION_CURVE":
                return CurveOf(_f.Get(e.Args.ElementAtOrDefault(1)));
            case "TRIMMED_CURVE":
                return CurveOf(_f.Get(e.Args.ElementAtOrDefault(1)));
            case "B_SPLINE_CURVE_WITH_KNOTS":
            {
                var a = e.Args;
                return new BSplineCurve((int)Num(a[1]), Points(a[2]), null, Ints(a[6]), Nums(a[7]), a.ElementAtOrDefault(4) is StepEnum { Value: "T" });
            }
        }
        if (e.Parts.TryGetValue("SURFACE_CURVE", out var scp)) return CurveOf(_f.Get(scp.ElementAtOrDefault(0)));
        if (e.Parts.TryGetValue("B_SPLINE_CURVE", out var bs) && e.Parts.TryGetValue("B_SPLINE_CURVE_WITH_KNOTS", out var bk))
        {
            double[]? w = e.Parts.TryGetValue("RATIONAL_B_SPLINE_CURVE", out var rw) ? Nums(rw[0]).ToArray() : null;
            return new BSplineCurve((int)Num(bs[0]), Points(bs[1]), w, Ints(bk[0]), Nums(bk[1]), bs.ElementAtOrDefault(3) is StepEnum { Value: "T" });
        }
        return null;
    }

    private Surface? Surf(StepEntity? e)
    {
        if (e is null) return null;
        if (_surfCache.TryGetValue(e.Id, out var s)) return s;
        s = BuildSurface(e);
        if (s is null && e.Type != "PLANE") _unsupportedSurf++;
        _surfCache[e.Id] = s;
        return s;
    }

    private Surface? BuildSurface(StepEntity e)
    {
        var a = e.Args;
        switch (e.Type)
        {
            case "PLANE": return new PlaneSurface(Placement(a.ElementAtOrDefault(1)));
            case "CYLINDRICAL_SURFACE": return new CylinderSurface(Placement(a[1]), Num(a[2]));
            case "CONICAL_SURFACE": return new ConeSurface(Placement(a[1]), Num(a[2]), Num(a[3]) * _angle);
            case "SPHERICAL_SURFACE": return new SphereSurface(Placement(a[1]), Num(a[2]));
            case "TOROIDAL_SURFACE":
            case "DEGENERATE_TOROIDAL_SURFACE":
                return new TorusSurface(Placement(a[1]), Num(a[2]), Num(a[3]));
            case "SURFACE_OF_REVOLUTION":
            {
                var c = CurveOf(_f.Get(a[1]));
                var ax = _f.Get(a[2]);
                if (c is null || ax is null) return null;
                return new RevolutionSurface(c, Point(ax.Args.ElementAtOrDefault(1)), ax.Args.ElementAtOrDefault(2) is null ? new V3(0, 0, 1) : Dir(ax.Args[2]));
            }
            case "SURFACE_OF_LINEAR_EXTRUSION":
            {
                var c = CurveOf(_f.Get(a[1]));
                var vec = _f.Get(a[2]);
                if (c is null || vec is null) return null;
                return new ExtrusionSurface(c, Dir(vec.Args.ElementAtOrDefault(1)) * Num(vec.Args.ElementAtOrDefault(2), 1));
            }
            case "B_SPLINE_SURFACE_WITH_KNOTS":
                return new BSplineSurface((int)Num(a[1]), (int)Num(a[2]), Grid(a[3]), null, Ints(a[8]), Ints(a[9]), Nums(a[10]), Nums(a[11]),
                    a.ElementAtOrDefault(5) is StepEnum { Value: "T" }, a.ElementAtOrDefault(6) is StepEnum { Value: "T" });
        }
        if (e.Parts.TryGetValue("B_SPLINE_SURFACE", out var bs) && e.Parts.TryGetValue("B_SPLINE_SURFACE_WITH_KNOTS", out var bk))
        {
            var grid = Grid(bs[2]);
            double[,]? w = null;
            if (e.Parts.TryGetValue("RATIONAL_B_SPLINE_SURFACE", out var rw) && rw[0] is List<object?> rows)
            {
                w = new double[grid.GetLength(0), grid.GetLength(1)];
                for (var i = 0; i < rows.Count && i < grid.GetLength(0); i++)
                    if (rows[i] is List<object?> row)
                        for (var j = 0; j < row.Count && j < grid.GetLength(1); j++) w[i, j] = Num(row[j], 1);
            }
            return new BSplineSurface((int)Num(bs[0]), (int)Num(bs[1]), grid, w, Ints(bk[0]), Ints(bk[1]), Nums(bk[2]), Nums(bk[3]),
                bs.ElementAtOrDefault(4) is StepEnum { Value: "T" }, bs.ElementAtOrDefault(5) is StepEnum { Value: "T" });
        }
        return null;
    }

    private V3[,] Grid(object? o)
    {
        if (o is not List<object?> rows || rows.Count == 0) return new V3[1, 1];
        var cols = rows.Max(r => r is List<object?> l ? l.Count : 0);
        var g = new V3[rows.Count, cols];
        for (var i = 0; i < rows.Count; i++)
            if (rows[i] is List<object?> row)
                for (var j = 0; j < row.Count; j++) g[i, j] = Point(row[j]);
        return g;
    }

    private List<V3> Points(object? o) => o is List<object?> l ? l.Select(Point).ToList() : [];
    private static List<double> Nums(object? o) => o is List<object?> l ? l.Select(x => Num(x)).ToList() : [];
    private static List<int> Ints(object? o) => o is List<object?> l ? l.Select(x => (int)Num(x)).ToList() : [];

    private static double Num(object? o, double def = 0) => o switch
    {
        double d => d,
        StepTyped t when t.Args.Count > 0 => Num(t.Args[0], def),
        _ => def,
    };

    private V3 Point(object? r)
    {
        if (_f.Get(r) is not { } e || e.Args.ElementAtOrDefault(1) is not List<object?> c) return V3.Zero;
        return new V3(Num(c.ElementAtOrDefault(0)), Num(c.ElementAtOrDefault(1)), Num(c.ElementAtOrDefault(2)));
    }

    private V3 Dir(object? r)
    {
        if (_f.Get(r) is not { } e || e.Args.ElementAtOrDefault(1) is not List<object?> c) return new V3(0, 0, 1);
        return new V3(Num(c.ElementAtOrDefault(0)), Num(c.ElementAtOrDefault(1)), Num(c.ElementAtOrDefault(2))).Normalized();
    }

    /// <summary>Placement (AXIS2_PLACEMENT_3D u operador de transformación) como sistema de coordenadas.</summary>
    private Frame Placement(object? r)
    {
        if (_f.Get(r) is not { } e) return Frame.Identity;
        if (e.Type == "AXIS2_PLACEMENT_3D")
            return Frame.From(Point(e.Args.ElementAtOrDefault(1)),
                e.Args.ElementAtOrDefault(2) is null ? null : Dir(e.Args[2]),
                e.Args.ElementAtOrDefault(3) is null ? null : Dir(e.Args[3]));
        if (e.Type == "AXIS2_PLACEMENT_2D")
            return Frame.From(Point(e.Args.ElementAtOrDefault(1)), null, e.Args.ElementAtOrDefault(2) is null ? null : Dir(e.Args[2]));
        if (e.Is("CARTESIAN_TRANSFORMATION_OPERATOR_3D") || e.Is("CARTESIAN_TRANSFORMATION_OPERATOR"))
        {
            var a = e.Is("CARTESIAN_TRANSFORMATION_OPERATOR") ? e.ArgsOf("CARTESIAN_TRANSFORMATION_OPERATOR") : e.Args;
            // (name, description, axis1, axis2, local_origin, scale[, axis3])
            var off = e.Parts.Count > 0 ? 0 : 2;
            var x = a.ElementAtOrDefault(off) is null ? (V3?)null : Dir(a[off]);
            var o = Point(a.ElementAtOrDefault(off + 2));
            var scale = Num(a.ElementAtOrDefault(off + 3), 1);
            if (scale == 0) scale = 1;
            var z = e.Type == "CARTESIAN_TRANSFORMATION_OPERATOR_3D" && a.ElementAtOrDefault(off + 4) is not null ? Dir(a[off + 4]) : (V3?)null;
            var fr = Frame.From(o, z, x);
            return new Frame(fr.O, fr.X * scale, fr.Y * scale, fr.Z * scale);
        }
        return Frame.Identity;
    }

    // ------------------------------------------------------------------ unidades, tolerancia y colores

    /// <summary>Unidades de longitud (→ mm) y de ángulo (→ rad) del contexto de una representación.</summary>
    private (double Scale, double Angle) UnitsOf(StepEntity rep)
    {
        var args = rep.Parts.TryGetValue("REPRESENTATION", out var ra) ? ra : rep.Args;
        var ctx = _f.Get(args.ElementAtOrDefault(2));
        if (ctx is null) return (_defaultScale, _angle);
        if (_unitCache.TryGetValue(ctx.Id, out var u)) return u;
        double scale = _defaultScale, angle = 1;
        if (ctx.Parts.TryGetValue("GLOBAL_UNIT_ASSIGNED_CONTEXT", out var ga) && ga.ElementAtOrDefault(0) is List<object?> units)
            foreach (var ur in units)
            {
                if (_f.Get(ur) is not { } unit) continue;
                if (unit.Parts.ContainsKey("LENGTH_UNIT")) scale = LengthScale(unit, 0);
                else if (unit.Parts.ContainsKey("PLANE_ANGLE_UNIT")) angle = AngleScale(unit);
            }
        return _unitCache[ctx.Id] = (scale, angle);
    }

    private double LengthScale(StepEntity unit, int depth)
    {
        if (unit.Parts.TryGetValue("SI_UNIT", out var si))
        {
            var prefix = si.ElementAtOrDefault(0) is StepEnum p ? p.Value : null;
            return prefix switch { "MILLI" => 1, "CENTI" => 10, "DECI" => 100, "KILO" => 1e6, "MICRO" => 1e-3, _ => 1000 };
        }
        if (unit.Parts.TryGetValue("CONVERSION_BASED_UNIT", out var cb))
        {
            if (depth < 4 && _f.Get(cb.ElementAtOrDefault(1)) is { } mwu)
            {
                var v = Num(mwu.Args.ElementAtOrDefault(0), 0);
                if (v > 0 && _f.Get(mwu.Args.ElementAtOrDefault(1)) is { } baseUnit) return v * LengthScale(baseUnit, depth + 1);
            }
            var name = (cb.ElementAtOrDefault(0) as string ?? "").ToUpperInvariant();
            return name.Contains("INCH") ? 25.4 : name.Contains("FOOT") ? 304.8 : name.Contains("THOU") || name.Contains("MIL") ? 0.0254 : 1;
        }
        return 1;
    }

    private double AngleScale(StepEntity unit)
    {
        if (unit.Parts.TryGetValue("CONVERSION_BASED_UNIT", out var cb))
        {
            if (_f.Get(cb.ElementAtOrDefault(1)) is { } mwu && Num(mwu.Args.ElementAtOrDefault(0), 0) is var v and > 0)
                return v;   // medida en radianes del grado (0.01745…)
            return (cb.ElementAtOrDefault(0) as string ?? "").Contains("DEGREE", StringComparison.OrdinalIgnoreCase) ? Math.PI / 180 : 1;
        }
        return 1;
    }

    private void ReadUnits()
    {
        foreach (var e in _f.Entities.Values)
        {
            if (e.Parts.ContainsKey("LENGTH_UNIT"))
            {
                if (e.Parts.TryGetValue("SI_UNIT", out var si))
                {
                    var prefix = si.ElementAtOrDefault(0) is StepEnum p ? p.Value : null;
                    _defaultScale = prefix switch { "MILLI" => 1, "CENTI" => 10, "DECI" => 100, "KILO" => 1e6, "MICRO" => 1e-3, _ => 1000 };
                }
                else _defaultScale = LengthScale(e, 0);
                break;
            }
        }
        foreach (var e in _f.Entities.Values)
            if (e.Parts.ContainsKey("PLANE_ANGLE_UNIT") && e.Parts.TryGetValue("CONVERSION_BASED_UNIT", out var cb)
                && (cb.ElementAtOrDefault(0) as string ?? "").Contains("DEGREE", StringComparison.OrdinalIgnoreCase))
            { _angle = Math.PI / 180; break; }
    }

    private void EstimateTolerance()
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var any = false;
        foreach (var e in _f.Entities.Values)
        {
            if (e.Type != "VERTEX_POINT") continue;
            var p = Point(e.Args.ElementAtOrDefault(1));
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            any = true;
        }
        var diag = any ? new V3(maxX - minX, maxY - minY, maxZ - minZ).Length * _defaultScale : 100;
        _tolMm = Math.Clamp(diag * 0.004, 0.05, 1.5);
    }

    private uint? Color(int id) => _colors.TryGetValue(id, out var c) ? c : null;

    private void ReadColors()
    {
        foreach (var e in _f.Entities.Values)
        {
            if (e.Type is not ("STYLED_ITEM" or "OVER_RIDING_STYLED_ITEM")) continue;
            if (_f.Get(e.Args.ElementAtOrDefault(2)) is not { } item) continue;
            var col = FindColor(e.Args.ElementAtOrDefault(1), 0);
            if (col is { } c && !(_colors.ContainsKey(item.Id) && e.Type == "STYLED_ITEM")) _colors[item.Id] = c;
        }
    }

    private uint? FindColor(object? o, int depth)
    {
        if (depth > 12) return null;
        switch (o)
        {
            case List<object?> l:
                foreach (var x in l) if (FindColor(x, depth + 1) is { } c) return c;
                return null;
            case StepRef r when _f[r.Id] is { } e:
                switch (e.Type)
                {
                    case "COLOUR_RGB":
                    {
                        double R = Num(e.Args.ElementAtOrDefault(1)), G = Num(e.Args.ElementAtOrDefault(2)), B = Num(e.Args.ElementAtOrDefault(3));
                        static uint C(double x) => (uint)Math.Clamp(Math.Round(x * 255), 0, 255);
                        return 0xFF000000 | (C(R) << 16) | (C(G) << 8) | C(B);
                    }
                    case "DRAUGHTING_PRE_DEFINED_COLOUR":
                        return (e.Args.ElementAtOrDefault(0) as string)?.ToLowerInvariant() switch
                        {
                            "red" => 0xFFD32F2F, "green" => 0xFF2E9E4D, "blue" => 0xFF2F6DB5, "yellow" => 0xFFE8C21F,
                            "magenta" => 0xFFC040C0, "cyan" => 0xFF30B0C0, "black" => 0xFF202326, "white" => 0xFFF2F2F2,
                            _ => null,
                        };
                    case "CURVE_STYLE":
                        return null;
                    default:
                        foreach (var a in e.Args) if (FindColor(a, depth + 1) is { } c) return c;
                        return null;
                }
            default:
                return null;
        }
    }
}
