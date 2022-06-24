using StardewModdingAPI.Events;

namespace StardewModdingAPI.Framework.Events
{
    /// <inheritdoc />
    internal class ModEvents : IModEvents
    {
        /*********
        ** Accessors
        *********/
        /// <inheritdoc />
        public IContentEvents Content { get; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="mod">The mod which uses this instance.</param>
        /// <param name="eventManager">The underlying event manager.</param>
        public ModEvents(IModMetadata mod, EventManager eventManager)
        {
            this.Content = new ModContentEvents(mod, eventManager);
        }
    }
}
