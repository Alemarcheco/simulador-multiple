namespace Simulador.Core.Modules;

/// <summary>Nombres de los menús de la barra superior, en el orden en que aparecen.</summary>
public static class MenuNames
{
    public const string File = "Archivo";
    public const string Edit = "Editar";
    public const string View = "Ver";
    public const string Tools = "Herramientas";

    public static readonly IReadOnlyList<string> Order = [File, Edit, View, Tools];
}

/// <summary>
/// Una orden de la barra de menús superior (Archivo, Editar, Ver…). Los simuladores ponen ahí lo que se usa
/// poco (Nuevo, Abrir, Guardar, Ejemplos, Exportar, Deshacer…) para dejar la barra de herramientas libre.
/// Sin dependencia de interfaz gráfica: la ventana principal construye el menú real.
/// </summary>
/// <param name="Header">Texto de la orden, p. ej. "Guardar como…".</param>
/// <param name="Run">Qué hace al pulsarla (nulo si sólo abre un submenú).</param>
public sealed record MenuCommand(string Header, Action? Run = null)
{
    /// <summary>Menú donde aparece (ver <see cref="MenuNames"/>). Por defecto, Archivo.</summary>
    public string Menu { get; init; } = MenuNames.File;

    /// <summary>Atajo de teclado que se muestra a la derecha, p. ej. "Ctrl+S" (sólo informativo).</summary>
    public string? Shortcut { get; init; }

    /// <summary>Explicación al pasar el ratón.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Submenú (p. ej. la lista de ejemplos).</summary>
    public IReadOnlyList<MenuCommand>? Items { get; init; }

    /// <summary>Dibuja una línea de separación antes de esta orden.</summary>
    public bool SeparatorBefore { get; init; }

    /// <summary>Si devuelve false, la orden aparece desactivada.</summary>
    public Func<bool>? IsEnabled { get; init; }

    /// <summary>Crea un submenú con las entradas dadas.</summary>
    public static MenuCommand Submenu(string header, IEnumerable<MenuCommand> items, string menu = MenuNames.File) =>
        new(header) { Items = items.ToList(), Menu = menu };
}

/// <summary>
/// Lo implementa una vista (control) de un simulador que aporta órdenes a la barra de menús.
/// La ventana principal pregunta a las vistas que estén a la vista en la pestaña activa cada vez
/// que se abre un menú, así que una vista con sub-pestañas (Esquemático, PCB…) muestra sólo las suyas.
/// </summary>
public interface IMenuSource
{
    IEnumerable<MenuCommand> GetMenuCommands();
}
