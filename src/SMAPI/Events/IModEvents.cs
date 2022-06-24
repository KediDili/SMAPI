namespace StardewModdingAPI.Events
{
    /// <summary>Manages access to events raised by SMAPI.</summary>
    public interface IModEvents
    {
        /// <summary>Events related to assets loaded from the content pipeline (including data, maps, and textures).</summary>
        IContentEvents Content { get; }
    }
}
