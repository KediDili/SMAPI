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

        /// <summary>Metadata about loaded mods.</summary>
        IModRegistry ModRegistry { get; }
    }
}
