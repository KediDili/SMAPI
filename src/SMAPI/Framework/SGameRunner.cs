using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
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
        /// <summary>Raised after the game finishes loading its initial content.</summary>
        private readonly Action OnGameContentLoaded;

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
        /// <param name="onGameContentLoaded">Raised after the game finishes loading its initial content.</param>
        /// <param name="onGameExiting">Raised before the game exits.</param>
        public SGameRunner(Action onGameContentLoaded, Action onGameExiting)
        {
            // init XNA
            Game1.graphics.GraphicsProfile = GraphicsProfile.HiDef;

            // init SMAPI
            this.OnGameContentLoaded = onGameContentLoaded;
            this.OnGameExiting = onGameExiting;
        }

        /// <summary>Create a game instance for a local player.</summary>
        /// <param name="playerIndex">The player index.</param>
        /// <param name="instanceIndex">The instance index.</param>
        public override Game1 CreateGameInstance(PlayerIndex playerIndex = PlayerIndex.One, int instanceIndex = 0)
        {
            return new SGame(playerIndex, instanceIndex, this.OnGameContentLoaded);
        }

        /// <inheritdoc />
        public override void AddGameInstance(PlayerIndex playerIndex)
        {
            base.AddGameInstance(playerIndex);

            EarlyConstants.LogScreenId = Context.ScreenId;
            this.UpdateForSplitScreenChanges();
        }

        /// <inheritdoc />
        public override void RemoveGameInstance(Game1 gameInstance)
        {
            base.RemoveGameInstance(gameInstance);

            if (this.gameInstances.Count <= 1)
                EarlyConstants.LogScreenId = null;
            this.UpdateForSplitScreenChanges();
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

        /// <summary>Update metadata when a split screen is added or removed.</summary>
        private void UpdateForSplitScreenChanges()
        {
            HashSet<int> oldScreenIds = new(Context.ActiveScreenIds);

            // track active screens
            Context.ActiveScreenIds.Clear();
            foreach (var screen in this.gameInstances)
                Context.ActiveScreenIds.Add(screen.instanceId);

            // remember last removed screen
            foreach (int id in oldScreenIds)
            {
                if (!Context.ActiveScreenIds.Contains(id))
                    Context.LastRemovedScreenId = id;
            }
        }
    }
}
