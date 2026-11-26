using System.IO.Compression;
using System.Text;

namespace Simulador.Cad3D.Geometry.Scan;

/// <summary>
/// Lee la geometría de los archivos USDZ de Apple (los que crean Object Capture, ARKit y las apps de escaneo del
/// iPhone): un ZIP sin comprimir con una capa USD binaria («crate», .usdc) y sus texturas. Solo saca lo necesario para
/// una pieza escaneada: las mallas (puntos, caras, coordenadas de textura), sus transformaciones, las unidades
/// (metersPerUnit), el eje vertical (upAxis) y la textura de color.
/// </summary>
public static class UsdReader
{
    public sealed record Result(ScanMesh Mesh, double MetersPerUnit, bool ZUp);

    public static Result ReadUsdz(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? first = null;
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/') || e.Length > 512L * 1024 * 1024) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            files[e.FullName] = ms.ToArray();
            var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
            if (first is null && ext is ".usdc" or ".usd" or ".usda") first = e.FullName;
        }
        if (first is null) throw new InvalidDataException("el USDZ no trae ninguna capa USD");
        var layer = files[first];
        if (!IsCrate(layer))
            throw new NotSupportedException("el USDZ trae USD de texto (.usda); expórtalo en OBJ o GLB.");
        byte[]? Sibling(string name)
        {
            var n = name.Replace('\\', '/').TrimStart('.', '/');
            if (files.TryGetValue(n, out var d)) return d;
            var baseDir = Path.GetDirectoryName(first)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(baseDir) && files.TryGetValue(baseDir + "/" + n, out d)) return d;
            return files.FirstOrDefault(f => Path.GetFileName(f.Key).Equals(Path.GetFileName(n), StringComparison.OrdinalIgnoreCase)).Value;
        }
        var result = ReadCrate(layer, Sibling);
        // Sin textura referenciada en el USD, usa la imagen de color que acompañe al paquete.
        return result;
    }

    public static bool IsCrate(byte[] d) => d.Length > 88 && Encoding.ASCII.GetString(d, 0, 8) == "PXR-USDC";

    // ------------------------------------------------------------------ crate

    private sealed class Crate
    {
        public byte[] D = [];
        public (int Major, int Minor) Version;
        public string[] Tokens = [];
        public int[] Strings = [];
        public (int Token, ulong Rep)[] Fields = [];
        public int[] FieldSets = [];
        public string[] Paths = [];
        public (int Path, int FieldSet, int Type)[] Specs = [];
    }

    private const int SpecAttribute = 1, SpecPrim = 6, SpecPseudoRoot = 7, SpecRelationship = 8;

    public static Result ReadCrate(byte[] d, Func<string, byte[]?> sibling)
    {
        var c = new Crate { D = d, Version = (d[8], d[9]) };
        if (c.Version.Major == 0 && c.Version.Minor < 4) throw new NotSupportedException("versión de USD binario demasiado antigua");
        long toc = BitConverter.ToInt64(d, 16);
        long nsec = BitConverter.ToInt64(d, (int)toc);
        var sections = new Dictionary<string, (long Start, long Size)>();
        for (int i = 0; i < nsec; i++)
        {
            int o = (int)(toc + 8 + i * 32);
            var name = Encoding.ASCII.GetString(d, o, 16).TrimEnd('\0');
            sections[name] = (BitConverter.ToInt64(d, o + 16), BitConverter.ToInt64(d, o + 24));
        }
        long Sec(string n) => sections.TryGetValue(n, out var s) ? s.Start : throw new InvalidDataException($"falta la sección {n} del USD");

        // TOKENS
        {
            int p = (int)Sec("TOKENS");
            long n = U64(d, p); long raw = U64(d, p + 8); long comp = U64(d, p + 16);
            var bytes = FastDecompress(d, p + 24, (int)comp, (int)raw);
            var list = new List<string>((int)n);
            int s = 0;
            for (int i = 0; i < bytes.Length && list.Count < n; i++)
                if (bytes[i] == 0) { list.Add(Encoding.UTF8.GetString(bytes, s, i - s)); s = i + 1; }
            c.Tokens = list.ToArray();
        }
        // STRINGS
        if (sections.ContainsKey("STRINGS"))
        {
            int p = (int)Sec("STRINGS");
            long n = U64(d, p);
            c.Strings = new int[n];
            for (int i = 0; i < n; i++) c.Strings[i] = BitConverter.ToInt32(d, p + 8 + i * 4);
        }
        // FIELDS
        {
            int p = (int)Sec("FIELDS");
            int n = (int)U64(d, p); p += 8;
            var tok = ReadCompressedInts(d, ref p, n);
            long comp = U64(d, p); p += 8;
            var reps = FastDecompress(d, p, (int)comp, n * 8);
            c.Fields = new (int, ulong)[n];
            for (int i = 0; i < n; i++) c.Fields[i] = (tok[i], BitConverter.ToUInt64(reps, i * 8));
        }
        // FIELDSETS
        {
            int p = (int)Sec("FIELDSETS");
            int n = (int)U64(d, p); p += 8;
            c.FieldSets = ReadCompressedInts(d, ref p, n);
        }
        // PATHS
        {
            int p = (int)Sec("PATHS");
            int n = (int)U64(d, p); p += 8;
            int enc = (int)U64(d, p); p += 8;
            var pathIdx = ReadCompressedInts(d, ref p, enc);
            var elem = ReadCompressedInts(d, ref p, enc);
            var jumps = ReadCompressedInts(d, ref p, enc);
            c.Paths = new string[n];
            if (enc > 0) BuildPaths(c, pathIdx, elem, jumps, 0, null);
        }
        // SPECS
        {
            int p = (int)Sec("SPECS");
            int n = (int)U64(d, p); p += 8;
            var pi = ReadCompressedInts(d, ref p, n);
            var fs = ReadCompressedInts(d, ref p, n);
            var ty = ReadCompressedInts(d, ref p, n);
            c.Specs = new (int, int, int)[n];
            for (int i = 0; i < n; i++) c.Specs[i] = (pi[i], fs[i], ty[i]);
        }
        return Extract(c, sibling);
    }

    private static void BuildPaths(Crate c, int[] pathIdx, int[] elem, int[] jumps, int cur, string? parent)
    {
        bool hasChild, hasSibling;
        do
        {
            int i = cur++;
            if (parent is null)
            {
                parent = "/";
                c.Paths[pathIdx[i]] = parent;
            }
            else
            {
                int t = elem[i];
                bool prop = t < 0;
                var name = c.Tokens[Math.Abs(t)];
                c.Paths[pathIdx[i]] = prop ? parent + "." + name : (parent == "/" ? "/" + name : parent + "/" + name);
            }
            hasChild = jumps[i] > 0 || jumps[i] == -1;
            hasSibling = jumps[i] >= 0;
            if (hasChild)
            {
                if (hasSibling) BuildPaths(c, pathIdx, elem, jumps, i + jumps[i], parent);
                parent = c.Paths[pathIdx[i]];
            }
        } while (hasChild || hasSibling);
    }

    // ------------------------------------------------------------------ de las especificaciones a la malla

    private sealed class Spec
    {
        public string Path = "";
        public int Type;
        public Dictionary<string, ulong> Fields = new();
    }

    private static Result Extract(Crate c, Func<string, byte[]?> sibling)
    {
        var specs = new Dictionary<string, Spec>();
        foreach (var (path, fs, type) in c.Specs)
        {
            var s = new Spec { Path = c.Paths[path] ?? "", Type = type };
            for (int i = fs; i < c.FieldSets.Length && c.FieldSets[i] >= 0; i++)
            {
                var (tok, rep) = c.Fields[c.FieldSets[i]];
                s.Fields[c.Tokens[tok]] = rep;
            }
            specs[s.Path] = s;
        }
        double mpu = 0.01; // valor por omisión de USD (centímetros)
        bool zUp = false;
        if (specs.TryGetValue("/", out var rootSpec))
        {
            if (rootSpec.Fields.TryGetValue("metersPerUnit", out var r) && Scalar(c, r) is { } m && m > 0) mpu = m;
            if (rootSpec.Fields.TryGetValue("upAxis", out var u) && TokenValue(c, u) == "Z") zUp = true;
        }

        // Textura de color: entre las «inputs:file» de los sombreadores, la que no es normal, rugosidad, etc.
        ReaderTexture? texture = null;
        var files = specs.Values.Where(s => s.Type == SpecAttribute && s.Path.EndsWith(".inputs:file") && s.Fields.ContainsKey("default"))
            .Select(s => (s.Path, File: TokenValue(c, s.Fields["default"]) ?? "")).Where(x => x.File.Length > 0).ToList();
        string[] bad = ["norm", "rough", "metal", "occl", "_ao", "ao0", "opacity", "disp", "height", "emiss"];
        var pick = files.OrderBy(f => Score(f.Path + " " + f.File)).FirstOrDefault(f => !bad.Any(b => (f.Path + f.File).ToLowerInvariant().Contains(b)));
        if (pick.File is { Length: > 0 } && sibling(pick.File) is { } img) texture = ReaderTexture.Decode(img);

        var mesh = new ScanMesh();
        bool anyColor = texture is not null;
        if (anyColor) mesh.Colors = new List<uint>();
        foreach (var prim in specs.Values.Where(s => s.Type == SpecPrim && s.Fields.TryGetValue("typeName", out var tn) && TokenValue(c, tn) == "Mesh"))
        {
            var pts = Vec3fArray(c, specs, prim.Path + ".points");
            var counts = IntArray(c, specs, prim.Path + ".faceVertexCounts");
            var idx = IntArray(c, specs, prim.Path + ".faceVertexIndices");
            if (pts is null || idx is null) continue;
            counts ??= Enumerable.Repeat(3, idx.Length / 3).ToArray();
            var world = WorldMatrix(c, specs, prim.Path);
            // Coordenadas de textura: el primer primvar de dos componentes.
            double[][]? st = null;
            int[]? stIdx = null;
            if (texture is not null)
            {
                var stSpec = specs.Values.FirstOrDefault(s => s.Type == SpecAttribute && s.Path.StartsWith(prim.Path + ".primvars:")
                    && !s.Path.EndsWith(":indices") && s.Fields.TryGetValue("default", out var rep) && RepType(rep) is 20 or 19 or 21);
                if (stSpec is not null)
                {
                    st = Vec2Array(c, stSpec.Fields["default"]);
                    stIdx = IntArray(c, specs, stSpec.Path + ":indices");
                }
            }
            int o = mesh.Vertices.Count;
            foreach (var p in pts) mesh.Vertices.Add(Apply(world, p));
            var acc = anyColor ? new (double R, double G, double B, int N)[pts.Length] : null;
            int corner = 0;
            foreach (var n in counts)
            {
                if (corner + n > idx.Length) break;
                for (int k = 1; k + 1 < n; k++)
                {
                    int a = idx[corner], b = idx[corner + k], cc = idx[corner + k + 1];
                    if (a >= pts.Length || b >= pts.Length || cc >= pts.Length) continue;
                    mesh.Indices.Add(o + a); mesh.Indices.Add(o + b); mesh.Indices.Add(o + cc);
                }
                if (acc is not null && st is not null)
                    for (int k = 0; k < n; k++)
                    {
                        int fv = corner + k, v = idx[fv];
                        int si = stIdx is not null && fv < stIdx.Length ? stIdx[fv] : st.Length == idx.Length ? fv : v;
                        if (si < 0 || si >= st.Length || v >= pts.Length) continue;
                        uint col = texture!.Sample(st[si][0], st[si][1]);
                        var x = acc[v];
                        acc[v] = (x.R + ((col >> 16) & 0xFF), x.G + ((col >> 8) & 0xFF), x.B + (col & 0xFF), x.N + 1);
                    }
                corner += n;
            }
            if (acc is not null)
                foreach (var x in acc)
                    mesh.Colors!.Add(x.N == 0 ? 0xFFB9C0C7 : 0xFF000000 | ((uint)(x.R / x.N) << 16) | ((uint)(x.G / x.N) << 8) | (uint)(x.B / x.N));
        }
        if (mesh.TriangleCount == 0) throw new InvalidDataException("el USD no tiene ninguna malla");
        return new Result(mesh, mpu, zUp);
    }

    private static int Score(string s)
    {
        s = s.ToLowerInvariant();
        return s.Contains("diffuse") || s.Contains("albedo") || s.Contains("basecolor") || s.Contains("base_color") ? 0
            : s.Contains("tex0") || s.Contains("color") ? 1 : 2;
    }

    // Matriz 4×4 por filas (convención USD: punto fila × matriz).
    private static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Mul(double[] a, double[] b)
    {
        var r = new double[16];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++) s += a[i * 4 + k] * b[k * 4 + j];
                r[i * 4 + j] = s;
            }
        return r;
    }

    private static Vec3 Apply(double[] m, Vec3 p) => new(
        p.X * m[0] + p.Y * m[4] + p.Z * m[8] + m[12],
        p.X * m[1] + p.Y * m[5] + p.Z * m[9] + m[13],
        p.X * m[2] + p.Y * m[6] + p.Z * m[10] + m[14]);

    /// <summary>Matriz local→mundo: las xformOp de la prim y de sus padres (traslación, giro, escala, matriz).</summary>
    private static double[] WorldMatrix(Crate c, Dictionary<string, Spec> specs, string primPath)
    {
        var chain = new List<string>();
        for (var p = primPath; p.Length > 1; p = p[..Math.Max(1, p.LastIndexOf('/'))]) chain.Add(p);
        var world = Identity();
        foreach (var p in chain) world = Mul(world, LocalMatrix(c, specs, p)); // hijo × padre
        return world;
    }

    private static double[] LocalMatrix(Crate c, Dictionary<string, Spec> specs, string prim)
    {
        List<string> order;
        if (specs.TryGetValue(prim + ".xformOpOrder", out var os) && os.Fields.TryGetValue("default", out var orep)) order = TokenArray(c, orep);
        else order = specs.Keys.Where(k => k.StartsWith(prim + ".xformOp:") && !k[(prim.Length + 1)..].Contains('.')).Select(k => k[(prim.Length + 1)..]).ToList();
        var m = Identity();
        // USD aplica la lista de derecha a izquierda sobre el punto: con vectores fila, M = op[n-1] × … × op[0].
        foreach (var raw in order)
        {
            bool inverse = raw.StartsWith("!invert!");
            var name = inverse ? raw[8..] : raw;
            if (!specs.TryGetValue(prim + "." + name, out var s) || !s.Fields.TryGetValue("default", out var rep)) continue;
            var op = OpMatrix(c, name, rep);
            if (op is null) continue;
            m = Mul(op, m);
        }
        return m;
    }

    private static double[]? OpMatrix(Crate c, string name, ulong rep)
    {
        var kind = name["xformOp:".Length..].Split(':')[0];
        var v = Doubles(c, rep);
        if (v is null) return null;
        switch (kind)
        {
            case "transform" when v.Length == 16: return v;
            case "translate" when v.Length >= 3: { var m = Identity(); m[12] = v[0]; m[13] = v[1]; m[14] = v[2]; return m; }
            case "scale" when v.Length >= 3: { var m = Identity(); m[0] = v[0]; m[5] = v[1]; m[10] = v[2]; return m; }
            case "orient" when v.Length >= 4:
            {
                // GfQuat se guarda como (imaginaria x, y, z, real w).
                double x = v[0], y = v[1], z = v[2], w = v[3];
                return
                [
                    1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                    2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                    2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                    0, 0, 0, 1,
                ];
            }
            case "rotateX" or "rotateY" or "rotateZ" when v.Length >= 1:
                return Rot(kind[^1], v[0]);
            case "rotateXYZ" when v.Length >= 3:
                return Mul(Mul(Rot('X', v[0]), Rot('Y', v[1])), Rot('Z', v[2]));
            default: return null;
        }
    }

    private static double[] Rot(char axis, double deg)
    {
        double a = deg * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
        return axis switch
        {
            'X' => [1, 0, 0, 0, 0, cs, sn, 0, 0, -sn, cs, 0, 0, 0, 0, 1],
            'Y' => [cs, 0, -sn, 0, 0, 1, 0, 0, sn, 0, cs, 0, 0, 0, 0, 1],
            _ => [cs, sn, 0, 0, -sn, cs, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
        };
    }

    // ------------------------------------------------------------------ valores

    private const ulong IsArrayBit = 1UL << 63, IsInlinedBit = 1UL << 62, IsCompressedBit = 1UL << 61;
    private static int RepType(ulong rep) => (int)((rep >> 48) & 0xFF);
    private static long Payload(ulong rep) => (long)(rep & 0xFFFFFFFFFFFFUL);

    private static string? TokenValue(Crate c, ulong rep)
    {
        int t = RepType(rep);
        if ((rep & IsInlinedBit) == 0) return null;
        int i = (int)Payload(rep);
        return t switch
        {
            11 or 12 => i < c.Tokens.Length ? c.Tokens[i] : null, // token o ruta de archivo (asset path)
            10 => i < c.Strings.Length && c.Strings[i] < c.Tokens.Length ? c.Tokens[c.Strings[i]] : null,
            _ => null,
        };
    }

    private static double? Scalar(Crate c, ulong rep)
    {
        int t = RepType(rep);
        long p = Payload(rep);
        if ((rep & IsInlinedBit) != 0)
            return t switch
            {
                8 => BitConverter.Int32BitsToSingle((int)p),
                9 => BitConverter.Int32BitsToSingle((int)p), // un double que cabe en float se guarda como float
                3 or 4 or 5 or 6 => p,
                _ => null,
            };
        if (t == 9) return BitConverter.ToDouble(c.D, (int)p);
        return null;
    }

    /// <summary>Valor numérico pequeño (vector, cuaternión o matriz) como lista de dobles.</summary>
    private static double[]? Doubles(Crate c, ulong rep)
    {
        int t = RepType(rep);
        if ((rep & IsArrayBit) != 0) return null;
        int comps = t switch { 15 => 16, 16 or 17 or 18 or 27 or 28 or 29 or 30 => 4, 23 or 24 or 25 or 26 => 3, 8 or 9 or 7 => 1, _ => 0 };
        if (comps == 0) return null;
        long p = Payload(rep);
        if ((rep & IsInlinedBit) != 0)
        {
            if (comps == 1) return Scalar(c, rep) is { } s ? [s] : null;
            if (t == 15)
            {
                // Matriz diagonal en línea: 4 valores int8.
                var m = new double[16];
                for (int i = 0; i < 4; i++) m[i * 5] = (sbyte)((p >> (8 * i)) & 0xFF);
                return m;
            }
            var r = new double[comps];
            for (int i = 0; i < comps; i++) r[i] = (sbyte)((p >> (8 * i)) & 0xFF);
            return r;
        }
        int size = t switch { 15 or 16 or 23 or 27 => 8, 18 or 25 or 29 => 2, _ => 4 };
        var res = new double[comps];
        for (int i = 0; i < comps; i++) res[i] = ReadNum(c.D, (int)p + i * size, t, size);
        return res;
    }

    private static double ReadNum(byte[] d, int o, int t, int size) => size switch
    {
        8 => BitConverter.ToDouble(d, o),
        2 => (double)BitConverter.ToHalf(d, o),
        _ => t is 26 or 30 or 3 ? BitConverter.ToInt32(d, o) : BitConverter.ToSingle(d, o),
    };

    private static ulong? DefaultRep(Dictionary<string, Spec> specs, string path) =>
        specs.TryGetValue(path, out var s) && s.Fields.TryGetValue("default", out var r) ? r : null;

    private static int ArrayStart(Crate c, ulong rep, out long count)
    {
        int p = (int)Payload(rep);
        if (c.Version.Major > 0 || c.Version.Minor >= 7) { count = U64(c.D, p); return p + 8; }
        count = BitConverter.ToUInt32(c.D, p);
        return p + 4;
    }

    private static int[]? IntArray(Crate c, Dictionary<string, Spec> specs, string path) =>
        DefaultRep(specs, path) is { } rep ? IntArray(c, rep) : null;

    private static int[]? IntArray(Crate c, ulong rep)
    {
        if ((rep & IsArrayBit) == 0 || RepType(rep) is not (3 or 4)) return null;
        if (Payload(rep) == 0) return [];
        int p = ArrayStart(c, rep, out var n);
        if ((rep & IsCompressedBit) != 0 && n >= 16) return ReadCompressedInts(c.D, ref p, (int)n);
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = BitConverter.ToInt32(c.D, p + i * 4);
        return r;
    }

    private static Vec3[]? Vec3fArray(Crate c, Dictionary<string, Spec> specs, string path)
    {
        if (DefaultRep(specs, path) is not { } rep || (rep & IsArrayBit) == 0) return null;
        int t = RepType(rep);
        if (t is not (23 or 24 or 25)) return null;
        if (Payload(rep) == 0) return [];
        int p = ArrayStart(c, rep, out var n);
        int size = t == 23 ? 8 : t == 25 ? 2 : 4;
        var r = new Vec3[n];
        for (int i = 0; i < n; i++)
        {
            int o = p + i * size * 3;
            r[i] = new Vec3(ReadNum(c.D, o, t, size), ReadNum(c.D, o + size, t, size), ReadNum(c.D, o + 2 * size, t, size));
        }
        return r;
    }

    private static double[][]? Vec2Array(Crate c, ulong rep)
    {
        if ((rep & IsArrayBit) == 0 || Payload(rep) == 0) return null;
        int t = RepType(rep);
        int p = ArrayStart(c, rep, out var n);
        int size = t == 19 ? 8 : t == 21 ? 2 : 4;
        var r = new double[n][];
        for (int i = 0; i < n; i++) r[i] = [ReadNum(c.D, p + i * size * 2, t, size), ReadNum(c.D, p + i * size * 2 + size, t, size)];
        return r;
    }

    private static List<string> TokenArray(Crate c, ulong rep)
    {
        var list = new List<string>();
        int t = RepType(rep);
        if ((rep & IsInlinedBit) != 0 || Payload(rep) == 0) return list;
        if (t == 41 || ((rep & IsArrayBit) != 0 && t == 11))
        {
            int p = t == 41 ? (int)Payload(rep) + 8 : ArrayStart(c, rep, out _);
            long n = t == 41 ? U64(c.D, (int)Payload(rep)) : (c.Version.Major > 0 || c.Version.Minor >= 7 ? U64(c.D, (int)Payload(rep)) : BitConverter.ToUInt32(c.D, (int)Payload(rep)));
            for (int i = 0; i < n; i++)
            {
                int ti = BitConverter.ToInt32(c.D, p + i * 4);
                if (ti >= 0 && ti < c.Tokens.Length) list.Add(c.Tokens[ti]);
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ compresión

    private static long U64(byte[] d, int o) => BitConverter.ToInt64(d, o);

    private static int[] ReadCompressedInts(byte[] d, ref int p, int count)
    {
        long comp = U64(d, p);
        p += 8;
        var r = DecompressInts(d, p, (int)comp, count);
        p += (int)comp;
        return r;
    }

    /// <summary>Enteros de 32 bits comprimidos por USD: diferencias codificadas con 2 bits de tamaño, y luego LZ4.</summary>
    internal static int[] DecompressInts(byte[] d, int offset, int size, int count)
    {
        var result = new int[count];
        if (count == 0) return result;
        int encodedSize = 4 + (count * 2 + 7) / 8 + count * 4;
        var buf = FastDecompress(d, offset, size, encodedSize);
        int common = BitConverter.ToInt32(buf, 0);
        int codes = 4;
        int vals = codes + (count * 2 + 7) / 8;
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            int code = (buf[codes + i / 4] >> (2 * (i % 4))) & 3;
            int delta;
            switch (code)
            {
                case 0: delta = common; break;
                case 1: delta = (sbyte)buf[vals]; vals += 1; break;
                case 2: delta = BitConverter.ToInt16(buf, vals); vals += 2; break;
                default: delta = BitConverter.ToInt32(buf, vals); vals += 4; break;
            }
            prev += delta;
            result[i] = prev;
        }
        return result;
    }

    /// <summary>TfFastCompression de USD: un byte con el número de trozos y bloques LZ4.</summary>
    internal static byte[] FastDecompress(byte[] d, int offset, int size, int maxOut)
    {
        var output = new byte[Math.Max(maxOut, 0)];
        int chunks = d[offset];
        int p = offset + 1;
        int written = 0;
        if (chunks == 0) written = Lz4Block(d, p, size - 1, output, 0);
        else
            for (int i = 0; i < chunks; i++)
            {
                int cs = BitConverter.ToInt32(d, p);
                p += 4;
                written += Lz4Block(d, p, cs, output, written);
                p += cs;
            }
        return written == output.Length ? output : output[..written];
    }

    /// <summary>Descompresión de un bloque LZ4 (formato de bloque, sin cabecera de marco).</summary>
    internal static int Lz4Block(byte[] src, int sp, int len, byte[] dst, int dp)
    {
        int end = sp + len, start = dp;
        while (sp < end)
        {
            int token = src[sp++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[sp++]; lit += b; } while (b == 255); }
            Array.Copy(src, sp, dst, dp, lit);
            sp += lit; dp += lit;
            if (sp >= end) break;
            int off = src[sp] | (src[sp + 1] << 8);
            sp += 2;
            int ml = token & 15;
            if (ml == 15) { int b; do { b = src[sp++]; ml += b; } while (b == 255); }
            ml += 4;
            int from = dp - off;
            if (off == 0 || from < 0) throw new InvalidDataException("datos LZ4 dañados en el USD");
            for (int i = 0; i < ml; i++) dst[dp++] = dst[from + i];
        }
        return dp - start;
    }

    /// <summary>Textura para pasar colores a los vértices (misma que en <see cref="ScanReader"/>).</summary>
    private sealed class ReaderTexture
    {
        private readonly ScanReader.Texture _t;
        private ReaderTexture(ScanReader.Texture t) { _t = t; }
        public static ReaderTexture? Decode(byte[] b) => ScanReader.Texture.Decode(b) is { } t ? new ReaderTexture(t) : null;
        public uint Sample(double u, double v) => _t.Sample(u, v);
    }
}
