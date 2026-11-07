using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Export;

/// <summary>
/// Exporta el edificio a IFC4 (ISO 16739, formato STEP de texto) para abrirlo en programas BIM como Revit, ArchiCAD,
/// BIMvision o BlenderBIM. Cada nivel es una planta (IfcBuildingStorey) con sus muros (con los huecos recortados),
/// puertas, ventanas, forjados, escaleras, habitaciones con su superficie e instalaciones colocadas.
/// La geometría es sólida extruida (perfiles en planta y altura), en metros. Los identificadores (GlobalId) se
/// derivan del id de cada elemento, así que exportar dos veces el mismo edificio da los mismos GlobalId.
/// </summary>
public static class IfcExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ToIfc(BuildingDocument doc, DateTime? timestamp = null) => new Writer(doc).Write(timestamp ?? DateTime.Now);

    public static byte[] ToIfcBytes(BuildingDocument doc, DateTime? timestamp = null) => Encoding.ASCII.GetBytes(ToIfc(doc, timestamp));

    private sealed class Writer(BuildingDocument doc)
    {
        private readonly StringBuilder _data = new();
        private int _next = 1;
        private int _ctx, _origin, _zAxis, _xAxis, _identity3;

        private int Add(string entity)
        {
            int id = _next++;
            _data.Append('#').Append(id).Append('=').Append(entity).Append(";\n");
            return id;
        }

        public string Write(DateTime stamp)
        {
            string projectGuid = Guid(doc.Name + "/proyecto");

            // Unidades (SI, metros) y contexto geométrico.
            int len = Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            int area = Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            int vol = Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            int ang = Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            int units = Add($"IFCUNITASSIGNMENT(({Ref(len)},{Ref(area)},{Ref(vol)},{Ref(ang)}))");
            _origin = Add("IFCCARTESIANPOINT((0.,0.,0.))");
            _zAxis = Add("IFCDIRECTION((0.,0.,1.))");
            _xAxis = Add("IFCDIRECTION((1.,0.,0.))");
            _identity3 = Add($"IFCAXIS2PLACEMENT3D({Ref(_origin)},{Ref(_zAxis)},{Ref(_xAxis)})");
            int model = Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,{Ref(_identity3)},$)");
            _ctx = Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,{Ref(model)},$,.MODEL_VIEW.,$)");

            int project = Add($"IFCPROJECT('{projectGuid}',$,{S(doc.Name)},$,$,$,$,({Ref(model)}),{Ref(units)})");
            int sitePl = Placement(null, 0);
            int site = Add($"IFCSITE('{Guid(doc.Name + "/parcela")}',$,'Parcela',$,$,{Ref(sitePl)},$,$,.ELEMENT.,$,$,$,$,$)");
            int bldPl = Placement(sitePl, 0);
            int building = Add($"IFCBUILDING('{Guid(doc.Name + "/edificio")}',$,{S(doc.Name)},$,$,{Ref(bldPl)},$,$,.ELEMENT.,$,$,$)");
            Rel("IFCRELAGGREGATES", "agg/proyecto", project, [site]);
            Rel("IFCRELAGGREGATES", "agg/parcela", site, [building]);

            var storeys = new List<int>();
            var materials = new Dictionary<string, List<int>>();
            foreach (var lvl in doc.Levels.OrderBy(l => l.Elevation))
            {
                int pl = Placement(bldPl, lvl.Elevation);
                int storey = Add($"IFCBUILDINGSTOREY('{Guid(lvl.Id)}',$,{S(lvl.Name)},$,$,{Ref(pl)},$,$,.ELEMENT.,{R(lvl.Elevation)})");
                storeys.Add(storey);
                var contained = new List<int>();
                var spaces = new List<int>();

                foreach (var w in doc.Walls.Where(w => w.LevelId == lvl.Id))
                {
                    int wall = WriteWall(w, pl, contained);
                    if (!materials.TryGetValue(w.Material, out var list)) materials[w.Material] = list = [];
                    list.Add(wall);
                }
                foreach (var s in doc.Slabs.Where(s => s.LevelId == lvl.Id && s.Kind != SlabKind.Ceiling))
                {
                    double z = s.Kind == SlabKind.Roof ? lvl.Height - s.Thickness : -s.Thickness;
                    int spl = Placement(pl, 0);
                    int rep = Body("SweptSolid", [Extrude(s.Outline, z, s.Thickness)]);
                    string kind = s.Kind == SlabKind.Roof ? "ROOF" : "FLOOR";
                    string name = s.Kind == SlabKind.Roof ? "Cubierta" : "Forjado";
                    contained.Add(Add($"IFCSLAB('{Guid(s.Id)}',$,'{name}',$,$,{Ref(spl)},{Ref(rep)},{S(s.Id)},.{kind}.)"));
                }
                foreach (var st in doc.Stairs.Where(s => s.LevelId == lvl.Id))
                {
                    int spl = Placement(pl, 0);
                    int rep = Body("SweptSolid", st.Steps.Select(step => Extrude(step.Outline, 0, step.Top)).ToList());
                    string type = st.Shape switch
                    {
                        StairShape.L => "QUARTER_TURN_STAIR",
                        StairShape.U => "HALF_TURN_STAIR",
                        _ => "STRAIGHT_RUN_STAIR",
                    };
                    contained.Add(Add($"IFCSTAIR('{Guid(st.Id)}',$,'Escalera',$,$,{Ref(spl)},{Ref(rep)},{S(st.Id)},.{type}.)"));
                }
                foreach (var sp in doc.Spaces.Where(s => s.LevelId == lvl.Id))
                {
                    int spl = Placement(pl, 0);
                    int rep = Body("SweptSolid", [Extrude(sp.Outline, 0, lvl.Height)]);
                    int space = Add($"IFCSPACE('{Guid(sp.Id + "/" + lvl.Id)}',$,{S(sp.Name)},$,{S(sp.Use)},{Ref(spl)},{Ref(rep)},{S(sp.Name)},.ELEMENT.,.INTERNAL.,$)");
                    spaces.Add(space);
                    int qa = Add($"IFCQUANTITYAREA('NetFloorArea',$,$,{R(sp.Area)},$)");
                    int ql = Add($"IFCQUANTITYLENGTH('GrossPerimeter',$,$,{R(sp.Perimeter)},$)");
                    int qto = Add($"IFCELEMENTQUANTITY('{Guid(sp.Id + "/" + lvl.Id + "/qto")}',$,'Qto_SpaceBaseQuantities',$,$,({Ref(qa)},{Ref(ql)}))");
                    Add($"IFCRELDEFINESBYPROPERTIES('{Guid(sp.Id + "/" + lvl.Id + "/relqto")}',$,$,$,({Ref(space)}),{Ref(qto)})");
                }
                foreach (var f in doc.Fixtures.Where(f => f.LevelId == lvl.Id))
                    contained.Add(WriteFixture(f, pl, lvl));

                if (contained.Count > 0) Rel("IFCRELCONTAINEDINSPATIALSTRUCTURE", "cont/" + lvl.Id, storey, contained, relatedFirst: true);
                if (spaces.Count > 0) Rel("IFCRELAGGREGATES", "agg/" + lvl.Id, storey, spaces);
            }
            if (storeys.Count > 0) Rel("IFCRELAGGREGATES", "agg/edificio", building, storeys);

            foreach (var (name, walls) in materials)
            {
                int mat = Add($"IFCMATERIAL({S(name)},$,$)");
                Add($"IFCRELASSOCIATESMATERIAL('{Guid("mat/" + name)}',$,$,$,({string.Join(',', walls.Select(Ref))}),{Ref(mat)})");
            }

            var sb = new StringBuilder();
            sb.Append("ISO-10303-21;\nHEADER;\n");
            sb.Append("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\n");
            sb.Append($"FILE_NAME({S(doc.Name + ".ifc")},'{stamp:yyyy-MM-ddTHH:mm:ss}',(''),(''),'Simulador multiple','Simulador multiple - Edificacion','');\n");
            sb.Append("FILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n");
            sb.Append(_data);
            sb.Append("ENDSEC;\nEND-ISO-10303-21;\n");
            return sb.ToString();
        }

        /// <summary>Muro con su sólido y, por cada hueco, un IfcOpeningElement que lo vacía y la puerta o ventana que lo llena.</summary>
        private int WriteWall(Wall w, int storeyPl, List<int> contained)
        {
            int pl = Placement(storeyPl, 0);
            var d = w.Direction;
            var n = d.Perp() * (w.Thickness / 2);
            // Se alarga medio espesor por cada lado para que las esquinas queden cerradas.
            var a = w.Start - d * (w.Thickness / 2);
            var b = w.End + d * (w.Thickness / 2);
            int rep = Body("SweptSolid", [Extrude([a + n, b + n, b - n, a - n], 0, w.Height)]);
            int wall = Add($"IFCWALL('{Guid(w.Id)}',$,'Muro',$,{S(w.Material)},{Ref(pl)},{Ref(rep)},{S(w.Id)},.STANDARD.)");
            contained.Add(wall);

            foreach (var o in doc.Openings.Where(o => o.HostWallId == w.Id))
            {
                var c = w.Start + d * o.Offset;
                var h = d * (o.Width / 2);
                var nn = d.Perp() * (w.Thickness / 2 + 0.02);
                int opl = Placement(pl, 0);
                int orep = Body("SweptSolid", [Extrude([c - h + nn, c + h + nn, c + h - nn, c - h - nn], o.SillHeight, o.Height)]);
                int opening = Add($"IFCOPENINGELEMENT('{Guid(o.Id + "/hueco")}',$,'Hueco',$,$,{Ref(opl)},{Ref(orep)},$,.OPENING.)");
                Add($"IFCRELVOIDSELEMENT('{Guid(o.Id + "/vacia")}',$,$,$,{Ref(wall)},{Ref(opening)})");

                // Hoja o carpintería: una placa fina centrada en el muro.
                var fn = d.Perp() * 0.025;
                int fpl = Placement(storeyPl, 0);
                int frep = Body("SweptSolid", [Extrude([c - h + fn, c + h + fn, c + h - fn, c - h - fn], o.SillHeight, o.Height)]);
                int fill = o.Kind == OpeningKind.Door
                    ? Add($"IFCDOOR('{Guid(o.Id)}',$,'Puerta',$,$,{Ref(fpl)},{Ref(frep)},{S(o.Id)},{R(o.Height)},{R(o.Width)},.DOOR.,.{(o.SwingLeft == o.HingeAtStart ? "SINGLE_SWING_LEFT" : "SINGLE_SWING_RIGHT")}.,$)")
                    : Add($"IFCWINDOW('{Guid(o.Id)}',$,'Ventana',$,$,{Ref(fpl)},{Ref(frep)},{S(o.Id)},{R(o.Height)},{R(o.Width)},.WINDOW.,.SINGLE_PANEL.,$)");
                Add($"IFCRELFILLSELEMENT('{Guid(o.Id + "/llena")}',$,$,$,{Ref(opening)},{Ref(fill)})");
                contained.Add(fill);
            }
            return wall;
        }

        /// <summary>Instalación colocada: un elemento genérico con su designación del banco de pruebas (X1, E3…).</summary>
        private int WriteFixture(PlacedFixture f, int storeyPl, Level lvl)
        {
            double z = f.Position.Z - lvl.Elevation;
            double hw = Math.Max(0.05, f.Width / 2), hd = Math.Max(0.03, f.Depth / 2);
            double height = f.Kind == "tanque" ? Math.Max(0.5, f.Width) : 0.1;
            double rot = f.Rotation * Math.PI / 180;
            var u = new Point2(Math.Cos(rot), Math.Sin(rot));
            var v = u.Perp();
            var p = f.Position.Xy;
            int pl = Placement(storeyPl, 0);
            int rep = Body("SweptSolid", [Extrude([p - u * hw - v * hd, p + u * hw - v * hd, p + u * hw + v * hd, p - u * hw + v * hd], z, height)]);
            string module = f.ModuleId == "hidraulica" ? "Hidráulica" : f.ModuleId == "electrico" ? "Electricidad" : f.ModuleId;
            return Add($"IFCBUILDINGELEMENTPROXY('{Guid(f.Id)}',$,{S(f.ComponentId)},{S(module)},{S(f.Kind)},{Ref(pl)},{Ref(rep)},{S(f.ComponentId)},.NOTDEFINED.)");
        }

        // ------------------------------------------------------------ geometría

        private int Placement(int? relativeTo, double z)
        {
            int axis = _identity3;
            if (Math.Abs(z) > 1e-9)
            {
                int pt = Add($"IFCCARTESIANPOINT((0.,0.,{R(z)}))");
                axis = Add($"IFCAXIS2PLACEMENT3D({Ref(pt)},$,$)");
            }
            return Add($"IFCLOCALPLACEMENT({(relativeTo is { } r ? Ref(r) : "$")},{Ref(axis)})");
        }

        /// <summary>Prisma: el polígono en planta extruido hacia arriba desde <paramref name="z0"/> con altura <paramref name="height"/>.</summary>
        private int Extrude(IReadOnlyList<Point2> outline, double z0, double height)
        {
            var pts = outline.ToList();
            if (pts.Count > 1 && pts[0].DistanceTo(pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
            // Perfil en sentido antihorario (lo esperan algunos visores).
            double signed = 0;
            for (int i = 0; i < pts.Count; i++) signed += Point2.Cross(pts[i], pts[(i + 1) % pts.Count]);
            if (signed < 0) pts.Reverse();
            var ids = pts.Select(p => Add($"IFCCARTESIANPOINT(({R(p.X)},{R(p.Y)}))")).ToList();
            ids.Add(ids[0]);
            int poly = Add($"IFCPOLYLINE(({string.Join(',', ids.Select(Ref))}))");
            int profile = Add($"IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,{Ref(poly)})");
            int pos = _identity3;
            if (Math.Abs(z0) > 1e-9)
            {
                int pt = Add($"IFCCARTESIANPOINT((0.,0.,{R(z0)}))");
                pos = Add($"IFCAXIS2PLACEMENT3D({Ref(pt)},$,$)");
            }
            return Add($"IFCEXTRUDEDAREASOLID({Ref(profile)},{Ref(pos)},{Ref(_zAxis)},{R(Math.Max(1e-3, height))})");
        }

        private int Body(string type, IReadOnlyList<int> items)
        {
            int shape = Add($"IFCSHAPEREPRESENTATION({Ref(_ctx)},'Body','{type}',({string.Join(',', items.Select(Ref))}))");
            return Add($"IFCPRODUCTDEFINITIONSHAPE($,$,({Ref(shape)}))");
        }

        private void Rel(string entity, string key, int relating, IReadOnlyList<int> related, bool relatedFirst = false)
        {
            string list = $"({string.Join(',', related.Select(Ref))})";
            Add(relatedFirst
                ? $"{entity}('{Guid(key)}',$,$,$,{list},{Ref(relating)})"
                : $"{entity}('{Guid(key)}',$,$,$,{Ref(relating)},{list})");
        }

        // ------------------------------------------------------------ STEP

        private static string Ref(int id) => "#" + id.ToString(Inv);

        /// <summary>Real STEP: siempre con punto decimal.</summary>
        public static string R(double v)
        {
            if (Math.Abs(v) < 1e-12) return "0.";
            var s = v.ToString("0.0#########", Inv);
            return s;
        }

        /// <summary>Cadena STEP con las letras que no son ASCII codificadas como \X2\hhhh\X0\ (tildes, ñ, ²).</summary>
        public static string S(string? text)
        {
            if (text is null) return "$";
            var sb = new StringBuilder("'");
            foreach (char c in text)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c < 127) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4", Inv)).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        /// <summary>GlobalId IFC: 128 bits en 22 caracteres del alfabeto IFC, derivado de forma estable de una clave.</summary>
        private string Guid(string key) => CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes(doc.Name + "|" + key)));
    }

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Codifica 16 bytes como GlobalId IFC (el primer carácter lleva 2 bits y los 21 siguientes 6 bits cada uno).</summary>
    public static string CompressGuid(byte[] bytes)
    {
        if (bytes.Length != 16) throw new ArgumentException("Se esperaban 16 bytes.", nameof(bytes));
        var n = new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var chars = new char[22];
        for (int i = 21; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(n & 63)];
            n >>= 6;
        }
        return new string(chars);
    }
}
