using System;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewModdingAPI.Framework
{
    /// <summary>SMAPI's extension of the game's core <see cref="GameRunner"/>, used to inject SMAPI components.</summary>
    internal class SGameRunner : GameRunner
    {
        /*********
        ** Fields
        *********/
        /// <summary>Raised before the game exits.</summary>
        private readonly Action OnGameExiting;


        /*********
        ** Public methods
        *********/
        /// <summary>The singleton instance.</summary>
        public static SGameRunner Instance => (SGameRunner)GameRunner.instance;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="onGameExiting">Raised before the game exits.</param>
        public SGameRunner(Action onGameExiting)
        {
            // init XNA
            Game1.graphics.GraphicsProfile = GraphicsProfile.HiDef;

            // init SMAPI
            this.OnGameExiting = onGameExiting;
        }

        /*********
        ** Protected methods
        *********/
        /// <summary>Perform cleanup logic when the game exits.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="args">The event args.</param>
        /// <remarks>This overrides the logic in <see cref="Game1.exitEvent"/> to let SMAPI clean up before exit.</remarks>
        protected override void OnExiting(object sender, EventArgs args)
        {
            this.OnGameExiting();
        }
    }
}
