using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Security;
#if SMAPI_FOR_WINDOWS
#endif
using Newtonsoft.Json;
using StardewModdingAPI.Framework.Serialization;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Toolkit;
using StardewValley;

namespace StardewModdingAPI.Framework
{
    /// <summary>The core class which initializes and manages SMAPI.</summary>
    internal class SCore : IDisposable
    {
        /*********
        ** Fields
        *********/
        /****
        ** Low-level components
        ****/
        /// <summary>The mod toolkit used for generic mod interactions.</summary>
        private readonly ModToolkit Toolkit = new();

        /****
        ** Higher-level components
        ****/
        /// <summary>The underlying game instance.</summary>
        private GameRunner Game = null!; // initialized very early

        /****
        ** State
        ****/
        /// <summary>Whether the program has been disposed.</summary>
        private bool IsDisposed;

        /*********
        ** Accessors
        *********/
        /// <summary>The singleton instance.</summary>
        /// <remarks>This is only intended for use by external code like the Error Handler mod.</remarks>
        internal static SCore Instance { get; private set; } = null!; // initialized in constructor, which happens before other code can access it


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        public SCore()
        {
            SCore.Instance = this;
        }

        /// <summary>Launch SMAPI.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical] // let try..catch handle corrupted state exceptions
        public void RunInteractively()
        {
            // initialize SMAPI
            try
            {
                JsonConverter[] converters = {
                    new ColorConverter(),
                    new PointConverter(),
                    new Vector2Converter(),
                    new RectangleConverter()
                };
                foreach (JsonConverter converter in converters)
                    this.Toolkit.JsonHelper.JsonSettings.Converters.Add(converter);

                // add error handlers
                AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine($"Critical app domain exception: {e.ExceptionObject}");

                // override game
                this.Game = new GameRunner();
                StardewValley.GameRunner.instance = this.Game;

                // set window titles
                this.UpdateWindowTitles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SMAPI failed to initialize: {ex.GetLogSummary()}");
                Console.ReadLine();
                return;
            }

            // set window titles
            this.UpdateWindowTitles();

            // start game
            try
            {
                StardewValley.Program.releaseBuild = true; // game's debug logic interferes with SMAPI opening the game window
                this.Game.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadLine();
            }
            finally
            {
                try
                {
                    this.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"The game ended, but SMAPI wasn't able to dispose correctly. Technical details: {ex}");
                }
            }
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract", Justification = "May be disposed before SMAPI is fully initialized.")]
        public void Dispose()
        {
            // skip if already disposed
            if (this.IsDisposed)
                return;
            this.IsDisposed = true;

            // dispose core components
            this.Game?.Dispose();

            // end game (moved from Game1.OnExiting to let us clean up first)
            Process.GetCurrentProcess().Kill();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Set the titles for the game and console windows.</summary>
        private void UpdateWindowTitles()
        {
            string consoleTitle = $"SMAPI {Constants.ApiVersion} - running Stardew Valley {Constants.GameVersion}";
            string gameTitle = $"Stardew Valley {Constants.GameVersion} - running SMAPI {Constants.ApiVersion}";

            this.Game.Window.Title = gameTitle;
        }
    }
}
