using StardewModdingAPI.Enums;

namespace StardewModdingAPI
{
    /// <summary>Provides information about the current game state.</summary>
    public static class Context
    {
        /*********
        ** Fields
        *********/
        /// <summary>The current stage in the game's loading process.</summary>
        internal static LoadStage LoadStage { get; set; }


        /*********
        ** Accessors
        *********/
        /****
        ** Game/player state
        ****/
        /// <summary>Whether the player has loaded a save and the world has finished initializing.</summary>
        public static bool IsWorldReady { get; set; }
    }
}
