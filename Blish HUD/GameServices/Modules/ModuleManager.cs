using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Blish_HUD.Content;

namespace Blish_HUD.Modules {

    public class ModuleManager : IDisposable {

        private static readonly Logger Logger = Logger.GetLogger<ModuleManager>();

        private static readonly List<string> _dirtyNamespaces = new List<string>();

        public event EventHandler<EventArgs> ModuleEnabled;
        public event EventHandler<EventArgs> ModuleDisabled;

        public event EventHandler<EventArgs> ModuleLoaded;

        public void OnModuleLoaded(object _, EventArgs e) {
            this.ModuleLoaded?.Invoke(this, e);
        }

        // private Assembly _moduleAssembly;

        // private static ConcurrentDictionary<string,ModuleLoadContext> _loadContexts = new ConcurrentDictionary<string, ModuleLoadContext>();

        private ModuleLoadContext? _loadContext;
        
        private bool _forceAllowDependency = false;

        /// <summary>
        /// Indicates that the modules assembly has been loaded into memory.
        /// </summary>
        public bool AssemblyLoaded => _loadContext != null;
        
        /// <summary>
        /// Used to indicate if a different version of the assembly has previously
        /// been loaded preventing us from loading another of a different version.
        /// </summary>
        public bool IsModuleAssemblyStateDirty { get; private set; }
        
        /// <summary>
        /// Indicates if the module is currently enabled.
        /// </summary>
        public bool Enabled { get; private set; }

        public bool DependenciesMet =>
            State.IgnoreDependencies
         || Manifest.Dependencies.TrueForAll(d => d.GetDependencyDetails().CheckResult == ModuleDependencyCheckResult.Available);

        public Manifest Manifest { get; }

        public ModuleState State { get; }

        public IDataReader DataReader { get; }

        [Import]
        public Module ModuleInstance { get; private set; }

        internal ModuleManager(Manifest manifest, ModuleState state, IDataReader dataReader) {
            this.Manifest   = manifest;
            this.State      = state;
            this.DataReader = dataReader;

            if (_dirtyNamespaces.Contains(this.Manifest.Namespace)) {
                this.IsModuleAssemblyStateDirty = true;
            }
            
            // AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
            // AppDomain.CurrentDomain..TypeResolve += CurrentDomainOnTypeResolve;
        }

        // private Assembly CurrentDomainOnTypeResolve(object sender, ResolveEventArgs args) {
        //     // Search all loaded module contexts
        //     foreach (var ctx in _loadContexts.Values) {
        //         foreach (var assembly in from assembly in ctx.Assemblies
        //                                  let type = assembly.GetType(args.Name)
        //                                  where type != null
        //                                  select assembly) {
        //             return assembly;
        //         }
        //     }
        //     return null;
        // }

        public bool TryEnable() {
            if (this.Enabled                                             // We're already enabled.
             || this.IsModuleAssemblyStateDirty                          // User updated the module after the old assembly had already been enabled.
             || GameService.Module.ModuleIsExplicitlyIncompatible(this)) // Module is on the explicit "incompatibile" list.
                return false;

            var moduleParams = ModuleParameters.BuildFromManifest(this.Manifest, this);

            if (moduleParams != null) {
                string packagePath = this.Manifest.Package.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                         ? this.Manifest.Package
                                         : $"{this.Manifest.Package}.dll";

                if (this.DataReader.FileExists(packagePath)) {
                    ComposeModuleFromFileSystemReader(packagePath, moduleParams);

                    if (this.ModuleInstance != null) {
                        if (!_dirtyNamespaces.Contains(this.Manifest.Namespace)) {
                            _dirtyNamespaces.Add(this.Manifest.Namespace);
                        }

                        this.ModuleInstance.ModuleLoaded += OnModuleLoaded;

                        this.Enabled = true;

                        try {
                            this.ModuleInstance.DoInitialize();
                            this.ModuleInstance.DoLoad();

                            this.ModuleEnabled?.Invoke(this, EventArgs.Empty);
                        } catch (TypeLoadException ex) {
                            this.ModuleInstance = null;
                            this.Enabled        = false;
                            Logger.Error(ex, "Module {module} failed to load because it depended on a type which is not available in this version.  Ensure you are using the correct module and Blish HUD versions.", this.Manifest.GetDetailedName());
                        }
                    }
                } else {
                    Logger.Error($"Assembly '{packagePath}' could not be loaded from {this.DataReader.GetType().Name}.");
                }
            }

            this.State.Enabled = this.Enabled;
            GameService.Settings.Save();

            GameService.Module.SortMenuItems();

            return this.Enabled;
        }

