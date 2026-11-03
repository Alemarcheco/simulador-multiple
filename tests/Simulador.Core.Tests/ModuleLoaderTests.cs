using Simulador.Core.Modules;

namespace Simulador.Core.Tests;

public sealed class FakeModuleB : IModule
{
    public string Id => "b";
    public string Name => "B";
    public string Icon => "B";
    public int Order => 20;
    public object CreateRootView(IModuleHost host) => new();
}

public sealed class FakeModuleA : IModule
{
    public string Id => "a";
    public string Name => "A";
    public string Icon => "A";
    public int Order => 10;
    public object CreateRootView(IModuleHost host) => new();
}

public class ModuleLoaderTests
{
    [Fact]
    public void Discovers_modules_and_sorts_by_order()
    {
        var modules = ModuleLoader.FromAssemblies([typeof(ModuleLoaderTests).Assembly]);
        Assert.Equal(["a", "b"], modules.Select(m => m.Id));
    }
}
