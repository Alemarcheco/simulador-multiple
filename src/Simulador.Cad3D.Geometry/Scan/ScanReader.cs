using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace Simulador.Cad3D.Geometry.Scan;

/// <summary>Eje que apunta hacia arriba en el archivo.</summary>
public enum UpAxis
{
    /// <summary>
    /// OBJ, PLY, GLB y USDZ con Y arriba (lo que exportan las apps de escaneo); STL con Z arriba, salvo si viene en
    /// metros (STL de ARKit, Y arriba).
    /// </summary>
    Automatico,
    Y,
    Z,
}

/// <summary>
/// Lee las mallas que exportan las apps de escaneo del móvil (Polycam, Scaniverse, 3d Scanner App, KIRI Engine…) y
/// otros programas: OBJ (con su .mtl y textura), STL, PLY (de texto o binario, con colores) y glTF/GLB. Devuelve la
/// malla en las unidades del archivo con Z hacia arriba, y los colores de la textura pasados a los vértices.
/// </summary>
public static class ScanReader
{
    public static readonly string[] Extensions = [".obj", ".stl", ".ply", ".glb", ".gltf", ".usdz", ".usdc"];

    /// <summary>Más que esto no cabe con soltura en memoria ni en el visor.</summary>
    private const int MaxTriangles = 6_000_000;

