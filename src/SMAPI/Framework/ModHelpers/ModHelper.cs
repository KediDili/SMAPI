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
        public IContentPackHelper ContentPacks { get; }

        /// <inheritdoc />
        public IDataHelper Data { get; }

        /// <inheritdoc />
        public IModRegistry ModRegistry { get; }

        /// <inheritdoc />
        public ITranslationHelper Translation { get; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="mod">The mod using this instance.</param>
        /// <param name="modDirectory">The full path to the mod's folder.</param>
        /// <param name="contentPackHelper">An API for managing content packs.</param>
        /// <param name="dataHelper">An API for reading and writing persistent mod data.</param>
        /// <param name="modRegistry">an API for fetching metadata about loaded mods.</param>
        /// <param name="translationHelper">An API for reading translations stored in the mod's <c>i18n</c> folder.</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The <paramref name="modDirectory"/> path does not exist on disk.</exception>
        public ModHelper(
            IModMetadata mod, string modDirectory, IContentPackHelper contentPackHelper, IDataHelper dataHelper, IModRegistry modRegistry, ITranslationHelper translationHelper
        )
            : base(mod)
        {
            // validate directory
            if (string.IsNullOrWhiteSpace(modDirectory))
                throw new ArgumentNullException(nameof(modDirectory));
            if (!Directory.Exists(modDirectory))
                throw new InvalidOperationException("The specified mod directory does not exist.");

            // initialize
            this.DirectoryPath = modDirectory;
            this.ContentPacks = contentPackHelper ?? throw new ArgumentNullException(nameof(contentPackHelper));
            this.Data = dataHelper ?? throw new ArgumentNullException(nameof(dataHelper));
            this.ModRegistry = modRegistry ?? throw new ArgumentNullException(nameof(modRegistry));
            this.Translation = translationHelper ?? throw new ArgumentNullException(nameof(translationHelper));
        }

        /****
        ** Mod config file
        ****/
        /// <inheritdoc />
        public TConfig ReadConfig<TConfig>()
            where TConfig : class, new()
        {
            TConfig config = this.Data.ReadJsonFile<TConfig>("config.json") ?? new TConfig();
            this.WriteConfig(config); // create file or fill in missing fields
            return config;
        }

        /// <inheritdoc />
        public void WriteConfig<TConfig>(TConfig config)
            where TConfig : class, new()
        {
            this.Data.WriteJsonFile("config.json", config);
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
