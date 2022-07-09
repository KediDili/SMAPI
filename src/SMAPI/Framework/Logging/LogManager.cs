using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using StardewModdingAPI.Framework.Models;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Toolkit.Utilities;

namespace StardewModdingAPI.Framework.Logging
{
    /// <summary>Manages the SMAPI console window and log file.</summary>
    internal class LogManager
    {
        /*********
        ** Fields
        *********/
        /// <summary>Create a monitor instance given the ID and name.</summary>
        private readonly Func<string, string, Monitor> GetMonitorImpl;


        /*********
        ** Accessors
        *********/
        /// <summary>The core logger and monitor for SMAPI.</summary>
        public Monitor Monitor { get; }

        /// <summary>The core logger and monitor on behalf of the game.</summary>
        public Monitor MonitorForGame { get; }


        /*********
        ** Public methods
        *********/
        /****
        ** Initialization
        ****/
        /// <summary>Construct an instance.</summary>
        /// <param name="verboseLogging">The log contexts for which to enable verbose logging, which may show a lot more information to simplify troubleshooting.</param>
        /// <param name="getScreenIdForLog">Get the screen ID that should be logged to distinguish between players in split-screen mode, if any.</param>
        public LogManager(HashSet<string> verboseLogging, Func<int?> getScreenIdForLog)
        {
            // init monitor
            this.GetMonitorImpl = (id, name) => new Monitor(name, verboseLogging.Contains("*") || verboseLogging.Contains(id), getScreenIdForLog);
            this.Monitor = this.GetMonitor("SMAPI", "SMAPI");
            this.MonitorForGame = this.GetMonitor("game", "game");

            // enable Unicode handling on Windows
            // (the terminal defaults to UTF-8 on Linux/macOS)
#if SMAPI_FOR_WINDOWS
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;
#endif
        }

        /// <summary>Get a monitor instance derived from SMAPI's current settings.</summary>
        /// <param name="id">The unique ID for the mod context.</param>
        /// <param name="name">The name of the module which will log messages with this instance.</param>
        public Monitor GetMonitor(string id, string name)
        {
            return this.GetMonitorImpl(id, name);
        }

        /// <summary>Set the title of the SMAPI console window.</summary>
        /// <param name="title">The new window title.</param>
        public void SetConsoleTitle(string title)
        {
            Console.Title = title;
        }

        /****
        ** Console input
        ****/
        /// <summary>Show a 'press any key to exit' message, and exit when they press a key.</summary>
        public void PressAnyKeyToExit()
        {
            this.Monitor.Log("Game has ended. Press any key to exit.", LogLevel.Info);
            this.PressAnyKeyToExit(showMessage: false);
        }

        /// <summary>Show a 'press any key to exit' message, and exit when they press a key.</summary>
        /// <param name="showMessage">Whether to print a 'press any key to exit' message to the console.</param>
        public void PressAnyKeyToExit(bool showMessage)
        {
            if (showMessage)
                this.Monitor.Log("Game has ended. Press any key to exit.");
            Thread.Sleep(100);
            Console.ReadKey();
            Environment.Exit(0);
        }

        /****
        ** Crash/update handling
        ****/
        /// <summary>Check whether SMAPI crashed or detected an update during the last session, and display them in the SMAPI console.</summary>
        public void HandleMarkerFiles()
        {
            // show update alert
            if (File.Exists(Constants.UpdateMarker))
            {
                string[] rawUpdateFound = File.ReadAllText(Constants.UpdateMarker).Split(new[] { '|' }, 2);
                if (SemanticVersion.TryParse(rawUpdateFound[0], out ISemanticVersion? updateFound))
                {
                    if (Constants.ApiVersion.IsPrerelease() && updateFound.IsNewerThan(Constants.ApiVersion))
                    {
                        string url = rawUpdateFound.Length > 1
                            ? rawUpdateFound[1]
                            : Constants.HomePageUrl;

                        this.Monitor.Log("A new version of SMAPI was detected last time you played.", LogLevel.Error);
                        this.Monitor.Log($"You can update to {updateFound}: {url}.", LogLevel.Error);
                        this.Monitor.Log("Press any key to continue playing anyway. (This only appears when using a SMAPI beta.)", LogLevel.Info);
                        Console.ReadKey();
                    }
                }
                File.Delete(Constants.UpdateMarker);
            }

            // show details if game crashed during last session
            if (File.Exists(Constants.FatalCrashMarker))
            {
                this.Monitor.Log("The game crashed last time you played. If it happens repeatedly, see 'get help' on https://smapi.io.", LogLevel.Error);
                this.Monitor.Log("If you ask for help, make sure to share your SMAPI log: https://smapi.io/log.", LogLevel.Error);
                this.Monitor.Log("Press any key to delete the crash data and continue playing.", LogLevel.Info);
                Console.ReadKey();
                File.Delete(Constants.FatalCrashLog);
                File.Delete(Constants.FatalCrashMarker);
            }
        }

        /// <summary>Log a fatal exception which prevents SMAPI from launching.</summary>
        /// <param name="exception">The exception details.</param>
        public void LogFatalLaunchError(Exception exception)
        {
            this.MonitorForGame.Log($"The game failed to launch: {exception.GetLogSummary()}", LogLevel.Error);
        }

        /****
        ** General log output
        ****/
        /// <summary>Log the initial header with general SMAPI and system details.</summary>
        /// <param name="customSettings">The custom SMAPI settings.</param>
        public void LogIntro(IDictionary<string, object?> customSettings)
        {
            // log platform
            this.Monitor.Log($"SMAPI {Constants.ApiVersion} with Stardew Valley {Constants.GameVersion} (build {Constants.GetBuildVersionLabel()}) on {EnvironmentUtility.GetFriendlyPlatformName(Constants.Platform)}", LogLevel.Info);

            // log basic info
            this.Monitor.Log($"Log started at {DateTime.UtcNow:s} UTC");

            // log custom settings
            if (customSettings.Any())
                this.Monitor.Log($"Loaded with custom settings: {string.Join(", ", customSettings.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"))}");
        }

        /// <summary>Log details for settings that don't match the default.</summary>
        /// <param name="settings">The settings to log.</param>
        public void LogSettingsHeader(SConfig settings)
        {
            // warnings
            if (!settings.CheckForUpdates)
                this.Monitor.Log("You disabled update checks, so you won't be notified of new SMAPI or mod updates. Running an old version of SMAPI is not recommended. You can undo this by reinstalling SMAPI.", LogLevel.Warn);
            if (!settings.RewriteMods)
                this.Monitor.Log("You disabled rewriting broken mods, so many older mods may fail to load. You can undo this by reinstalling SMAPI.", LogLevel.Info);

            // verbose logging
            this.Monitor.VerboseLog("Verbose logging enabled.");
        }
    }
}
