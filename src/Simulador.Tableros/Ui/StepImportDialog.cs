using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Core.Rendering;
using Simulador.Tableros.Import;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Ui;

/// <summary>Qué hacer con un modelo STEP después de revisarlo.</summary>
internal enum StepDialogResult { Save, SaveAll, Skip, Cancel }

/// <summary>
/// Revisa un modelo STEP del fabricante antes de guardarlo en la librería: marca, referencia, tipo y hacia dónde mira
/// su frente, con una vista previa tal como quedará en la placa (o en la puerta).
/// </summary>
internal sealed class StepImportDialog : Window
{
    private const int PreviewW = 360, PreviewH = 270;
    private readonly StepImporter.Candidate _c;
    private readonly Image _preview = new() { Width = PreviewW, Height = PreviewH };
    private readonly TextBlock _size = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _brand = new();
    private readonly TextBox _reference = new();
    private readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly List<DeviceModel> _kinds = DeviceLibrary.Placeable.ToList();
    private readonly ZBufferRasterizer _ras = new() { Ambient = 0.5f, Supersampling = 2 };
    private WriteableBitmap? _bitmap;

    public StepImportDialog(StepImporter.Candidate c, int index, int count)
    {
        _c = c;
        Title = count > 1 ? $"Modelo STEP del fabricante ({index + 1} de {count})" : "Modelo STEP del fabricante";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _brand.Text = c.Brand;
        _reference.Text = c.Reference;
        _kind.ItemsSource = _kinds.Select(k => k.Name).ToList();
        _kind.SelectedIndex = Math.Max(0, _kinds.FindIndex(k => k.Kind == c.Kind));
        _kind.SelectionChanged += (_, _) =>
        {
            if (_kind.SelectedIndex < 0) return;
            var wasDoor = IsDoor;
            _c.Kind = _kinds[_kind.SelectedIndex].Kind;
            // Al pasar de placa a puerta (o al revés) cambia cómo se busca el frente.
            if (wasDoor != IsDoor) (_c.Front, _c.Up) = StepImporter.AutoOrientation(_c.Raw, IsDoor);
            Render();
        };

        var flip = new Button { Content = "Darle la vuelta (frente ↔ espalda)" };
        flip.Click += (_, _) => { _c.Front = Opposite(_c.Front); Render(); };
        var nextFront = new Button { Content = "Otro eje como frente" };
        nextFront.Click += (_, _) =>
        {
            var axis = "XYZ".IndexOf(_c.Front[1]);
            var next = "XYZ"[(axis + 1) % 3];
            _c.Front = "+" + next;
            if (_c.Up[1] == next) _c.Up = "+" + "XYZ"[axis];
            Render();
        };
        var rotate = new Button { Content = "Girar 90°" };
        rotate.Click += (_, _) => { _c.Up = RotateUp(_c.Front, _c.Up); Render(); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        void Row(string label, Control control)
        {
            var r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            control.Margin = new Thickness(0, 3);
            Grid.SetRow(tb, r); Grid.SetRow(control, r); Grid.SetColumn(control, 1);
            grid.Children.Add(tb); grid.Children.Add(control);
        }
        Row("Archivo", new TextBlock { Text = c.FileName, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Row("Marca", _brand);
        Row("Referencia", _reference);
        Row("Tipo de aparato", _kind);

        var side = new StackPanel
        {
            Spacing = 8, Width = 300,
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = "La vista muestra el aparato como quedará montado: el frente hacia ti y la parte de arriba hacia arriba. "
                           + "Si no es así, corrígelo con los botones.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8,
                },
                flip, nextFront, rotate,
                _size,
            },
        };

        var save = new Button { Content = "Guardar en la librería", IsDefault = true };
        var saveAll = new Button { Content = "Guardar este y los demás sin preguntar", IsVisible = count - index > 1 };
        var skip = new Button { Content = "Omitir" };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        save.Click += (_, _) => Close(Finish(StepDialogResult.Save));
        saveAll.Click += (_, _) => Close(Finish(StepDialogResult.SaveAll));
        skip.Click += (_, _) => Close(StepDialogResult.Skip);
        cancel.Click += (_, _) => Close(StepDialogResult.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 12,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _preview, side } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, skip, saveAll, save } },
            },
        };
        Render();
    }

    private bool IsDoor => DeviceLibrary.Get(_c.Kind).Location == DeviceLocation.Door;

    private StepDialogResult Finish(StepDialogResult r)
    {
        _c.Brand = _brand.Text ?? "";
        _c.Reference = _reference.Text ?? "";
        return r;
    }

    private static string Opposite(string axis) => (axis[0] == '-' ? "+" : "-") + axis[1];

    /// <summary>Gira 90° la parte de arriba alrededor del eje del frente.</summary>
    internal static string RotateUp(string front, string up)
    {
        static Vector3 D(string a) => (a[1] switch { 'X' => Vector3.UnitX, 'Y' => Vector3.UnitY, _ => Vector3.UnitZ }) * (a[0] == '-' ? -1 : 1);
        var v = Vector3.Cross(D(front), D(up));
        var k = MathF.Abs(v.X) > 0.5f ? 0 : MathF.Abs(v.Y) > 0.5f ? 1 : 2;
        var neg = (k == 0 ? v.X : k == 1 ? v.Y : v.Z) < 0;
        return (neg ? "-" : "+") + "XYZ"[k];
    }

    private void Render()
    {
        var door = IsDoor;
        var m = StepImporter.Orient(_c.Raw, _c.Front, _c.Up, door);
        var (w, h, d) = StepImporter.Size(m, door);
        var inv = CultureInfo.CurrentCulture;
        _size.Text = string.Format(inv, "Medidas: {0:0.#} de ancho × {1:0.#} de alto × {2:0.#} de fondo (mm){3}", w, h, d,
            door ? ", contando lo que queda detrás de la puerta" : "");
        // Mismos ejes que la escena: X a la derecha, el fondo hacia -Y del mundo, Z arriba.
        static Vector3 P(Vector3 v) => new(v.X / 1000, -v.Y / 1000, v.Z / 1000);
        var min = new Vector3(m.Min.X, -m.Max.Y, m.Min.Z) / 1000;
        var max = new Vector3(m.Max.X, -m.Min.Y, m.Max.Z) / 1000;
        var cam = new OrbitCamera { Yaw = -62, Pitch = 18, FieldOfView = 30, MinPitch = -60, Target = (min + max) / 2 };
        cam.Distance = Math.Max(1e-3, (max - min).Length()) / (2 * Math.Tan(cam.FieldOfView * Math.PI / 360)) * 1.2;
        _ras.Resize(PreviewW, PreviewH);
        _ras.Begin(cam, 0xFFF4F6F8, 0xFFDDE3EA);
        foreach (var (a, b, c, col) in m.Triangles) _ras.DrawTriangle(new Triangle(P(a), P(b), P(c), col));
        _ras.End();
        _bitmap ??= new WriteableBitmap(new PixelSize(PreviewW, PreviewH), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = _bitmap.Lock())
        {
            var bytes = MemoryMarshal.AsBytes(_ras.Pixels.AsSpan());
            for (var y = 0; y < PreviewH; y++)
                unsafe
                {
                    bytes.Slice(y * PreviewW * 4, PreviewW * 4).CopyTo(new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, PreviewW * 4));
                }
        }
        _preview.Source = null;
        _preview.Source = _bitmap;
    }
}
