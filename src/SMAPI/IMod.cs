namespace StardewModdingAPI
{
    /// <summary>The implementation for a Stardew Valley mod.</summary>
    public interface IMod
    {
        /*********
        ** Accessors
        *********/
        /// <summary>Writes messages to the console and log file.</summary>
        IMonitor Monitor { get; }

        /// <summary>The mod's manifest.</summary>
        IManifest ModManifest { get; }


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        void Entry();

        /// <summary>Get an API that other mods can access. This is always called after <see cref="Entry"/>.</summary>
        object? GetApi();
    }
}
