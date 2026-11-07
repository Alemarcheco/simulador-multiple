using System.Globalization;

namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Lee archivos Intel HEX (los que genera el compilador de Arduino) a palabras de programa.</summary>
public static class IntelHex
{
    /// <summary>Convierte el texto .hex en palabras de 16 bits. Lanza <see cref="FormatException"/> si está mal.</summary>
    public static ushort[] Parse(string text, int maxBytes = 256 * 1024)
    {
        var bytes = new byte[maxBytes];
        int used = 0, lineNo = 0, upper = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':' || line.Length < 11) throw new FormatException($"Línea {lineNo} del .hex no válida.");
            int count = Hex(line, 1), address = (Hex(line, 3) << 8) | Hex(line, 5), type = Hex(line, 7);
            if (line.Length < 11 + 2 * count) throw new FormatException($"Línea {lineNo} del .hex incompleta.");
            int sum = count + (address >> 8) + (address & 0xff) + type;
            for (int i = 0; i <= count; i++) sum += Hex(line, 9 + 2 * i);
            if ((sum & 0xff) != 0) throw new FormatException($"Suma de control incorrecta en la línea {lineNo} del .hex.");
            switch (type)
            {
                case 0:
                    for (int i = 0; i < count; i++)
                    {
                        int at = upper + address + i;
                        if (at >= bytes.Length) throw new FormatException("El programa no cabe en la memoria del microcontrolador.");
                        bytes[at] = (byte)Hex(line, 9 + 2 * i);
                        used = Math.Max(used, at + 1);
                    }
                    break;
                case 1:
                    return ToWords(bytes, used);
                case 2:
                    upper = ((Hex(line, 9) << 8) | Hex(line, 11)) << 4;
                    break;
                case 4:
                    upper = ((Hex(line, 9) << 8) | Hex(line, 11)) << 16;
                    break;
            }
        }
        return ToWords(bytes, used);
    }

    private static int Hex(string s, int at) => int.Parse(s.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static ushort[] ToWords(byte[] bytes, int used)
    {
        var words = new ushort[(used + 1) / 2];
        for (int i = 0; i < words.Length; i++) words[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
        return words;
    }
}
