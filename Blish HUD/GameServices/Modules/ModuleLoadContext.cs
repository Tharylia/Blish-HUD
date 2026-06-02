using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Blish_HUD.Modules;

public class ModuleLoadContext : AssemblyLoadContext {
    private readonly IReadOnlyDictionary<string, byte[]> _packagedAssemblies;

    public event EventHandler<AssemblyName> DependencyFound;
    public event EventHandler<AssemblyName> DependencyMissed;

    public ModuleLoadContext(string name, IReadOnlyDictionary<string, byte[]> packagedAssemblies) 
        : base(name, isCollectible: true) {
        _packagedAssemblies = packagedAssemblies;
    }

    protected override Assembly Load(AssemblyName assemblyName) {
        string dllName = $"{assemblyName.Name}.dll";

        if (_packagedAssemblies.TryGetValue(dllName, out byte[] assemblyData)) {
            this.DependencyFound?.Invoke(this, assemblyName);
            using var stream = new MemoryStream(assemblyData);
            return LoadFromStream(stream);
        }

        // Explicitly return null to fall back to the default/host context
        this.DependencyMissed?.Invoke(this, assemblyName);
        return null;
    }
}