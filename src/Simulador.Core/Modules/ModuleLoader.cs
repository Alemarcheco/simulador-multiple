using System.Reflection;

namespace Simulador.Core.Modules;

/// <summary>Descubre los módulos (<see cref="IModule"/>) en los ensamblados <c>Simulador.*.dll</c>.</summary>
public static class ModuleLoader
{
    /// <summary>Carga los ensamblados <c>Simulador.*.dll</c> de una carpeta y devuelve sus módulos ordenados.</summary>
    public static IReadOnlyList<IModule> LoadFromDirectory(string directory, Action<string, Exception>? onError = null)
    {
        var assemblies = new List<Assembly>();
        foreach (var path in Directory.EnumerateFiles(directory, "Simulador.*.dll"))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path);
                assemblies.Add(Assembly.Load(name));
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                onError?.Invoke(path, ex);
            }
        }
        return FromAssemblies(assemblies, onError);
    }

    /// <summary>Instancia todos los <see cref="IModule"/> públicos y concretos de los ensamblados dados.</summary>
    public static IReadOnlyList<IModule> FromAssemblies(IEnumerable<Assembly> assemblies, Action<string, Exception>? onError = null)
    {
        var modules = new List<IModule>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies.Distinct())
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (Exception ex)
            {
                onError?.Invoke(assembly.FullName ?? "?", ex);
                continue;
            }

            foreach (var type in types)
            {
                if (!typeof(IModule).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) is null)
                    continue;
                try
                {
                    var module = (IModule)Activator.CreateInstance(type)!;
                    if (seenIds.Add(module.Id))
                        modules.Add(module);
                    else
                        onError?.Invoke(type.FullName!, new InvalidOperationException($"Id de módulo duplicado: {module.Id}"));
                }
                catch (Exception ex)
                {
                    onError?.Invoke(type.FullName!, ex);
                }
            }
        }

        return modules.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.CurrentCulture).ToList();
    }
}
