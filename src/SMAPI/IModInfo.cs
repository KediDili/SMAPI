namespace StardewModdingAPI
{
    /// <summary>Metadata for a loaded mod.</summary>
    public interface IModInfo
    {
        /// <summary>The mod manifest.</summary>
        IManifest Manifest { get; }
    }
}
