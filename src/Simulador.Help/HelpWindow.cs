using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Simulador.Help;

/// <summary>
/// Ventana de ayuda (F1): buscador arriba, guías y temas a la izquierda y el tema elegido a la derecha.
/// Hay una sola ventana; si ya está abierta, F1 la trae al frente y salta al tema de la pestaña activa.
/// </summary>
public sealed class HelpWindow : Window
{
    private static HelpWindow? _instance;

    private readonly HelpLibrary _library;
    private readonly TextBox _search;
    private readonly TreeView _tree = new();
    private readonly ListBox _results = new();
    private readonly TextBlock _resultsInfo = new() { Opacity = 0.7, Margin = new Thickness(4, 0, 4, 6), TextWrapping = TextWrapping.Wrap };
    private readonly Panel _left = new();
    private readonly ScrollViewer _pageScroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Button _back = new() { Content = "← Atrás", IsEnabled = false };
    private readonly Stack<HelpTopic> _history = new();
    private readonly Dictionary<HelpTopic, TreeViewItem> _treeItems = new();
    private HelpTopic? _current;
    private string? _moduleId;
    private bool _syncingTree;

    /// <summary>
    /// Abre la ayuda (o la trae al frente) en el tema de la pestaña <paramref name="moduleId"/> y, si se pasa,
    /// de la sub-pestaña visible más concreta dentro de <paramref name="activeView"/>.
    /// </summary>
    public static void ShowFor(Window? owner, string? moduleId, Control? activeView)
    {
        var library = HelpLibrary.Default;
        var topic = library.ForContext(moduleId, activeView is null ? null : VisibleTabHeaders(activeView));
        if (_instance is null)
        {
            _instance = new HelpWindow(library);
            _instance.Closed += (_, _) => _instance = null;
            if (owner is not null) _instance.Show(owner); else _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
        _instance._moduleId = moduleId;
        _instance._search.Text = "";
        _instance.Open(topic, remember: _instance._current is not null);
        _instance.FocusSearch();
    }

    /// <summary>Cabeceras de las pestañas seleccionadas y visibles dentro de una vista, de la más interna a la más externa.</summary>
    public static IReadOnlyList<string> VisibleTabHeaders(Control view) =>
        view.GetSelfAndVisualDescendants().OfType<TabControl>()
            .Where(t => t.IsEffectivelyVisible && t.SelectedItem is TabItem)
            .Select(t => (Depth: t.GetVisualAncestors().Count(), Header: HeaderText(((TabItem)t.SelectedItem!).Header)))
            .Where(x => x.Header.Length > 0)
            .OrderByDescending(x => x.Depth)
            .Select(x => x.Header)
            .ToList();

    private static string HeaderText(object? header) => header switch
    {
        string s => s,
        TextBlock tb => tb.Text ?? "",
        Control c => string.Join(' ', c.GetVisualDescendants().Prepend(c).OfType<TextBlock>().Select(t => t.Text)),
        null => "",
        _ => header.ToString() ?? "",
    };

    public HelpWindow(HelpLibrary library)
    {
        _library = library;
        Title = "Ayuda · Simulador múltiple";
        Width = 1040;
        Height = 720;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _search = new TextBox
        {
            Watermark = "Escribe una pregunta o unas palabras… p. ej. «¿cómo simulo un circuito?» o «exportar STL»",
            FontSize = 15,
            Padding = new Thickness(10, 8),
            InnerLeftContent = new TextBlock { Text = "🔍", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) },
        };
        _search.TextChanged += (_, _) => UpdateSearch();
        _search.KeyDown += OnSearchKeyDown;

        BuildTree();
        _tree.SelectionChanged += (_, _) =>
        {
            if (!_syncingTree && _tree.SelectedItem is TreeViewItem { Tag: HelpTopic t }) Open(t);
        };

        _results.SelectionMode = SelectionMode.Single;
        _results.SelectionChanged += (_, _) =>
        {
            if (_results.SelectedItem is ListBoxItem { Tag: HelpSearchResult r }) Open(r.Topic);
        };
        _results.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Up && _results.SelectedIndex <= 0) { FocusSearch(); e.Handled = true; }
        };

        var resultsPanel = new DockPanel();
        DockPanel.SetDock(_resultsInfo, Dock.Top);
        resultsPanel.Children.Add(_resultsInfo);
        resultsPanel.Children.Add(_results);
        resultsPanel.IsVisible = false;
        _left.Children.Add(_tree);
        _left.Children.Add(resultsPanel);

        _back.Click += (_, _) => GoBack();
        var printHint = new TextBlock
        {
            Text = "F1 abre la ayuda de la pestaña en la que estás · Ctrl+F busca · Esc cierra",
            Opacity = 0.6,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var bottom = new DockPanel { Margin = new Thickness(12, 6) };
        DockPanel.SetDock(_back, Dock.Left);
        bottom.Children.Add(_back);
        bottom.Children.Add(printHint);

        var leftBorder = new Border { Child = _left, Padding = new Thickness(6, 4) };
        var page = new Border { Child = _pageScroll, Padding = new Thickness(0) };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("320,Auto,*") };
        body.Children.Add(leftBorder);
        var splitter = new GridSplitter { Width = 5, ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        Grid.SetColumn(page, 2);
        body.Children.Add(page);

        var searchBox = new Border { Child = _search, Padding = new Thickness(12, 12, 12, 6) };
        var root = new DockPanel();
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(searchBox);
        root.Children.Add(bottom);
        root.Children.Add(body);
        Content = root;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void BuildTree()
    {
        var items = new List<TreeViewItem>();
        foreach (var guide in _library.Guides)
        {
            var guideItem = new TreeViewItem { Header = GuideHeader(guide), Tag = guide.Overview };
            _treeItems[guide.Overview] = guideItem;
            var children = new List<TreeViewItem>();
            foreach (var topic in guide.Topics.Where(t => !t.IsOverview && !t.IsQuestion))
                children.Add(TopicItem(topic));
            var questions = guide.Topics.Where(t => t.IsQuestion).ToList();
            if (questions.Count > 0)
            {
                var faq = new TreeViewItem
                {
                    Header = new TextBlock { Text = $"Preguntas frecuentes ({questions.Count})", FontStyle = FontStyle.Italic },
                    ItemsSource = questions.Select(TopicItem).ToList(),
                };
                children.Add(faq);
            }
            guideItem.ItemsSource = children;
            items.Add(guideItem);
        }
        _tree.ItemsSource = items;
    }

    private static Control GuideHeader(HelpGuide guide) =>
        new TextBlock { Text = guide.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    private TreeViewItem TopicItem(HelpTopic topic)
    {
        var item = new TreeViewItem { Header = new TextBlock { Text = topic.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 }, Tag = topic };
        _treeItems[topic] = item;
        return item;
    }

    private void UpdateSearch()
    {
        var query = _search.Text ?? "";
        bool searching = query.Trim().Length > 0;
        _tree.IsVisible = !searching;
        _left.Children[1].IsVisible = searching;
        if (!searching)
        {
            _results.ItemsSource = null;
            if (_current is not null) SyncTree(_current);
            return;
        }

        var results = _library.Search(query, _moduleId);
        _resultsInfo.Text = results.Count switch
        {
            0 => "No encontré nada. Prueba con otras palabras (por ejemplo, el nombre de un botón) o con menos palabras.",
            1 => "1 tema encontrado.",
            _ => $"{results.Count} temas encontrados. Usa ↓ y Enter para abrirlos.",
        };
        _results.ItemsSource = results.Select(ResultItem).ToList();
    }

    private static ListBoxItem ResultItem(HelpSearchResult r)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2) };
        panel.Children.Add(new TextBlock { Text = r.Topic.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = r.Topic.Guide.Title, FontSize = 11, Opacity = 0.65 });
        if (r.Snippet.Length > 0)
            panel.Children.Add(new TextBlock { Text = r.Snippet, FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, MaxHeight = 52 });
        return new ListBoxItem { Content = panel, Tag = r };
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_results.IsEffectivelyVisible || _results.ItemCount == 0) return;
        if (e.Key == Key.Down)
        {
            _results.SelectedIndex = Math.Max(0, _results.SelectedIndex);
            (_results.ContainerFromIndex(_results.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _results.SelectedIndex = Math.Max(0, _results.SelectedIndex);
            if (_results.SelectedItem is ListBoxItem { Tag: HelpSearchResult r }) Open(r.Topic);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 || (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_search.Text)) { _search.Text = ""; FocusSearch(); }
            else Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            GoBack();
            e.Handled = true;
        }
    }

    private void FocusSearch() => Dispatcher.UIThread.Post(() => { _search.Focus(); _search.SelectAll(); }, DispatcherPriority.Input);

    private void OpenById(string id)
    {
        var topic = _library.FindTopic(id) ?? _library.FindGuide(id)?.Overview;
        if (topic is not null) Open(topic);
    }

    private void Open(HelpTopic topic, bool remember = true)
    {
        if (ReferenceEquals(topic, _current)) return;
        if (remember && _current is not null) _history.Push(_current);
        _back.IsEnabled = _history.Count > 0;
        _current = topic;
        _pageScroll.Content = Page(topic);
        _pageScroll.Offset = default;
        if (_tree.IsVisible) SyncTree(topic);
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        var previous = _history.Pop();
        _current = null;
        Open(previous, remember: false);
    }

    private void SyncTree(HelpTopic topic)
    {
        if (!_treeItems.TryGetValue(topic, out var item)) return;
        _syncingTree = true;
        try
        {
            // Despliega la guía (y "Preguntas frecuentes" si hace falta) para que se vea dónde estás.
            foreach (var guideItem in _tree.Items.OfType<TreeViewItem>())
            {
                if (!ReferenceEquals(guideItem.Tag, topic.Guide.Overview)) continue;
                guideItem.IsExpanded = true;
                if (topic.IsQuestion)
                    foreach (var faq in guideItem.Items.OfType<TreeViewItem>().Where(i => i.Tag is null))
                        faq.IsExpanded = true;
            }
            _tree.SelectedItem = item;
            Dispatcher.UIThread.Post(() => item.BringIntoView(), DispatcherPriority.Background);
        }
        finally
        {
            _syncingTree = false;
        }
    }

    private Control Page(HelpTopic topic)
    {
        var page = new StackPanel { Spacing = 10, Margin = new Thickness(24, 12, 28, 24), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        if (!topic.IsOverview)
        {
            var crumb = new TextBlock
            {
                Text = topic.Guide.Title + " ›",
                Opacity = 0.65,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            crumb.PointerPressed += (_, e) => { Open(topic.Guide.Overview); e.Handled = true; };
            page.Children.Add(crumb);
        }
        page.Children.Add(new TextBlock { Text = topic.Title, FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        page.Children.Add(new MarkdownView(topic.Body, OpenById));

        if (topic.IsOverview)
        {
            // Índice de la guía: los temas y las preguntas, para llegar con un clic.
            AddLinks(page, "En esta guía", topic.Guide.Topics.Where(t => !t.IsOverview && !t.IsQuestion));
            AddLinks(page, "Preguntas frecuentes", topic.Guide.Topics.Where(t => t.IsQuestion));
        }
        else
        {
            var siblings = topic.Guide.Topics.Where(t => !t.IsOverview).ToList();
            int i = siblings.IndexOf(topic);
            var nav = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            if (i > 0) nav.Children.Add(NavButton("← " + siblings[i - 1].Title, siblings[i - 1], Dock.Left));
            if (i >= 0 && i < siblings.Count - 1) nav.Children.Add(NavButton(siblings[i + 1].Title + " →", siblings[i + 1], Dock.Right));
            page.Children.Add(nav);
        }
        return page;
    }

    private Button NavButton(string text, HelpTopic target, Dock dock)
    {
        var b = new Button { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 } };
        b.Click += (_, _) => Open(target);
        DockPanel.SetDock(b, dock);
        if (dock == Dock.Right) b.HorizontalAlignment = HorizontalAlignment.Right;
        return b;
    }

    private void AddLinks(StackPanel page, string title, IEnumerable<HelpTopic> topics)
    {
        var list = topics.ToList();
        if (list.Count == 0) return;
        page.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        var wrap = new WrapPanel();
        foreach (var t in list)
        {
            var b = new Button { Content = t.Title, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => Open(t);
            wrap.Children.Add(b);
        }
        page.Children.Add(wrap);
    }
}
