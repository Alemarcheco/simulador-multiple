namespace Simulador.Core.Modules;

/// <summary>
/// Intercambio sencillo de documentos entre pestañas sin que un módulo dependa de otro: un módulo publica una
/// función que devuelve su documento abierto (p. ej. el esquema eléctrico en JSON) y otro lo pide cuando lo necesita.
/// </summary>
public static class ModuleExchange
{
    /// <summary>Clave del esquema abierto en Instalaciones eléctricas (JSON del <c>.selx</c>).</summary>
    public const string ElectricSchematic = "electrico.esquema";

    private static readonly Dictionary<string, Func<string?>> Providers = [];

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