        public void Disable() {
            if (!this.Enabled) return;

            this.Enabled = false;

            if (this.ModuleInstance != null) {
                this.ModuleInstance.ModuleLoaded -= OnModuleLoaded;
            }

            try {
                this.ModuleInstance?.Dispose();
            } catch (Exception ex) {
                Logger.GetLogger(this.ModuleInstance != null ? this.ModuleInstance.GetType() : typeof(ModuleManager)).Error(ex, "Module {module} threw an exception while unloading.", this.Manifest.GetDetailedName());
                
                if (ApplicationSettings.Instance.DebugEnabled) {
                    // To assist in debugging modules
                    throw;
                }
            }

            this.ModuleInstance = null;
            
            this.ModuleDisabled?.Invoke(this, EventArgs.Empty);

            this.State.Enabled = this.Enabled;
            GameService.Settings.Save();

            GameService.Module.SortMenuItems();

            if (this._loadContext is not null) {
                // _loadContexts.Remove(this._loadContext.Name, out _);
                _loadContext.DependencyFound  -= LoadContextOnDependencyFound;
                _loadContext.DependencyMissed -= LoadContextOnDependencyMissed;
                this._loadContext.Unload();
                this._loadContext = null;
            }
        }

        public void DeleteModule() {
            Disable();
            GameService.Module.UnregisterModule(this);
            this.DataReader.DeleteRoot();
        }

        private Assembly LoadPackagedAssembly(AssemblyLoadContext context, string assemblyPath) {
            string symbolsPath = assemblyPath.Replace(".dll", ".pdb");

            var assemblyData = this.DataReader.GetFileStream(assemblyPath);
            var symbolData   = this.DataReader.GetFileStream(symbolsPath);

            return context.LoadFromStream(assemblyData, symbolData);
        }

        private Assembly GetResourceAssembly(AssemblyLoadContext context, AssemblyName resourceDetails, string assemblyPath) {
            // Avoid loading resource assembly from wrong module
            // if (_moduleAssembly != requestingAssembly) return null;

            // English is default — ignore it
            if (!string.Equals(resourceDetails.CultureInfo.TwoLetterISOLanguageName, "en")) {
                // Non-English resource to be loaded
                assemblyPath = $"{resourceDetails.CultureName}/{assemblyPath}";

                if (this.DataReader.FileExists(assemblyPath)) {
                    try {
                        return LoadPackagedAssembly(context, assemblyPath);
                    } catch (Exception ex) {
                        Logger.Debug(ex, "Failed to load resource {dependency} for {module}.", resourceDetails.FullName, this.Manifest.GetDetailedName());
                    }
                } else {
                    Logger.Debug("Resource assembly {dependency} for {module} could not be found.", resourceDetails.FullName, this.Manifest.GetDetailedName());
                }
            }

            return null;
        }

        // private Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args) {
        //     if (this.Enabled || _forceAllowDependency) {
        //         var assemblyDetails = new AssemblyName(args.Name);
        //
        //         string assemblyPath = $"{assemblyDetails.Name}.dll";
        //
        //         if (!Equals(assemblyDetails.CultureInfo, CultureInfo.InvariantCulture)) {
        //             return GetResourceAssembly(args.RequestingAssembly, assemblyDetails, assemblyPath);
        //         }
        //
        //         if (!this.DataReader.FileExists(assemblyPath)) return null;
        //
        //         Logger.Debug("Requested dependency {dependency} ({assemblyName}) was found by module {module}.", args.Name, assemblyPath, this.Manifest.GetDetailedName());
        //
        //         try {
        //             return LoadPackagedAssembly(assemblyPath);
        //         } catch (Exception ex) {
        //             Logger.Warn(ex, "Failed to load dependency {dependency} for {module}.", args.Name, this.Manifest.GetDetailedName());
        //         }
        //     }
        //
        //     return null;
        // }

