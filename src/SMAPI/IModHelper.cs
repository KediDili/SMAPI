namespace StardewModdingAPI
{
    /// <summary>Provides simplified APIs for writing mods.</summary>
    public interface IModHelper
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The full path to the mod's folder.</summary>
        string DirectoryPath { get; }

        /// <summary>An API for managing content packs.</summary>
        IContentPackHelper ContentPacks { get; }

        /// <summary>An API for reading and writing persistent mod data.</summary>
        /// <remarks>This API is intended for persisting internal mod data; see also <see cref="ModContent"/> which is intended for reading content assets (like game data, images, etc).</remarks>
        IDataHelper Data { get; }

        /// <summary>Metadata about loaded mods.</summary>
        IModRegistry ModRegistry { get; }


        /*********
        ** Public methods
        *********/
        /****
        ** Mod config file
        ****/
        /// <summary>Read the mod's configuration file (and create it if needed).</summary>
        /// <typeparam name="TConfig">The config class type. This should be a plain class that has public properties for the settings you want. These can be complex types.</typeparam>
        TConfig ReadConfig<TConfig>() where TConfig : class, new();

        /// <summary>Save to the mod's configuration file.</summary>
        /// <typeparam name="TConfig">The config class type.</typeparam>
        /// <param name="config">The config settings to save.</param>
        void WriteConfig<TConfig>(TConfig config) where TConfig : class, new();
    }
}
