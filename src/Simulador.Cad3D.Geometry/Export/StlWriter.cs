using System.Globalization;
using System.Text;

namespace Simulador.Cad3D.Geometry.Export;

/// <summary>Escribe mallas en STL (binario por defecto, el que esperan todos los laminadores). Unidades: mm.</summary>
public static class StlWriter
{
    public static void WriteBinary(Stream stream, IEnumerable<Mesh> meshes, string name = "Simulador multiple")
    {
        var tris = meshes.SelectMany(m => m.Triangles()).ToList();
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var header = new byte[80];
        Encoding.ASCII.GetBytes(name.Length > 79 ? name[..79] : name).CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)tris.Count);
        foreach (var (a, b, c) in tris)
        {
            var n = (b - a).Cross(c - a).Normalized();
            WriteVec(w, n); WriteVec(w, a); WriteVec(w, b); WriteVec(w, c);
            w.Write((ushort)0);
        }
    }

    public static void WriteAscii(TextWriter w, IEnumerable<Mesh> meshes, string name = "pieza")
    {
        var ci = CultureInfo.InvariantCulture;
        w.WriteLine($"solid {name}");
        foreach (var (a, b, c) in meshes.SelectMany(m => m.Triangles()))
        {
            var n = (b - a).Cross(c - a).Normalized();
            w.WriteLine(string.Format(ci, "  facet normal {0:e6} {1:e6} {2:e6}", n.X, n.Y, n.Z));
            w.WriteLine("    outer loop");
            foreach (var v in new[] { a, b, c }) w.WriteLine(string.Format(ci, "      vertex {0:e6} {1:e6} {2:e6}", v.X, v.Y, v.Z));
            w.WriteLine("    endloop");
            w.WriteLine("  endfacet");
        }
        w.WriteLine($"endsolid {name}");
    }

    public static void Save(string path, IEnumerable<Mesh> meshes)
    {
        using var fs = File.Create(path);
        WriteBinary(fs, meshes, Path.GetFileNameWithoutExtension(path));
    }

    private static void WriteVec(BinaryWriter w, Vec3 v)
    {
        w.Write((float)v.X); w.Write((float)v.Y); w.Write((float)v.Z);
    }
}