        private void ComposeModuleFromFileSystemReader(string dllName, ModuleParameters parameters) {
            if (_loadContext == null) {
                try {
                    if (!this.DataReader.FileExists(dllName)) {
                        Logger.Warn("Module {module} does not contain assembly DLL {dll}", this.Manifest.GetDetailedName(), dllName);
                        return;
                    }
                    
                    var dependencies = this.DataReader
                        .GetAllFiles()
                        .Where(f => f != this.Manifest.Package && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(Path.GetFileName,
                                      f => this.DataReader.GetFileBytes(f),
                                      StringComparer.OrdinalIgnoreCase);
                    
                    // dependencies.Add(Path.GetFileName(typeof(BlishHud).Assembly.Location), File.ReadAllBytes(typeof(BlishHud).Assembly.Location));

                    _loadContext                 =  new ModuleLoadContext(parameters.Manifest.Namespace, dependencies);
                    _loadContext.DependencyFound += LoadContextOnDependencyFound;
                    _loadContext.DependencyMissed += LoadContextOnDependencyMissed;
                    Logger.Info("Creating module load context {name}", _loadContext.Name);
                    var moduleAssembly              =  LoadPackagedAssembly(_loadContext, dllName);

                    if (moduleAssembly == null) {
                        Logger.Warn("Module {module} failed to load assembly DLL {dll}.", this.Manifest.GetDetailedName(), dllName);
                        return;
                    }
                    
                    var catalog   = new AssemblyCatalog(moduleAssembly);
                    var container = new CompositionContainer(catalog);

                    container.ComposeExportedValue("ModuleParameters", parameters);

                    _forceAllowDependency = true;

                    try {
                        container.SatisfyImportsOnce(this);
                    } catch (CompositionException ex) {
                        Logger.Warn(ex, "Module {module} failed to be composed.", this.Manifest.GetDetailedName());
                    } catch (FileNotFoundException ex) {
                        Logger.Warn(ex, "Module {module} failed to load a dependency.", this.Manifest.GetDetailedName());
                    } catch (ReflectionTypeLoadException ex) {
                        Logger.Warn(ex, "Module {module} failed to load because it depended on something not available in this version.  Ensure you are using the correct module and Blish HUD versions.", this.Manifest.GetDetailedName());
                    }

                    _forceAllowDependency = false;
                    
                    // _ = _loadContexts.TryAdd(this._loadContext.Name, _loadContext);
                } catch (ReflectionTypeLoadException ex) {
                    Logger.Warn(ex, "Module {module} failed to load due to a type exception. Ensure that you are using the correct version of the Module", this.Manifest.GetDetailedName());
                    return;
                } catch (BadImageFormatException ex) {
                    Logger.Warn(ex, "Module {module} failed to load.  Check that it is a compiled module.", this.Manifest.GetDetailedName());
                    return;
                } catch (Exception ex) {
                    Logger.Warn(ex, "Module {module} failed to load due to an unexpected error.", this.Manifest.GetDetailedName());
                    return;
                }
            }
        }

        private void LoadContextOnDependencyMissed(object sender, AssemblyName assemblyName) {
            Logger.Debug("Requested dependency {dependency} was not found inside module {module} and will be searched in default context.", assemblyName.Name, this.Manifest.GetDetailedName());
        }

        private void LoadContextOnDependencyFound(object sender, AssemblyName assemblyName) {
            Logger.Debug("Requested dependency {dependency} was found inside module {module}.", assemblyName.Name, this.Manifest.GetDetailedName());
        }

        private Assembly LoadContextOnResolving(AssemblyLoadContext context, AssemblyName assemblyName) {
            if (this.Enabled || _forceAllowDependency) {
                string assemblyPath = $"{assemblyName.Name}.dll";

                if (!Equals(assemblyName.CultureInfo, CultureInfo.InvariantCulture)) {
                    return GetResourceAssembly(context, assemblyName, assemblyPath);
                }

                if (!this.DataReader.FileExists(assemblyPath)) return null;

                Logger.Debug("Requested dependency {dependency} ({assemblyName}) was found by module {module}.", assemblyName.Name, assemblyPath, this.Manifest.GetDetailedName());

                try {
                    return LoadPackagedAssembly(context, assemblyPath);
                } catch (Exception ex) {
                    Logger.Warn(ex, "Failed to load dependency {dependency} for {module}.", assemblyName.Name, this.Manifest.GetDetailedName());
                }
            }

            return null;
        }

        public void Dispose() {
            Disable();

            GameService.Module.UnregisterModule(this);

            this.ModuleEnabled = null;
            this.ModuleDisabled = null;

            this.ModuleLoaded = null;

            // _moduleAssembly = null;

            this.DataReader?.Dispose();
        }

    }

}
