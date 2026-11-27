using System.Globalization;
using System.Numerics;

namespace Simulador.Tableros.Model;

/// <summary>
/// Modelo 3D de un aparato de librería, en milímetros: X a lo ancho (0 = borde izquierdo), Y hacia fuera desde la
/// superficie de montaje (placa o carril; en la puerta, 0 = cara exterior de la puerta, positivo lo que sobresale y
/// negativo lo que queda dentro) y Z hacia arriba (0 = borde inferior).
/// </summary>
public sealed class DeviceMesh
{
    public List<(Vector3 A, Vector3 B, Vector3 C, uint Color)> Triangles { get; } = [];
    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }
    public Vector3 Size => Max - Min;

    public void Add(Vector3 a, Vector3 b, Vector3 c, uint color) => Triangles.Add((a, b, c, color));

    /// <summary>Calcula la caja que envuelve la malla (llamar tras añadir triángulos).</summary>
    public DeviceMesh Seal()
    {
        if (Triangles.Count == 0) return this;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (a, b, c, _) in Triangles)
        {
            min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
            max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
        }
        Min = min;
        Max = max;
        return this;
    }

    private const uint DefaultColor = 0xFFB9C0C7;

    /// <summary>
    /// Copia con menos triángulos (agrupando vértices en una rejilla), para que los modelos muy detallados de los
    /// fabricantes no hagan lenta la vista 3D. Si ya tiene pocos, devuelve el mismo.
    /// </summary>
    public DeviceMesh Simplified(int maxTriangles)
    {
        if (Triangles.Count <= maxTriangles || Triangles.Count == 0) return this;
        var size = Size.Length();
        if (size <= 0) return this;
        for (var cells = 800.0; cells >= 20; cells *= 0.8)
        {
            var cell = size / (float)cells;
            var rep = new Dictionary<(int, int, int), Vector3>();
            (int, int, int) Key(Vector3 p) => ((int)MathF.Floor((p.X - Min.X) / cell), (int)MathF.Floor((p.Y - Min.Y) / cell), (int)MathF.Floor((p.Z - Min.Z) / cell));
            Vector3 Rep(Vector3 p) { var k = Key(p); if (!rep.TryGetValue(k, out var r)) rep[k] = r = p; return r; }
            var seen = new HashSet<((int, int, int), (int, int, int), (int, int, int))>();
            var outMesh = new DeviceMesh();
            foreach (var (a, b, c, col) in Triangles)
            {
                var ka = Key(a); var kb = Key(b); var kc = Key(c);
                if (ka == kb || kb == kc || ka == kc) continue;
                // Mismo triángulo con otro orden: una sola vez.
                var sorted = new[] { ka, kb, kc }.OrderBy(k => k).ToArray();
                if (!seen.Add((sorted[0], sorted[1], sorted[2]))) continue;
                outMesh.Add(Rep(a), Rep(b), Rep(c), col);
            }
            if (outMesh.Triangles.Count <= maxTriangles) return outMesh.Seal();
        }
        return this;
    }

    /// <summary>
    /// Lee un Wavefront .obj (v, f y usemtl). Los colores salen del .mtl (Kd) si se da, o del nombre del material
    /// cuando es «c_RRGGBB». Las caras de más de tres vértices se abren en abanico.
    /// </summary>
    public static DeviceMesh FromObj(string obj, string? mtl = null)
    {
        var colors = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (mtl is not null)
        {
            string? current = null;
            foreach (var raw in mtl.Split('\n'))
            {
                var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && p[0] == "newmtl") current = p[1];
                else if (p.Length >= 4 && p[0] == "Kd" && current is not null)
                    colors[current] = Argb(F(p[1]), F(p[2]), F(p[3]));
            }
        }
        var mesh = new DeviceMesh();
        var v = new List<Vector3>();
        var color = DefaultColor;
        foreach (var raw in obj.Split('\n'))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v" when p.Length >= 4:
                    v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                    break;
                case "usemtl" when p.Length >= 2:
                    color = colors.TryGetValue(p[1], out var c) ? c : ColorFromName(p[1]);
                    break;
                case "f" when p.Length >= 4:
                {
                    var idx = p.Skip(1).Select(t => Index(t, v.Count)).ToList();
                    if (idx.Any(i => i < 0 || i >= v.Count)) break;
                    for (var i = 1; i + 1 < idx.Count; i++) mesh.Add(v[idx[0]], v[idx[i]], v[idx[i + 1]], color);
                    break;
                }
            }
        }
        return mesh.Seal();
    }

    /// <summary>Lee un .stl binario o de texto (sin colores: gris claro).</summary>
    public static DeviceMesh FromStl(byte[] data)
    {
        var mesh = new DeviceMesh();
        var ascii = data.Length > 5 && System.Text.Encoding.ASCII.GetString(data, 0, 5) == "solid"
                    && System.Text.Encoding.ASCII.GetString(data).Contains("facet");
        if (ascii)
        {
            var pts = new List<Vector3>();
            foreach (var raw in System.Text.Encoding.ASCII.GetString(data).Split('\n'))
            {
                var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 && p[0] == "vertex") pts.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                if (pts.Count == 3) { mesh.Add(pts[0], pts[1], pts[2], DefaultColor); pts.Clear(); }
            }
        }
        else if (data.Length >= 84)
        {
            var n = BitConverter.ToInt32(data, 80);
            for (var i = 0; i < n && 84 + i * 50 + 50 <= data.Length; i++)
            {
                var o = 84 + i * 50 + 12;
                Vector3 P(int k) => new(BitConverter.ToSingle(data, o + k * 12), BitConverter.ToSingle(data, o + k * 12 + 4), BitConverter.ToSingle(data, o + k * 12 + 8));
                mesh.Add(P(0), P(1), P(2), DefaultColor);
            }
        }
        return mesh.Seal();
    }

    private static int Index(string token, int count)
    {
        var s = token.Split('/')[0];
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return -1;
        return i < 0 ? count + i : i - 1;
    }

    private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;

    private static uint Argb(float r, float g, float b)
    {
        static uint C(float x) => (uint)Math.Clamp(Math.Round(x * 255), 0, 255);
        return 0xFF000000 | (C(r) << 16) | (C(g) << 8) | C(b);
    }

    private static uint ColorFromName(string name) =>
        name.StartsWith("c_", StringComparison.Ordinal) && name.Length == 8
        && uint.TryParse(name[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? 0xFF000000 | rgb : DefaultColor;
}
