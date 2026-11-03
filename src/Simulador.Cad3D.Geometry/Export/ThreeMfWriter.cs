using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Simulador.Cad3D.Geometry.Export;

/// <summary>
/// Escribe un archivo 3MF (núcleo de la especificación 3MF Consortium): un objeto por pieza, con nombre y color,
/// colocadas en la bandeja. Unidades: mm. Lo abren Cura, PrusaSlicer, OrcaSlicer y Bambu Studio.
/// </summary>
public static class ThreeMfWriter
{
    public sealed record Item(string Name, Mesh Mesh, string ColorHex = "#4A90D9");

    private const string CoreNs = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    public static void Write(Stream stream, IReadOnlyList<Item> items)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>" +
            "</Types>");
        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/>" +
            "</Relationships>");

        var entry = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.Optimal);
        using var es = entry.Open();
        using var xw = XmlWriter.Create(es, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        var ci = CultureInfo.InvariantCulture;
        xw.WriteStartDocument();
        xw.WriteStartElement("model", CoreNs);
        xw.WriteAttributeString("unit", "millimeter");
        xw.WriteAttributeString("xml", "lang", null, "es-ES");
        xw.WriteStartElement("metadata", CoreNs);
        xw.WriteAttributeString("name", "Application");
        xw.WriteString("Simulador multiple");
        xw.WriteEndElement();

        xw.WriteStartElement("resources", CoreNs);
        // Materiales base (un color por pieza), id 1.
        xw.WriteStartElement("basematerials", CoreNs);
        xw.WriteAttributeString("id", "1");
        foreach (var it in items)
        {
            xw.WriteStartElement("base", CoreNs);
            xw.WriteAttributeString("name", it.Name);
            xw.WriteAttributeString("displaycolor", NormalizeColor(it.ColorHex));
            xw.WriteEndElement();
        }
        xw.WriteEndElement();

        for (int i = 0; i < items.Count; i++)
        {
            var mesh = items[i].Mesh;
            xw.WriteStartElement("object", CoreNs);
            xw.WriteAttributeString("id", (i + 2).ToString(ci));
            xw.WriteAttributeString("type", "model");
            xw.WriteAttributeString("name", items[i].Name);
            xw.WriteAttributeString("pid", "1");
            xw.WriteAttributeString("pindex", i.ToString(ci));
            xw.WriteStartElement("mesh", CoreNs);
            xw.WriteStartElement("vertices", CoreNs);
            foreach (var v in mesh.Vertices)
            {
                xw.WriteStartElement("vertex", CoreNs);
                xw.WriteAttributeString("x", v.X.ToString("0.######", ci));
                xw.WriteAttributeString("y", v.Y.ToString("0.######", ci));
                xw.WriteAttributeString("z", v.Z.ToString("0.######", ci));
                xw.WriteEndElement();
            }
            xw.WriteEndElement();
            xw.WriteStartElement("triangles", CoreNs);
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                xw.WriteStartElement("triangle", CoreNs);
                xw.WriteAttributeString("v1", mesh.Indices[t].ToString(ci));
                xw.WriteAttributeString("v2", mesh.Indices[t + 1].ToString(ci));
                xw.WriteAttributeString("v3", mesh.Indices[t + 2].ToString(ci));
                xw.WriteEndElement();
            }
            xw.WriteEndElement();
            xw.WriteEndElement(); // mesh
            xw.WriteEndElement(); // object
        }
        xw.WriteEndElement(); // resources

        xw.WriteStartElement("build", CoreNs);
        for (int i = 0; i < items.Count; i++)
        {
            xw.WriteStartElement("item", CoreNs);
            xw.WriteAttributeString("objectid", (i + 2).ToString(ci));
            xw.WriteEndElement();
        }
        xw.WriteEndElement();
        xw.WriteEndElement(); // model
        xw.WriteEndDocument();
    }

    public static void Save(string path, IReadOnlyList<Item> items)
    {
        using var fs = File.Create(path);
        Write(fs, items);
    }

    private static string NormalizeColor(string hex)
    {
        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return hex.Length == 7 ? hex.ToUpperInvariant() + "FF" : hex.ToUpperInvariant();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }
}