    public static ScanMesh Read(string path, UpAxis up = UpAxis.Automatico)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"no se encuentra {path}");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Func<string, byte[]?> sibling = name =>
        {
            var p = Path.Combine(dir, Uri.UnescapeDataString(name.Replace('\\', '/')));
            if (File.Exists(p)) return File.ReadAllBytes(p);
            var alt = Path.Combine(dir, Path.GetFileName(name.Replace('\\', '/')));
            return File.Exists(alt) ? File.ReadAllBytes(alt) : null;
        };
        return ext switch
        {
            ".fbx" or ".dae" or ".3ds" => throw new NotSupportedException(
                $"el formato {ext} no se puede leer. Exporta el escaneo en OBJ, GLB, PLY o STL."),
            _ => Read(File.ReadAllBytes(path), ext, up, sibling),
        };
    }

    /// <param name="sibling">Devuelve otro archivo que acompaña al principal (.mtl, texturas, .bin), o null.</param>
    public static ScanMesh Read(byte[] data, string extension, UpAxis up = UpAxis.Automatico, Func<string, byte[]?>? sibling = null)
    {
        sibling ??= _ => null;
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        var mesh = ext switch
        {
            ".obj" => ReadObj(data, sibling),
            ".stl" => ReadStl(data),
            ".ply" => ReadPly(data),
            ".glb" => ReadGlb(data, sibling),
            ".gltf" => ReadGltf(Encoding.UTF8.GetString(data), null, sibling),
            ".usdz" or ".usdc" or ".usd" => ReadUsd(data, ext, ref up),
            _ => throw new NotSupportedException($"formato {ext} no admitido (usa OBJ, STL, PLY o GLB)"),
        };
        if (mesh.TriangleCount == 0)
            throw new InvalidDataException(ext == ".ply" && mesh.Vertices.Count > 0
                ? "el PLY es una nube de puntos sin caras. En la app de escaneo exporta la malla (mesh), no los puntos."
                : "el archivo no tiene triángulos");
        // glTF es siempre Y arriba (y USD salvo que diga upAxis = Z); las apps de escaneo también exportan OBJ y PLY con Y arriba.
        // Un STL en metros (pieza de menos de 20 unidades) viene de una app de escaneo (ARKit), que usa Y arriba.
        bool stlFromScanner = ext == ".stl" && mesh.Size is var sz && Math.Max(sz.X, Math.Max(sz.Y, sz.Z)) < 20;
        bool yUp = up == UpAxis.Y || (up == UpAxis.Automatico && (ext != ".stl" || stlFromScanner));
        if (yUp) mesh.Transform(v => new Vec3(v.X, -v.Z, v.Y));
        return mesh;
    }

    private static ScanMesh ReadUsd(byte[] data, string ext, ref UpAxis up)
    {
        var r = ext == ".usdz" ? UsdReader.ReadUsdz(data)
            : UsdReader.IsCrate(data) ? UsdReader.ReadCrate(data, _ => null)
            : throw new NotSupportedException("solo se lee USD binario (.usdc o .usdz)");
        r.Mesh.MetersPerUnit = r.MetersPerUnit;
        if (up == UpAxis.Automatico && r.ZUp) up = UpAxis.Z;
        return r.Mesh;
    }

    // ------------------------------------------------------------------ OBJ

    private static ScanMesh ReadObj(byte[] data, Func<string, byte[]?> sibling)
    {
        var text = Encoding.UTF8.GetString(data);
        var pos = new List<Vec3>();
        var vcol = new List<uint>();
        var uv = new List<(double U, double V)>();
        var mats = new Dictionary<string, (uint Kd, Texture? Tex)>(StringComparer.Ordinal);
        (uint Kd, Texture? Tex) current = (0xFFB9C0C7, null);
        var mesh = new ScanMesh();
        // Color acumulado por vértice (de la textura o del material): suma de R, G, B y cuántas veces.
        var acc = new List<(double R, double G, double B, int N)>();
        bool anyColor = false;
        var corner = new List<(int V, int T)>(8);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 2 || line[0] == '#') continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v" when p.Length >= 4:
                    pos.Add(new Vec3(D(p[1]), D(p[2]), D(p[3])));
                    if (p.Length >= 7) { vcol.Add(Rgb(D(p[4]), D(p[5]), D(p[6]))); anyColor = true; }
                    else vcol.Add(0);
                    break;
                case "vt" when p.Length >= 3:
                    uv.Add((D(p[1]), D(p[2])));
                    break;
                case "mtllib" when p.Length >= 2:
                    var mtlName = line[6..].Trim();
                    if (sibling(mtlName) is { } mtl) ParseMtl(Encoding.UTF8.GetString(mtl), sibling, mats);
                    break;
                case "usemtl" when p.Length >= 2:
                    current = mats.TryGetValue(p[1], out var m) ? m : (0xFFB9C0C7, null);
                    if (mats.ContainsKey(p[1])) anyColor = true;
                    break;
                case "f" when p.Length >= 4:
                {
                    corner.Clear();
                    foreach (var tok in p.Skip(1))
                    {
                        var parts = tok.Split('/');
                        int vi = Idx(parts[0], pos.Count);
                        int ti = parts.Length > 1 && parts[1].Length > 0 ? Idx(parts[1], uv.Count) : -1;
                        if (vi < 0 || vi >= pos.Count) { corner.Clear(); break; }
                        corner.Add((vi, ti));
                    }
                    if (corner.Count < 3) break;
                    while (acc.Count < pos.Count) acc.Add((0, 0, 0, 0));
                    foreach (var (vi, ti) in corner)
                    {
                        uint c = current.Tex is { } tex && ti >= 0 && ti < uv.Count ? tex.Sample(uv[ti].U, uv[ti].V)
                            : vcol[vi] != 0 ? vcol[vi] : current.Kd;
                        var a = acc[vi];
                        acc[vi] = (a.R + ((c >> 16) & 0xFF), a.G + ((c >> 8) & 0xFF), a.B + (c & 0xFF), a.N + 1);
                    }
                    for (int i = 1; i + 1 < corner.Count; i++)
                    {
                        mesh.Indices.Add(corner[0].V); mesh.Indices.Add(corner[i].V); mesh.Indices.Add(corner[i + 1].V);
                    }
                    if (mesh.Indices.Count / 3 > MaxTriangles) throw new InvalidDataException(TooBig);
                    break;
                }
            }
        }
        mesh.Vertices.AddRange(pos);
        if (anyColor)
        {
            while (acc.Count < pos.Count) acc.Add((0, 0, 0, 0));
            mesh.Colors = acc.Select((a, i) => a.N > 0
                ? 0xFF000000 | ((uint)(a.R / a.N) << 16) | ((uint)(a.G / a.N) << 8) | (uint)(a.B / a.N)
                : vcol[i] != 0 ? vcol[i] : 0xFFB9C0C7).ToList();
        }
        return mesh;
    }

    private static void ParseMtl(string mtl, Func<string, byte[]?> sibling, Dictionary<string, (uint Kd, Texture? Tex)> mats)
    {
        string? name = null;
        uint kd = 0xFFB9C0C7;
        Texture? tex = null;
        void Flush() { if (name is not null) mats[name] = (kd, tex); }
        foreach (var raw in mtl.Split('\n'))
        {
            var line = raw.Trim();
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) continue;
            if (p[0] == "newmtl") { Flush(); name = p[1]; kd = 0xFFB9C0C7; tex = null; }
            else if (p[0] == "Kd" && p.Length >= 4) kd = Rgb(D(p[1]), D(p[2]), D(p[3]));
            else if (p[0] == "map_Kd")
            {
                // El nombre de archivo es lo último (puede llevar opciones -s, -o… delante).
                var file = p[^1];
                if (sibling(file) is { } img) tex = Texture.Decode(img);
            }
        }
        Flush();
    }

    // ------------------------------------------------------------------ STL

    private static ScanMesh ReadStl(byte[] data)
    {
        var tris = new List<(Vec3, Vec3, Vec3)>();
        bool ascii = data.Length > 5 && Encoding.ASCII.GetString(data, 0, 5) == "solid"
                     && Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 2048)).Contains("facet");
        if (ascii)
        {
            var pts = new List<Vec3>(3);
            foreach (var raw in Encoding.ASCII.GetString(data).Split('\n'))
            {
                var p = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 && p[0] == "vertex") pts.Add(new Vec3(D(p[1]), D(p[2]), D(p[3])));
                if (pts.Count == 3) { tris.Add((pts[0], pts[1], pts[2])); pts.Clear(); }
            }
        }
        else if (data.Length >= 84)
        {
            int n = BitConverter.ToInt32(data, 80);
            if (n > MaxTriangles) throw new InvalidDataException(TooBig);
            for (int i = 0; i < n && 84 + i * 50 + 50 <= data.Length; i++)
            {
                int o = 84 + i * 50 + 12;
                Vec3 P(int k) => new(BitConverter.ToSingle(data, o + k * 12), BitConverter.ToSingle(data, o + k * 12 + 4), BitConverter.ToSingle(data, o + k * 12 + 8));
                tris.Add((P(0), P(1), P(2)));
            }
        }
        return FromTriangles(tris);
    }

    /// <summary>Malla indexada desde triángulos sueltos (une los vértices repetidos exactamente).</summary>
    internal static ScanMesh FromTriangles(IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> tris)
    {
        var mesh = new ScanMesh();
        var lookup = new Dictionary<Vec3, int>();
        int I(Vec3 v)
        {
            if (!lookup.TryGetValue(v, out var i)) { i = mesh.Vertices.Count; mesh.Vertices.Add(v); lookup[v] = i; }
            return i;
        }
        foreach (var (a, b, c) in tris) { mesh.Indices.Add(I(a)); mesh.Indices.Add(I(b)); mesh.Indices.Add(I(c)); }
        return mesh;
    }

    // ------------------------------------------------------------------ PLY

    private static ScanMesh ReadPly(byte[] data)
    {
        // Cabecera de texto hasta «end_header».
        int headerEnd = IndexOf(data, "end_header"u8);
        if (headerEnd < 0 || !Encoding.ASCII.GetString(data, 0, 3).Equals("ply")) throw new InvalidDataException("no es un archivo PLY");
        int bodyStart = headerEnd + "end_header".Length;
        while (bodyStart < data.Length && data[bodyStart] != '\n') bodyStart++;
        bodyStart++;
        var header = Encoding.ASCII.GetString(data, 0, headerEnd).Split('\n').Select(l => l.Trim()).ToList();
        string format = "ascii";
        var elements = new List<(string Name, int Count, List<(string Name, string Type, string? CountType)> Props)>();
        foreach (var l in header)
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "format" && p.Length >= 2) format = p[1];
            else if (p[0] == "element" && p.Length >= 3) elements.Add((p[1], int.Parse(p[2], CultureInfo.InvariantCulture), new()));
            else if (p[0] == "property" && elements.Count > 0)
            {
                if (p[1] == "list" && p.Length >= 5) elements[^1].Props.Add((p[4], p[3], p[2]));
                else if (p.Length >= 3) elements[^1].Props.Add((p[2], p[1], null));
            }
        }
        var mesh = new ScanMesh();
        var colors = new List<uint>();
        bool hasColor = false;

        var reader = new PlyReader(data, bodyStart, format);
        foreach (var (name, count, props) in elements)
        {
            if (name == "face" && (long)count > MaxTriangles) throw new InvalidDataException(TooBig);
            int ix = props.FindIndex(p => p.Name == "x"), iy = props.FindIndex(p => p.Name == "y"), iz = props.FindIndex(p => p.Name == "z");
            int ir = props.FindIndex(p => p.Name is "red" or "r" or "diffuse_red");
            int ig = props.FindIndex(p => p.Name is "green" or "g" or "diffuse_green");
            int ib = props.FindIndex(p => p.Name is "blue" or "b" or "diffuse_blue");
            if (name == "vertex") hasColor = ir >= 0 && ig >= 0 && ib >= 0;
            var vals = new double[props.Count];
            for (int e = 0; e < count; e++)
            {
                List<int>? list = null;
                for (int k = 0; k < props.Count; k++)
                {
                    var pr = props[k];
                    if (pr.CountType is not null)
                    {
                        int n = (int)reader.Next(pr.CountType);
                        bool isIndices = pr.Name is "vertex_indices" or "vertex_index";
                        if (isIndices) list = new List<int>(n);
                        for (int j = 0; j < n; j++)
                        {
                            var x = reader.Next(pr.Type);
                            if (isIndices) list!.Add((int)x);
                        }
                    }
                    else vals[k] = reader.Next(pr.Type);
                }
                if (name == "vertex")
                {
                    mesh.Vertices.Add(new Vec3(ix >= 0 ? vals[ix] : 0, iy >= 0 ? vals[iy] : 0, iz >= 0 ? vals[iz] : 0));
                    if (hasColor)
                    {
                        bool isFloat = props[ir].Type.StartsWith("float") || props[ir].Type.StartsWith("double");
                        double s = isFloat ? 1 : 1 / 255.0;
                        colors.Add(Rgb(vals[ir] * s, vals[ig] * s, vals[ib] * s));
                    }
                }
                else if (name == "face" && list is { Count: >= 3 })
                {
                    if (list.Any(i => i < 0 || i >= mesh.Vertices.Count)) continue;
                    for (int i = 1; i + 1 < list.Count; i++) { mesh.Indices.Add(list[0]); mesh.Indices.Add(list[i]); mesh.Indices.Add(list[i + 1]); }
                }
            }
        }
        if (hasColor) mesh.Colors = colors;
        return mesh;
    }

    private sealed class PlyReader
    {
        private readonly byte[] _d;
        private int _p;
        private readonly bool _ascii, _big;
        private string[] _tokens = [];
        private int _t;

        public PlyReader(byte[] d, int start, string format)
        {
            _d = d; _p = start;
            _ascii = format == "ascii";
            _big = format == "binary_big_endian";
        }

        public double Next(string type)
        {
            if (_ascii)
            {
                while (_t >= _tokens.Length)
                {
                    if (_p >= _d.Length) throw new InvalidDataException("el PLY se corta antes de tiempo");
                    int e = Array.IndexOf(_d, (byte)'\n', _p);
                    if (e < 0) e = _d.Length;
                    _tokens = Encoding.ASCII.GetString(_d, _p, e - _p).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    _t = 0;
                    _p = e + 1;
                }
                return D(_tokens[_t++]);
            }
            int size = type switch
            {
                "char" or "uchar" or "int8" or "uint8" => 1,
                "short" or "ushort" or "int16" or "uint16" => 2,
                "double" or "float64" => 8,
                _ => 4,
            };
            if (_p + size > _d.Length) throw new InvalidDataException("el PLY se corta antes de tiempo");
            var span = new ReadOnlySpan<byte>(_d, _p, size);
            Span<byte> b = stackalloc byte[8];
            span.CopyTo(b);
            if (_big != !BitConverter.IsLittleEndian) b[..size].Reverse();
            _p += size;
            return type switch
            {
                "char" or "int8" => (sbyte)b[0],
                "uchar" or "uint8" => b[0],
                "short" or "int16" => BitConverter.ToInt16(b),
                "ushort" or "uint16" => BitConverter.ToUInt16(b),
                "int" or "int32" => BitConverter.ToInt32(b),
                "uint" or "uint32" => BitConverter.ToUInt32(b),
                "double" or "float64" => BitConverter.ToDouble(b),
                _ => BitConverter.ToSingle(b),
            };
        }
    }

    // ------------------------------------------------------------------ glTF / GLB

    private static ScanMesh ReadGlb(byte[] data, Func<string, byte[]?> sibling)
    {
        if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46546C67) throw new InvalidDataException("no es un archivo GLB");
        string? json = null;
        byte[]? bin = null;
        int p = 12;
        while (p + 8 <= data.Length)
        {
            int len = BitConverter.ToInt32(data, p);
            uint type = BitConverter.ToUInt32(data, p + 4);
            if (p + 8 + len > data.Length) break;
            if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(data, p + 8, len);
            else if (type == 0x004E4942) bin = data[(p + 8)..(p + 8 + len)];
            p += 8 + len;
        }
        if (json is null) throw new InvalidDataException("el GLB no trae descripción JSON");
        return ReadGltf(json, bin, sibling);
    }

    private static ScanMesh ReadGltf(string json, byte[]? glbBin, Func<string, byte[]?> sibling)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("extensionsRequired", out var req) && req.EnumerateArray().Any(e => e.GetString() == "KHR_draco_mesh_compression"))
            throw new NotSupportedException("el GLB está comprimido con Draco. Expórtalo sin compresión o en OBJ.");

        var buffers = new List<byte[]>();
        if (root.TryGetProperty("buffers", out var bufs))
            foreach (var b in bufs.EnumerateArray())
            {
                if (b.TryGetProperty("uri", out var uri) && uri.GetString() is { } u)
                    buffers.Add(DataUri(u) ?? sibling(u) ?? throw new FileNotFoundException($"falta el archivo {u} que acompaña al glTF"));
                else buffers.Add(glbBin ?? []);
            }
        List<JsonElement> views = root.TryGetProperty("bufferViews", out var bv) ? bv.EnumerateArray().ToList() : [];
        List<JsonElement> accessors = root.TryGetProperty("accessors", out var ac) ? ac.EnumerateArray().ToList() : [];
        List<JsonElement> materials = root.TryGetProperty("materials", out var ma) ? ma.EnumerateArray().ToList() : [];
        List<JsonElement> textures = root.TryGetProperty("textures", out var tx) ? tx.EnumerateArray().ToList() : [];
        List<JsonElement> images = root.TryGetProperty("images", out var im) ? im.EnumerateArray().ToList() : [];
        List<JsonElement> meshes = root.TryGetProperty("meshes", out var me) ? me.EnumerateArray().ToList() : [];
        List<JsonElement> nodes = root.TryGetProperty("nodes", out var no) ? no.EnumerateArray().ToList() : [];
        var texCache = new Dictionary<int, Texture?>();

        byte[] View(int index, out int offset, out int stride)
        {
            var v = views[index];
            offset = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            stride = v.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
            return buffers![v.GetProperty("buffer").GetInt32()];
        }

        double[][] Read(int accessor)
        {
            var a = accessors[accessor];
            int count = a.GetProperty("count").GetInt32();
            int comps = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
            int ct = a.GetProperty("componentType").GetInt32();
            bool normalized = a.TryGetProperty("normalized", out var nz) && nz.GetBoolean();
            int csize = ct switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
            var result = new double[count][];
            if (!a.TryGetProperty("bufferView", out var bvi))
            {
                for (int i = 0; i < count; i++) result[i] = new double[comps];
                return result;
            }
            var buf = View(bvi.GetInt32(), out var off, out var stride);
            off += a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
            if (stride == 0) stride = csize * comps;
            for (int i = 0; i < count; i++)
            {
                var r = new double[comps];
                for (int k = 0; k < comps; k++)
                {
                    int q = off + i * stride + k * csize;
                    if (q + csize > buf.Length) throw new InvalidDataException("datos del glTF incompletos");
                    r[k] = ct switch
                    {
                        5120 => normalized ? Math.Max((sbyte)buf[q] / 127.0, -1) : (sbyte)buf[q],
                        5121 => normalized ? buf[q] / 255.0 : buf[q],
                        5122 => normalized ? Math.Max(BitConverter.ToInt16(buf, q) / 32767.0, -1) : BitConverter.ToInt16(buf, q),
                        5123 => normalized ? BitConverter.ToUInt16(buf, q) / 65535.0 : BitConverter.ToUInt16(buf, q),
                        5125 => BitConverter.ToUInt32(buf, q),
                        _ => BitConverter.ToSingle(buf, q),
                    };
                }
                result[i] = r;
            }
            return result;
        }

        Texture? Tex(int texture)
        {
            if (texCache.TryGetValue(texture, out var t)) return t;
            t = null;
            try
            {
                if (texture < textures.Count && textures[texture].TryGetProperty("source", out var src) && src.GetInt32() is var si && si < images.Count)
                {
                    var img = images[si];
                    byte[]? bytes = null;
                    if (img.TryGetProperty("bufferView", out var ibv))
                    {
                        var buf = View(ibv.GetInt32(), out var off, out _);
                        bytes = buf[off..(off + views[ibv.GetInt32()].GetProperty("byteLength").GetInt32())];
                    }
                    else if (img.TryGetProperty("uri", out var iu) && iu.GetString() is { } u) bytes = DataUri(u) ?? sibling(u);
                    if (bytes is not null) t = Texture.Decode(bytes);
                }
            }
            catch { t = null; }
            texCache[texture] = t;
            return t;
        }

        var mesh = new ScanMesh { Colors = new List<uint>() };
        bool anyColor = false;

        void AddMesh(int meshIndex, double[] m)
        {
            foreach (var prim in meshes[meshIndex].GetProperty("primitives").EnumerateArray())
            {
                int mode = prim.TryGetProperty("mode", out var md) ? md.GetInt32() : 4;
                if (mode != 4) continue; // solo triángulos
                var attrs = prim.GetProperty("attributes");
                if (!attrs.TryGetProperty("POSITION", out var pa)) continue;
                var pos = Read(pa.GetInt32());
                double[][]? col = attrs.TryGetProperty("COLOR_0", out var ca) ? Read(ca.GetInt32()) : null;
                double[][]? uv = attrs.TryGetProperty("TEXCOORD_0", out var ta) ? Read(ta.GetInt32()) : null;
                uint baseColor = 0xFFB9C0C7;
                Texture? tex = null;
                if (prim.TryGetProperty("material", out var mi) && mi.GetInt32() < materials.Count
                    && materials[mi.GetInt32()].TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var bcf))
                    {
                        var f = bcf.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                        if (f.Length >= 3) baseColor = Rgb(f[0], f[1], f[2]);
                    }
                    if (pbr.TryGetProperty("baseColorTexture", out var bct)) tex = Tex(bct.GetProperty("index").GetInt32());
                    anyColor = true;
                }
                if (col is not null || tex is not null) anyColor = true;
                int o = mesh.Vertices.Count;
                for (int i = 0; i < pos.Length; i++)
                {
                    var v = pos[i];
                    mesh.Vertices.Add(new Vec3(
                        m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12],
                        m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13],
                        m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14]));
                    uint c = tex is not null && uv is not null ? tex.Sample(uv[i][0], 1 - uv[i][1])
                        : col is not null ? Rgb(col[i][0], col[i][1], col[i][2]) : baseColor;
                    mesh.Colors!.Add(c);
                }
                if (prim.TryGetProperty("indices", out var ia))
                    foreach (var idx in Read(ia.GetInt32())) mesh.Indices.Add(o + (int)idx[0]);
                else
                    for (int i = 0; i + 2 < pos.Length; i += 3) { mesh.Indices.Add(o + i); mesh.Indices.Add(o + i + 1); mesh.Indices.Add(o + i + 2); }
                if (mesh.Indices.Count / 3 > MaxTriangles) throw new InvalidDataException(TooBig);
            }
        }

        void Visit(int node, double[] parent, int depth)
        {
            if (depth > 64 || node >= nodes.Count) return;
            var n = nodes[node];
            var m = Mul(parent, NodeMatrix(n));
            if (n.TryGetProperty("mesh", out var mi)) AddMesh(mi.GetInt32(), m);
            if (n.TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray()) Visit(c.GetInt32(), m, depth + 1);
        }

        var identity = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        IEnumerable<int> roots;
        if (root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
        {
            int si = root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0;
            roots = scenes[si].TryGetProperty("nodes", out var rn) ? rn.EnumerateArray().Select(x => x.GetInt32()).ToList() : [];
        }
        else roots = [];
        foreach (var r in roots) Visit(r, identity, 0);
        if (mesh.TriangleCount == 0) // sin escena: todas las mallas tal cual
            for (int i = 0; i < meshes.Count; i++) AddMesh(i, identity);
        if (!anyColor) mesh.Colors = null;
        return mesh;
    }

    /// <summary>Matriz 4×4 (por columnas, como glTF) de un nodo: «matrix» o traslación·rotación·escala.</summary>
    private static double[] NodeMatrix(JsonElement n)
    {
        if (n.TryGetProperty("matrix", out var mx)) return mx.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        double[] t = n.TryGetProperty("translation", out var tr) ? tr.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [0, 0, 0];
        double[] q = n.TryGetProperty("rotation", out var ro) ? ro.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [0, 0, 0, 1];
        double[] s = n.TryGetProperty("scale", out var sc) ? sc.EnumerateArray().Select(x => x.GetDouble()).ToArray() : [1, 1, 1];
        double x = q[0], y = q[1], z = q[2], w = q[3];
        return
        [
            (1 - 2 * (y * y + z * z)) * s[0], 2 * (x * y + z * w) * s[0], 2 * (x * z - y * w) * s[0], 0,
            2 * (x * y - z * w) * s[1], (1 - 2 * (x * x + z * z)) * s[1], 2 * (y * z + x * w) * s[1], 0,
            2 * (x * z + y * w) * s[2], 2 * (y * z - x * w) * s[2], (1 - 2 * (x * x + y * y)) * s[2], 0,
            t[0], t[1], t[2], 1,
        ];
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var r = new double[16];
        for (int c = 0; c < 4; c++)
            for (int row = 0; row < 4; row++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++) s += a[k * 4 + row] * b[c * 4 + k];
                r[c * 4 + row] = s;
            }
        return r;
    }

    private static byte[]? DataUri(string uri)
    {
        if (!uri.StartsWith("data:", StringComparison.Ordinal)) return null;
        int comma = uri.IndexOf(',');
        return comma < 0 ? null : Convert.FromBase64String(uri[(comma + 1)..]);
    }

    // ------------------------------------------------------------------ utilidades

    private const string TooBig = "el escaneo tiene demasiados triángulos (más de 6 millones). Expórtalo con menos detalle desde la app.";

    /// <summary>Textura decodificada con SkiaSharp, para pasar sus colores a los vértices.</summary>
    internal sealed class Texture
    {
        private readonly int _w, _h;
        private readonly uint[] _px;

        private Texture(int w, int h, uint[] px) { _w = w; _h = h; _px = px; }

        public static Texture? Decode(byte[] bytes)
        {
            try
            {
                using var bmp = SKBitmap.Decode(bytes);
                if (bmp is null || bmp.Width == 0 || bmp.Height == 0) return null;
                var px = bmp.Pixels.Select(c => 0xFF000000 | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue).ToArray();
                return new Texture(bmp.Width, bmp.Height, px);
            }
            catch { return null; }
        }

        /// <summary>Color en (u, v) con v hacia arriba (convención OBJ); la textura se repite fuera de [0, 1].</summary>
        public uint Sample(double u, double v)
        {
            u -= Math.Floor(u);
            v -= Math.Floor(v);
            int x = Math.Clamp((int)(u * _w), 0, _w - 1);
            int y = Math.Clamp((int)((1 - v) * _h), 0, _h - 1);
            return _px[y * _w + x];
        }
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> needle)
    {
        int limit = Math.Min(data.Length, 64 * 1024);
        return new ReadOnlySpan<byte>(data, 0, limit).IndexOf(needle);
    }

    private static int Idx(string s, int count)
    {
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i == 0) return -1;
        return i < 0 ? count + i : i - 1;
    }

    private static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    internal static uint Rgb(double r, double g, double b)
    {
        static uint C(double x) => (uint)Math.Clamp(Math.Round(x * 255), 0, 255);
        return 0xFF000000 | (C(r) << 16) | (C(g) << 8) | C(b);
    }
}
