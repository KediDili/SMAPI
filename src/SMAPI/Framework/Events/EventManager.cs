using StardewModdingAPI.Events;

namespace StardewModdingAPI.Framework.Events
{
    /// <summary>Manages SMAPI events.</summary>
    internal class EventManager
    {
        /*********
        ** Events
        *********/
        /****
        ** Content
        ****/
        /// <inheritdoc cref="IContentEvents.AssetRequested" />
        public readonly ManagedEvent<AssetRequestedEventArgs> AssetRequested;

        /// <inheritdoc cref="IContentEvents.AssetsInvalidated" />
        public readonly ManagedEvent<AssetsInvalidatedEventArgs> AssetsInvalidated;

        /// <inheritdoc cref="IContentEvents.AssetReady" />
        public readonly ManagedEvent<AssetReadyEventArgs> AssetReady;

        /// <inheritdoc cref="IContentEvents.LocaleChanged" />
        public readonly ManagedEvent<LocaleChangedEventArgs> LocaleChanged;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="modRegistry">The mod registry with which to identify mods.</param>
        public EventManager(ModRegistry modRegistry)
        {
            // create shortcut initializers
            ManagedEvent<TEventArgs> ManageEventOf<TEventArgs>(string typeName, string eventName)
            {
                return new ManagedEvent<TEventArgs>($"{typeName}.{eventName}", modRegistry);
            }

            // init events
            this.AssetRequested = ManageEventOf<AssetRequestedEventArgs>(nameof(IModEvents.Content), nameof(IContentEvents.AssetRequested));
            this.AssetsInvalidated = ManageEventOf<AssetsInvalidatedEventArgs>(nameof(IModEvents.Content), nameof(IContentEvents.AssetsInvalidated));
            this.AssetReady = ManageEventOf<AssetReadyEventArgs>(nameof(IModEvents.Content), nameof(IContentEvents.AssetReady));
            this.LocaleChanged = ManageEventOf<LocaleChangedEventArgs>(nameof(IModEvents.Content), nameof(IContentEvents.LocaleChanged));
        }
    }
}
