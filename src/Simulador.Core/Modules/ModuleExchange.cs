namespace Simulador.Core.Modules;

/// <summary>
/// Intercambio sencillo de documentos entre pestañas sin que un módulo dependa de otro: un módulo publica una
/// función que devuelve su documento abierto (p. ej. el esquema eléctrico en JSON) y otro lo pide cuando lo necesita.
/// </summary>
public static class ModuleExchange
{
    /// <summary>Clave del esquema abierto en Instalaciones eléctricas (JSON del <c>.selx</c>).</summary>
    public const string ElectricSchematic = "electrico.esquema";

    /// <summary>
    /// Aviso de que hay un paquete de librería nuevo (.edz) guardado por otro módulo; el texto es la ruta del archivo.
    /// Lo envía Diseño 3D al guardar una pieza escaneada y lo recoge Tableros eléctricos.
    /// </summary>
    public const string LibraryPackageSaved = "tableros.libreria.guardada";

    private static readonly Dictionary<string, Func<string?>> Providers = [];
    private static readonly Dictionary<string, List<Action<string>>> Listeners = [];

    /// <summary>Se apunta a los avisos de una clave (se llaman en el hilo de quien avisa).</summary>
    public static void Subscribe(string key, Action<string> listener)
    {
        lock (Listeners)
        {
            if (!Listeners.TryGetValue(key, out var l)) Listeners[key] = l = [];
            l.Add(listener);
        }
    }

    /// <summary>Avisa a quien se haya apuntado a la clave. Devuelve cuántos lo recibieron.</summary>
    public static int Notify(string key, string value)
    {
        List<Action<string>> copy;
        lock (Listeners) copy = Listeners.TryGetValue(key, out var l) ? [.. l] : [];
        foreach (var a in copy) a(value);
        return copy.Count;
    }

    public static void Publish(string key, Func<string?> provider)
    {
        lock (Providers) Providers[key] = provider;
    }

    public static string? TryGet(string key)
    {
        Func<string?>? p;
        lock (Providers) Providers.TryGetValue(key, out p);
        return p?.Invoke();
    }
}
