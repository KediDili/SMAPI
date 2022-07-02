using System;
using System.IO;

namespace StardewModdingAPI.Framework.ModHelpers
{
    /// <summary>Provides simplified APIs for writing mods.</summary>
    internal class ModHelper : BaseHelper, IModHelper, IDisposable
    {
        /*********
        ** Accessors
        *********/
        /// <inheritdoc />
        public string DirectoryPath { get; }

        /// <inheritdoc />
        public IModRegistry ModRegistry { get; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="mod">The mod using this instance.</param>
        /// <param name="modDirectory">The full path to the mod's folder.</param>
        /// <param name="modRegistry">an API for fetching metadata about loaded mods.</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="modDirectory"/> path does not exist on disk.</exception>
        public ModHelper(IModMetadata mod, string modDirectory, IModRegistry modRegistry)
            : base(mod)
        {
            // validate directory
            if (string.IsNullOrWhiteSpace(modDirectory))
                throw new ArgumentNullException(nameof(modDirectory));
            if (!Directory.Exists(modDirectory))
                throw new InvalidOperationException("The specified mod directory does not exist.");

            // initialize
            this.DirectoryPath = modDirectory;
            this.ModRegistry = modRegistry ?? throw new ArgumentNullException(nameof(modRegistry));
        }

        /****
        ** Disposal
        ****/
        /// <inheritdoc />
        public void Dispose()
        {
            // nothing to dispose yet
        }
    }
}
