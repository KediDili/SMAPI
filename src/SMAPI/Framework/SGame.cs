using System;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewModdingAPI.Framework
{
    /// <summary>SMAPI's extension of the game's core <see cref="Game1"/>, used to inject events.</summary>
    internal class SGame : Game1
    {
        /*********
        ** Fields
        *********/
        /// <summary>Raised after the instance finishes loading its initial content.</summary>
        private readonly Action OnContentLoaded;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="playerIndex">The player index.</param>
        /// <param name="instanceIndex">The instance index.</param>
        /// <param name="onContentLoaded">Raised after the game finishes loading its initial content.</param>
        public SGame(PlayerIndex playerIndex, int instanceIndex, Action onContentLoaded)
            : base(playerIndex, instanceIndex)
        {
            // init XNA
            Game1.graphics.GraphicsProfile = GraphicsProfile.HiDef;

            // hook into game
            this._locations = new ObservableCollection<GameLocation>();

            // init SMAPI
            this.OnContentLoaded = onContentLoaded;
        }

        /// <inheritdoc />
        protected override void LoadContent()
        {
            base.LoadContent();

            this.OnContentLoaded();
        }
    }
}
