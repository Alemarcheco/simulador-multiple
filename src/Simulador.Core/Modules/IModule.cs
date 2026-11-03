namespace Simulador.Core.Modules;

/// <summary>
/// Un simulador de la aplicación. Cada módulo aparece como una pestaña en la ventana principal.
/// </summary>
/// <remarks>
/// Para añadir un módulo: crea un proyecto <c>Simulador.&lt;Nombre&gt;</c>, implementa esta interfaz en una
/// clase pública con constructor sin parámetros y referencia el proyecto desde <c>Simulador.App</c>.
/// La aplicación lo descubre sola al arrancar (ver <see cref="ModuleLoader"/>).
/// </remarks>
public interface IModule
{
    /// <summary>Identificador estable en minúsculas, p. ej. "circuitos", "electrico", "plc", "cad3d".</summary>
    string Id { get; }

    /// <summary>Nombre visible en la pestaña.</summary>
    string Name { get; }

    /// <summary>Icono de la pestaña: un carácter (emoji o glifo de fuente) para no depender de recursos.</summary>
    string Icon { get; }

    /// <summary>Orden de la pestaña (menor primero).</summary>
    int Order { get; }

    /// <summary>
    /// Crea la vista raíz del módulo. Debe devolver un <c>Avalonia.Controls.Control</c>;
    /// se declara como <see cref="object"/> para que Simulador.Core no dependa de Avalonia.
    /// Se llama una sola vez, la primera vez que se muestra la pestaña.
    /// </summary>
    object CreateRootView(IModuleHost host);
}

/// <summary>Servicios que la aplicación ofrece a cada módulo.</summary>
public interface IModuleHost
{
    /// <summary>Todos los módulos cargados, por si uno quiere integrarse con otro.</summary>
    IReadOnlyList<IModule> Modules { get; }

    /// <summary>Carpeta de datos del usuario para este módulo (librerías, ajustes). Se crea si no existe.</summary>
    string GetDataDirectory(string moduleId);

    /// <summary>Muestra un mensaje en la barra de estado.</summary>
    void SetStatus(string text);
}
