using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Simulador.Help;

/// <summary>
/// Dibuja el Markdown sencillo de los temas: párrafos, <c>###</c>, listas con «-» o «1.», notas con «&gt;»,
/// **negrita**, `teclas o botones` y enlaces a otros temas <c>[texto](#modulo/tema)</c>.
/// Usa los colores del tema de la aplicación (claro u oscuro).
/// </summary>
internal sealed class MarkdownView : StackPanel
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
    private static readonly IBrush NoteBackground = new SolidColorBrush(Color.FromArgb(0x22, 0x3B, 0x82, 0xF6));
    private static readonly IBrush NoteBorder = new SolidColorBrush(Color.FromArgb(0xC0, 0x3B, 0x82, 0xF6));
    private static readonly Regex InlinePattern = new(@"\*\*(.+?)\*\*|`(.+?)`|\[(.+?)\]\(#(.+?)\)");
    private static readonly Regex Numbered = new(@"^(\d+)\.\s+(.*)$");

    private readonly Action<string> _openTopic;

    public MarkdownView(string markdown, Action<string> openTopic)
    {
        _openTopic = openTopic;
        Spacing = 8;
        Build(markdown);
    }

    private void Build(string markdown)
    {
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count == 0) return;
            Children.Add(Text(string.Join(' ', paragraph)));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                var h = Text(line[4..]);
                h.FontSize = 16;
                h.FontWeight = FontWeight.SemiBold;
                h.Margin = new Thickness(0, 8, 0, 0);
                Children.Add(h);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                Flush();
                Children.Add(ListItem("•", line[2..]));
            }
            else if (Numbered.Match(line) is { Success: true } m)
            {
                Flush();
                Children.Add(ListItem(m.Groups[1].Value + ".", m.Groups[2].Value));
            }
            else if (line.StartsWith('>'))
            {
                Flush();
                Children.Add(new Border
                {
                    Background = NoteBackground,
                    BorderBrush = NoteBorder,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 6),
                    CornerRadius = new CornerRadius(4),
                    Child = Text(line.TrimStart('>').Trim()),
                });
            }
            else
            {
                paragraph.Add(line);
            }
        }
        Flush();
    }

    private Control ListItem(string bullet, string text)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Margin = new Thickness(6, 0, 0, 0) };
        grid.Children.Add(new TextBlock { Text = bullet, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) });
        var body = Text(text);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private TextBlock Text(string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
        var inlines = new InlineCollection();
        int pos = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > pos) inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups[1].Success)
            {
                inlines.Add(new Run(m.Groups[1].Value) { FontWeight = FontWeight.SemiBold });
            }
            else if (m.Groups[2].Success)
            {
                inlines.Add(new Run(" " + m.Groups[2].Value + " ") { FontFamily = Mono, FontSize = 13, Background = CodeBackground });
            }
            else
            {
                var target = m.Groups[4].Value;
                var link = new TextBlock
                {
                    Text = m.Groups[3].Value,
                    Foreground = NoteBorder,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 14,
                };
                link.PointerPressed += (_, e) => { _openTopic(target); e.Handled = true; };
                inlines.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom });
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) inlines.Add(new Run(text[pos..]));
        block.Inlines = inlines;
        return block;
    }
}
