using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using StardewModdingAPI.Framework;

namespace StardewModdingAPI
{
    /// <summary>The main entry point for SMAPI, responsible for hooking into and launching the game.</summary>
    internal class Program
    {
        /*********
        ** Fields
        *********/
        /// <summary>The absolute path to search for SMAPI's internal DLLs.</summary>
        private static readonly string DllSearchPath = EarlyConstants.InternalFilesPath;

        /// <summary>The assembly paths in the search folders indexed by assembly name.</summary>
        private static Dictionary<string, string>? AssemblyPathsByName;


        /*********
        ** Public methods
        *********/
        /// <summary>The main entry point which hooks into and launches the game.</summary>
        public static void Main()
        {
            Console.Title = $"SMAPI {EarlyConstants.RawApiVersion}";

            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += Program.CurrentDomain_AssemblyResolve;
                Program.Start();
            }
            catch (BadImageFormatException ex) when (ex.FileName == EarlyConstants.GameAssemblyName)
            {
                Console.WriteLine($"SMAPI failed to initialize because your game's {ex.FileName}.exe seems to be invalid.\nThis may be a pirated version which modified the executable in an incompatible way; if so, you can try a different download or buy a legitimate version.\n\nTechnical details:\n{ex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SMAPI failed to initialize: {ex}");
                Program.PressAnyKeyToExit(true);
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Method called when assembly resolution fails, which may return a manually resolved assembly.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private static Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs e)
        {
            // cache assembly paths by name
            if (Program.AssemblyPathsByName == null)
            {
                Program.AssemblyPathsByName = new(StringComparer.OrdinalIgnoreCase);

                foreach (string searchPath in new[] { EarlyConstants.GamePath, Program.DllSearchPath })
                {
                    foreach (string dllPath in Directory.EnumerateFiles(searchPath, "*.dll"))
                    {
                        try
                        {
                            string? curName = AssemblyName.GetAssemblyName(dllPath).Name;
                            if (curName != null)
                                Program.AssemblyPathsByName[curName] = dllPath;
                        }
                        catch
                        {
                            // ignore invalid DLL
                        }
                    }
                }
            }

            // resolve
            try
            {
                string? searchName = new AssemblyName(e.Name).Name;
                return searchName != null && Program.AssemblyPathsByName.TryGetValue(searchName, out string? assemblyPath)
                    ? Assembly.LoadFrom(assemblyPath)
                    : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving assembly: {ex}");
                return null;
            }
        }

        /// <summary>Initialize SMAPI and launch the game.</summary>
        /// <remarks>This method is separate from <see cref="Main"/> because that can't contain any references to assemblies loaded by <see cref="CurrentDomain_AssemblyResolve"/> (e.g. via <see cref="Constants"/>), or Mono will incorrectly show an assembly resolution error before assembly resolution is set up.</remarks>
        private static void Start()
        {
            // load SMAPI
            using SCore core = new();
            core.RunInteractively();
        }

        /// <summary>Show a 'press any key to exit' message, and exit when they press a key.</summary>
        /// <param name="showMessage">Whether to print a 'press any key to exit' message to the console.</param>
        private static void PressAnyKeyToExit(bool showMessage)
        {
            if (showMessage)
                Console.WriteLine("Game has ended. Press any key to exit.");
            Thread.Sleep(100);
            Console.ReadKey();
            Environment.Exit(0);
        }
    }
}
