using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
#if SMAPI_FOR_WINDOWS
using Microsoft.Win32;
#endif
using Newtonsoft.Json;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Framework.Deprecations;
using StardewModdingAPI.Framework.Exceptions;
using StardewModdingAPI.Framework.Logging;
using StardewModdingAPI.Framework.Models;
using StardewModdingAPI.Framework.ModHelpers;
using StardewModdingAPI.Framework.ModLoading;
using StardewModdingAPI.Framework.Reflection;
using StardewModdingAPI.Framework.Serialization;
using StardewModdingAPI.Framework.Utilities;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Framework.Clients.WebApi;
using StardewModdingAPI.Toolkit.Framework.ModData;
using StardewModdingAPI.Toolkit.Serialization;
using StardewModdingAPI.Toolkit.Utilities;
using StardewModdingAPI.Toolkit.Utilities.PathLookups;
using StardewModdingAPI.Utilities;
using StardewValley;
using LanguageCode = StardewValley.LocalizedContentManager.LanguageCode;
using PathUtilities = StardewModdingAPI.Toolkit.Utilities.PathUtilities;
using SObject = StardewValley.Object;

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
        /// <summary>Whether the game should exit immediately and any pending initialization should be cancelled.</summary>
        private bool IsExiting;

        /// <summary>Manages the SMAPI console window and log file.</summary>
        private readonly LogManager LogManager;

        /// <summary>The core logger and monitor for SMAPI.</summary>
        private Monitor Monitor => this.LogManager.Monitor;

        /// <summary>Simplifies access to private game code.</summary>
        private readonly Reflector Reflection = new();

        /// <summary>Encapsulates access to SMAPI core translations.</summary>
        private readonly Translator Translator = new();

        /// <summary>The SMAPI configuration settings.</summary>
        private readonly SConfig Settings;

        /// <summary>The mod toolkit used for generic mod interactions.</summary>
        private readonly ModToolkit Toolkit = new();

        /****
        ** Higher-level components
        ****/
        /// <summary>The underlying game instance.</summary>
        private SGameRunner Game = null!; // initialized very early

        /// <summary>Tracks the installed mods.</summary>
        /// <remarks>This is initialized after the game starts.</remarks>
        private readonly ModRegistry ModRegistry = new();


        /****
        ** State
        ****/
        /// <summary>The path to search for mods.</summary>
        private string ModsPath => Constants.ModsPath;

        /// <summary>Whether the game is currently running.</summary>
        private bool IsGameRunning;

        /// <summary>Whether the program has been disposed.</summary>
        private bool IsDisposed;

        /// <summary>Whether post-game-startup initialization has been performed.</summary>
        private bool IsInitialized;

        /// <summary>The last language set by the game.</summary>
        private (string Locale, LanguageCode Code) LastLanguage { get; set; } = ("", LanguageCode.en);

        /// <summary>The maximum number of consecutive attempts SMAPI should make to recover from an update error.</summary>
        private readonly Countdown UpdateCrashTimer = new(60); // 60 ticks = roughly one second

        /*********
        ** Accessors
        *********/
        /// <summary>Manages deprecation warnings.</summary>
        /// <remarks>This is initialized after the game starts. This is accessed directly because it's not part of the normal class model.</remarks>
        internal static DeprecationManager DeprecationManager { get; private set; } = null!; // initialized in constructor, which happens before other code can access it

        /// <summary>The singleton instance.</summary>
        /// <remarks>This is only intended for use by external code like the Error Handler mod.</remarks>
        internal static SCore Instance { get; private set; } = null!; // initialized in constructor, which happens before other code can access it

        /// <summary>The number of game update ticks which have already executed. This is similar to <see cref="Game1.ticks"/>, but incremented more consistently for every tick.</summary>
        internal static uint TicksElapsed { get; private set; }

        /// <summary>A specialized form of <see cref="TicksElapsed"/> which is incremented each time SMAPI performs a processing tick (whether that's a game update, one wait cycle while synchronizing code, etc).</summary>
        internal static uint ProcessTicksElapsed { get; private set; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="modsPath">The path to search for mods.</param>
        /// <param name="writeToConsole">Whether to output log messages to the console.</param>
        /// <param name="developerMode">Whether to enable development features, or <c>null</c> to use the value from the settings file.</param>
        public SCore(string modsPath, bool writeToConsole, bool? developerMode)
        {
            SCore.Instance = this;

            // init paths
            this.VerifyPath(modsPath);
            this.VerifyPath(Constants.LogDir);
            Constants.ModsPath = modsPath;

            // init log file
            this.PurgeNormalLogs();
            string logPath = this.GetLogPath();

            // init basics
            this.Settings = JsonConvert.DeserializeObject<SConfig>(File.ReadAllText(Constants.ApiConfigPath)) ?? throw new InvalidOperationException("The 'smapi-internal/config.json' file is missing or invalid. You can reinstall SMAPI to fix this.");
            if (File.Exists(Constants.ApiUserConfigPath))
                JsonConvert.PopulateObject(File.ReadAllText(Constants.ApiUserConfigPath), this.Settings);
            if (developerMode.HasValue)
                this.Settings.OverrideDeveloperMode(developerMode.Value);

            this.LogManager = new LogManager(logPath: logPath, colorConfig: this.Settings.ConsoleColors, writeToConsole: writeToConsole, verboseLogging: this.Settings.VerboseLogging, isDeveloperMode: this.Settings.DeveloperMode, getScreenIdForLog: this.GetScreenIdForLog);
            SCore.DeprecationManager = new DeprecationManager(this.Monitor, this.ModRegistry);
            SDate.Translations = this.Translator;

            // log SMAPI/OS info
            this.LogManager.LogIntro(modsPath, this.Settings.GetCustomSettings());

            // validate platform
#if SMAPI_FOR_WINDOWS
            if (Constants.Platform != Platform.Windows)
            {
                this.Monitor.Log("Oops! You're running Windows, but this version of SMAPI is for Linux or macOS. Please reinstall SMAPI to fix this.", LogLevel.Error);
                this.LogManager.PressAnyKeyToExit();
            }
#else
            if (Constants.Platform == Platform.Windows)
            {
                this.Monitor.Log($"Oops! You're running {Constants.Platform}, but this version of SMAPI is for Windows. Please reinstall SMAPI to fix this.", LogLevel.Error);
                this.LogManager.PressAnyKeyToExit();
            }
#endif
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
                AppDomain.CurrentDomain.UnhandledException += (_, e) => this.Monitor.Log($"Critical app domain exception: {e.ExceptionObject}", LogLevel.Error);

                // add more lenient assembly resolver
                AppDomain.CurrentDomain.AssemblyResolve += (_, e) => AssemblyLoader.ResolveAssembly(e.Name);

                // override game
                this.Game = new SGameRunner(
                    monitor: this.Monitor,
                    reflection: this.Reflection,
                    modHooks: new SModHooks(this.OnNewDayAfterFade, this.Monitor),
                    exitGameImmediately: this.ExitGameImmediately,

                    onGameContentLoaded: this.OnInstanceContentLoaded,
                    onGameUpdating: this.OnGameUpdating,
                    onPlayerInstanceUpdating: this.OnPlayerInstanceUpdating,
                    onGameExiting: this.OnGameExiting
                );
                StardewValley.GameRunner.instance = this.Game;

                // set window titles
                this.UpdateWindowTitles();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"SMAPI failed to initialize: {ex.GetLogSummary()}", LogLevel.Error);
                this.LogManager.PressAnyKeyToExit();
                return;
            }

            // log basic info
            this.LogManager.HandleMarkerFiles();
            this.LogManager.LogSettingsHeader(this.Settings);

            // set window titles
            this.UpdateWindowTitles();

            // start game
            this.Monitor.Log("Waiting for game to launch...", LogLevel.Debug);
            try
            {
                this.IsGameRunning = true;
                StardewValley.Program.releaseBuild = true; // game's debug logic interferes with SMAPI opening the game window
                this.Game.Run();
            }
            catch (Exception ex)
            {
                this.LogManager.LogFatalLaunchError(ex);
                this.LogManager.PressAnyKeyToExit();
            }
            finally
            {
                try
                {
                    this.Dispose();
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"The game ended, but SMAPI wasn't able to dispose correctly. Technical details: {ex}", LogLevel.Error);
                }
            }
        }

        /// <summary>Get the core logger and monitor on behalf of the game.</summary>
        /// <remarks>This method is called using reflection by the ErrorHandler mod to log game errors.</remarks>
        [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Used via reflection")]
        public IMonitor GetMonitorForGame()
        {
            return this.LogManager.MonitorForGame;
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract", Justification = "May be disposed before SMAPI is fully initialized.")]
        public void Dispose()
        {
            // skip if already disposed
            if (this.IsDisposed)
                return;
            this.IsDisposed = true;
            this.Monitor.Log("Disposing...");

            // dispose mod data
            foreach (IModMetadata mod in this.ModRegistry.GetAll())
            {
                try
                {
                    (mod.Mod as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    mod.LogAsMod($"Mod failed during disposal: {ex.GetLogSummary()}.", LogLevel.Warn);
                }
            }

            // dispose core components
            this.IsGameRunning = false;
            this.IsExiting = true;
            this.Game?.Dispose();
            this.LogManager.Dispose(); // dispose last to allow for any last-second log messages

            // end game (moved from Game1.OnExiting to let us clean up first)
            Process.GetCurrentProcess().Kill();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Initialize mods before the first game asset is loaded. At this point the core content managers are loaded (so mods can load their own assets), but the game is mostly uninitialized.</summary>
        private void InitializeBeforeFirstAssetLoaded()
        {
            if (this.IsExiting)
            {
                this.Monitor.Log("SMAPI shutting down: aborting initialization.", LogLevel.Warn);
                return;
            }

            // init TMX support
            xTile.Format.FormatManager.Instance.RegisterMapFormat(new TMXTile.TMXFormat(Game1.tileSize / Game1.pixelZoom, Game1.tileSize / Game1.pixelZoom, Game1.pixelZoom, Game1.pixelZoom));

            // load mod data
            ModToolkit toolkit = new();
            ModDatabase modDatabase = toolkit.GetModDatabase(Constants.ApiMetadataPath);

            // load mods
            {
                this.Monitor.Log("Loading mod metadata...", LogLevel.Debug);
                ModResolver resolver = new();

                // log loose files
                {
                    string[] looseFiles = new DirectoryInfo(this.ModsPath).GetFiles().Select(p => p.Name).ToArray();
                    if (looseFiles.Any())
                        this.Monitor.Log($"  Ignored loose files: {string.Join(", ", looseFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))}");
                }

                // load manifests
                IModMetadata[] mods = resolver.ReadManifests(toolkit, this.ModsPath, modDatabase, useCaseInsensitiveFilePaths: this.Settings.UseCaseInsensitivePaths).ToArray();

                // filter out ignored mods
                foreach (IModMetadata mod in mods.Where(p => p.IsIgnored))
                    this.Monitor.Log($"  Skipped {mod.GetRelativePathWithRoot()} (folder name starts with a dot).");
                mods = mods.Where(p => !p.IsIgnored).ToArray();

                // load mods
                resolver.ValidateManifests(mods, Constants.ApiVersion, toolkit.GetUpdateUrl, getFileLookup: this.GetFileLookup);
                mods = resolver.ProcessDependencies(mods, modDatabase).ToArray();
                this.LoadMods(mods, this.Toolkit.JsonHelper, modDatabase);

                // check for software likely to cause issues
                this.CheckForSoftwareConflicts();

                // check for updates
                _ = this.CheckForUpdatesAsync(mods); // ignore task since the main thread doesn't need to wait for it
            }

            // update window titles
            this.UpdateWindowTitles();
        }

        /// <summary>Raised after the game finishes initializing.</summary>
        private void OnGameInitialized()
        {
            // validate XNB integrity
            if (!this.ValidateContentIntegrity())
                this.Monitor.Log("SMAPI found problems in your game's content files which are likely to cause errors or crashes. Consider uninstalling XNB mods or reinstalling the game.", LogLevel.Error);

            this.InitializeBeforeFirstAssetLoaded();
        }

        /// <summary>Raised after an instance finishes loading its initial content.</summary>
        private void OnInstanceContentLoaded()
        {
            // log GPU info
#if SMAPI_FOR_WINDOWS
            this.Monitor.Log($"Running on GPU: {Game1.game1.GraphicsDevice?.Adapter?.Description ?? "<unknown>"}");
#endif
        }

        /// <summary>Raised when the game is updating its state (roughly 60 times per second).</summary>
        /// <param name="gameTime">A snapshot of the game timing state.</param>
        /// <param name="runGameUpdate">Invoke the game's update logic.</param>
        private void OnGameUpdating(GameTime gameTime, Action runGameUpdate)
        {
            try
            {
                /*********
                ** Safe queued work
                *********/
                // print warnings/alerts
                SCore.DeprecationManager.PrintQueued();

                /*********
                ** First-tick initialization
                *********/
                if (!this.IsInitialized)
                {
                    this.IsInitialized = true;
                    this.OnGameInitialized();
                }

                /*********
                ** Special cases
                *********/
                // Abort if SMAPI is exiting.
                if (this.IsExiting)
                {
                    this.Monitor.Log("SMAPI shutting down: aborting update.");
                    return;
                }

                /*********
                ** Run game update
                *********/
                runGameUpdate();

                /*********
                ** Reset crash timer
                *********/
                this.UpdateCrashTimer.Reset();
            }
            catch (Exception ex)
            {
                // log error
                this.Monitor.Log($"An error occurred in the overridden update loop: {ex.GetLogSummary()}", LogLevel.Error);

                // exit if irrecoverable
                if (!this.UpdateCrashTimer.Decrement())
                    this.ExitGameImmediately("The game crashed when updating, and SMAPI was unable to recover the game.");
            }
            finally
            {
                SCore.TicksElapsed++;
                SCore.ProcessTicksElapsed++;
            }
        }

        /// <summary>Raised when the game instance for a local player is updating (once per <see cref="OnGameUpdating"/> per player).</summary>
        /// <param name="instance">The game instance being updated.</param>
        /// <param name="gameTime">A snapshot of the game timing state.</param>
        /// <param name="runUpdate">Invoke the game's update logic.</param>
        private void OnPlayerInstanceUpdating(SGame instance, GameTime gameTime, Action runUpdate)
        {
            try
            {
                /*********
                ** Special cases
                *********/
                // Run async tasks synchronously to avoid issues due to mod events triggering
                // concurrently with game code.
                bool saveParsed = false;
                if (Game1.currentLoader != null)
                {
                    this.Monitor.Log("Game loader synchronizing...");
                    this.Reflection.GetMethod(Game1.game1, "UpdateTitleScreen").Invoke(Game1.currentGameTime); // run game logic to change music on load, etc
                    // ReSharper disable once ConstantConditionalAccessQualifier -- may become null within the loop
                    while (Game1.currentLoader?.MoveNext() == true)
                    {
                        SCore.ProcessTicksElapsed++;

                        // raise load stage changed
                        switch (Game1.currentLoader.Current)
                        {
                            case 20 when (!saveParsed && SaveGame.loaded != null):
                                saveParsed = true;
                                this.OnLoadStageChanged(LoadStage.SaveParsed);
                                break;

                            case 36:
                                this.OnLoadStageChanged(LoadStage.SaveLoadedBasicInfo);
                                break;

                            case 50:
                                this.OnLoadStageChanged(LoadStage.SaveLoadedLocations);
                                break;

                            default:
                                if (Game1.gameMode == Game1.playingGameMode)
                                    this.OnLoadStageChanged(LoadStage.Preloaded);
                                break;
                        }
                    }

                    Game1.currentLoader = null;
                    this.Monitor.Log("Game loader done.");
                }

                // While a background task is in progress, the game may make changes to the game
                // state while mods are running their code. This is risky, because data changes can
                // conflict (e.g. collection changed during enumeration errors) and data may change
                // unexpectedly from one mod instruction to the next.
                //
                // Therefore we can just run Game1.Update here without raising any SMAPI events. There's
                // a small chance that the task will finish after we defer but before the game checks,
                // which means technically events should be raised, but the effects of missing one
                // update tick are negligible and not worth the complications of bypassing Game1.Update.
                if (Game1.gameMode == Game1.loadingMode)
                {
                    runUpdate();
                    return;
                }

                // Raise minimal events while saving.
                // While the game is writing to the save file in the background, mods can unexpectedly
                // fail since they don't have exclusive access to resources (e.g. collection changed
                // during enumeration errors). To avoid problems, events are not invoked while a save
                // is in progress. It's safe to raise SaveEvents.BeforeSave as soon as the menu is
                // opened (since the save hasn't started yet), but all other events should be suppressed.
                if (Context.IsSaving)
                {
                    // raise before-create
                    if (!Context.IsWorldReady && !instance.IsBetweenCreateEvents)
                    {
                        instance.IsBetweenCreateEvents = true;
                    }

                    // raise before-save
                    if (Context.IsWorldReady && !instance.IsBetweenSaveEvents)
                    {
                        instance.IsBetweenSaveEvents = true;
                    }

                    // suppress non-save events
                    runUpdate();
                    return;
                }

                /*********
                ** Update context
                *********/
                bool wasWorldReady = Context.IsWorldReady;
                if ((Context.IsWorldReady && !Context.IsSaveLoaded) || Game1.exitToTitle)
                {
                    Context.IsWorldReady = false;
                    instance.AfterLoadTimer.Reset();
                }
                else if (Context.IsSaveLoaded && instance.AfterLoadTimer.Current > 0 && Game1.currentLocation != null)
                {
                    if (Game1.dayOfMonth != 0) // wait until new-game intro finishes (world not fully initialized yet)
                        instance.AfterLoadTimer.Decrement();
                    Context.IsWorldReady = instance.AfterLoadTimer.Current == 0;
                }

                /*********
                ** Pre-update events
                *********/
                {
                    /*********
                    ** Save created/loaded events
                    *********/
                    if (instance.IsBetweenCreateEvents)
                    {
                        // raise after-create
                        instance.IsBetweenCreateEvents = false;
                        this.Monitor.Log($"Context: after save creation, starting {Game1.currentSeason} {Game1.dayOfMonth} Y{Game1.year}.");
                        this.OnLoadStageChanged(LoadStage.CreatedSaveFile);
                    }

                    if (instance.IsBetweenSaveEvents)
                    {
                        // raise after-save
                        instance.IsBetweenSaveEvents = false;
                        this.Monitor.Log($"Context: after save, starting {Game1.currentSeason} {Game1.dayOfMonth} Y{Game1.year}.");
                    }

                    /*********
                    ** Load / return-to-title events
                    *********/
                    if (wasWorldReady && !Context.IsWorldReady)
                        this.OnLoadStageChanged(LoadStage.None);
                    else if (Context.IsWorldReady && Context.LoadStage != LoadStage.Ready)
                    {
                        // print context
                        string context = $"Context: loaded save '{Constants.SaveFolderName}', starting {Game1.currentSeason} {Game1.dayOfMonth} Y{Game1.year}.";
                        if (Context.IsMultiplayer)
                        {
                            int onlineCount = Game1.getOnlineFarmers().Count();
                            context += $" {(Context.IsMainPlayer ? "Main player" : "Farmhand")} with {onlineCount} {(onlineCount == 1 ? "player" : "players")} online.";
                        }
                        else
                            context += " Single-player.";

                        this.Monitor.Log(context);

                        // raise events
                        this.OnLoadStageChanged(LoadStage.Ready);
                    }


                    /*********
                    ** Game update
                    *********/
                    // game launched (not raised for secondary players in split-screen mode)
                    if (instance.IsFirstTick && !Context.IsGameLaunched)
                        Context.IsGameLaunched = true;

                    // preloaded
                    if (Context.IsSaveLoaded && Context.LoadStage != LoadStage.Loaded && Context.LoadStage != LoadStage.Ready && Game1.dayOfMonth != 0)
                        this.OnLoadStageChanged(LoadStage.Loaded);
                }

                /*********
                ** Game update tick
                *********/
                try
                {
                    runUpdate();
                }
                catch (Exception ex)
                {
                    this.LogManager.MonitorForGame.Log($"An error occurred in the base update loop: {ex.GetLogSummary()}", LogLevel.Error);
                }

                /*********
                ** Update events
                *********/
                this.UpdateCrashTimer.Reset();
            }
            catch (Exception ex)
            {
                // log error
                this.Monitor.Log($"An error occurred in the overridden update loop: {ex.GetLogSummary()}", LogLevel.Error);

                // exit if irrecoverable
                if (!this.UpdateCrashTimer.Decrement())
                    this.ExitGameImmediately("The game crashed when updating, and SMAPI was unable to recover the game.");
            }
        }

        /// <summary>Raised when the low-level stage while loading a save changes.</summary>
        /// <param name="newStage">The new load stage.</param>
        internal void OnLoadStageChanged(LoadStage newStage)
        {
            // nothing to do
            if (newStage == Context.LoadStage)
                return;

            // update data
            Context.LoadStage = newStage;
            this.Monitor.VerboseLog($"Context: load stage changed to {newStage}");
        }

        /// <summary>A callback invoked before <see cref="Game1.newDayAfterFade"/> runs.</summary>
        protected void OnNewDayAfterFade()
        {
            this.Reflection.NewCacheInterval();
        }


        /// <summary>Raised before the game exits.</summary>
        private void OnGameExiting()
        {
            this.Dispose();
        }

        /// <summary>Look for common issues with the game's XNB content, and log warnings if anything looks broken or outdated.</summary>
        /// <returns>Returns whether all integrity checks passed.</returns>
        private bool ValidateContentIntegrity()
        {
            this.Monitor.Log("Detecting common issues...");
            bool issuesFound = false;

            // object format (commonly broken by outdated files)
            {
                // detect issues
                bool hasObjectIssues = false;
                void LogIssue(int id, string issue) => this.Monitor.Log($@"Detected issue: item #{id} in Content\Data\ObjectInformation.xnb is invalid ({issue}).");
                foreach ((int id, string? fieldsStr) in Game1.objectInformation)
                {
                    // must not be empty
                    if (string.IsNullOrWhiteSpace(fieldsStr))
                    {
                        LogIssue(id, "entry is empty");
                        hasObjectIssues = true;
                        continue;
                    }

                    // require core fields
                    string[] fields = fieldsStr.Split('/');
                    if (fields.Length < SObject.objectInfoDescriptionIndex + 1)
                    {
                        LogIssue(id, "too few fields for an object");
                        hasObjectIssues = true;
                        continue;
                    }

                    // check min length for specific types
                    switch (fields[SObject.objectInfoTypeIndex].Split(new[] { ' ' }, 2)[0])
                    {
                        case "Cooking":
                            if (fields.Length < SObject.objectInfoBuffDurationIndex + 1)
                            {
                                LogIssue(id, "too few fields for a cooking item");
                                hasObjectIssues = true;
                            }
                            break;
                    }
                }

                // log error
                if (hasObjectIssues)
                {
                    issuesFound = true;
                    this.Monitor.Log(@"Your Content\Data\ObjectInformation.xnb file seems to be broken or outdated.", LogLevel.Warn);
                }
            }

            return !issuesFound;
        }

        /// <summary>Set the titles for the game and console windows.</summary>
        private void UpdateWindowTitles()
        {
            string consoleTitle = $"SMAPI {Constants.ApiVersion} - running Stardew Valley {Constants.GameVersion}";
            string gameTitle = $"Stardew Valley {Constants.GameVersion} - running SMAPI {Constants.ApiVersion}";

            if (this.ModRegistry.AreAllModsLoaded)
            {
                int modsLoaded = this.ModRegistry.GetAll().Count();
                consoleTitle += $" with {modsLoaded} mods";
                gameTitle += $" with {modsLoaded} mods";
            }

            this.Game.Window.Title = gameTitle;
            this.LogManager.SetConsoleTitle(consoleTitle);
        }

        /// <summary>Log a warning if software known to cause issues is installed.</summary>
        private void CheckForSoftwareConflicts()
        {
#if SMAPI_FOR_WINDOWS
            this.Monitor.Log("Checking for known software conflicts...");

            try
            {
                string[] registryKeys = { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" };

                string[] installedNames = registryKeys
                    .SelectMany(registryKey =>
                    {
                        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(registryKey);
                        if (key == null)
                            return Array.Empty<string>();

                        return key
                            .GetSubKeyNames()
                            .Select(subkeyName =>
                            {
                                using RegistryKey? subkey = key.OpenSubKey(subkeyName);
                                string? displayName = (string?)subkey?.GetValue("DisplayName");
                                string? displayVersion = (string?)subkey?.GetValue("DisplayVersion");

                                if (displayName != null && displayVersion != null && displayName.EndsWith($" {displayVersion}"))
                                    displayName = displayName.Substring(0, displayName.Length - displayVersion.Length - 1);

                                return displayName;
                            })
                            .ToArray();
                    })
                    .Where(name => name != null && (name.Contains("MSI Afterburner") || name.Contains("RivaTuner")))
                    .Select(name => name!)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToArray();

                if (installedNames.Any())
                    this.Monitor.Log($"Found {string.Join(" and ", installedNames)} installed, which may conflict with SMAPI. If you experience errors or crashes, try disabling that software or adding an exception for SMAPI and Stardew Valley.", LogLevel.Warn);
                else
                    this.Monitor.Log("   None found!");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed when checking for conflicting software. Technical details:\n{ex}");
            }
#endif
        }

        /// <summary>Asynchronously check for a new version of SMAPI and any installed mods, and print alerts to the console if an update is available.</summary>
        /// <param name="mods">The mods to include in the update check (if eligible).</param>
        private async Task CheckForUpdatesAsync(IModMetadata[] mods)
        {
            try
            {
                if (!this.Settings.CheckForUpdates)
                    return;

                // create client
                using WebApiClient client = new(this.Settings.WebApiBaseUrl, Constants.ApiVersion);
                this.Monitor.Log("Checking for updates...");

                // check SMAPI version
                {
                    ISemanticVersion? updateFound = null;
                    string? updateUrl = null;
                    try
                    {
                        // fetch update check
                        IDictionary<string, ModEntryModel> response = await client.GetModInfoAsync(
                            mods: new[] { new ModSearchEntryModel("Pathoschild.SMAPI", Constants.ApiVersion, new[] { $"GitHub:{this.Settings.GitHubProjectName}" }) },
                            apiVersion: Constants.ApiVersion,
                            gameVersion: Constants.GameVersion,
                            platform: Constants.Platform
                        );
                        ModEntryModel updateInfo = response.Single().Value;
                        updateFound = updateInfo.SuggestedUpdate?.Version;
                        updateUrl = updateInfo.SuggestedUpdate?.Url;

                        // log message
                        if (updateFound != null)
                            this.Monitor.Log($"You can update SMAPI to {updateFound}: {updateUrl}", LogLevel.Alert);
                        else
                            this.Monitor.Log("   SMAPI okay.");

                        // show errors
                        if (updateInfo.Errors.Any())
                        {
                            this.Monitor.Log("Couldn't check for a new version of SMAPI. This won't affect your game, but you may not be notified of new versions if this keeps happening.", LogLevel.Warn);
                            this.Monitor.Log($"Error: {string.Join("\n", updateInfo.Errors)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log("Couldn't check for a new version of SMAPI. This won't affect your game, but you won't be notified of new versions if this keeps happening.", LogLevel.Warn);
                        this.Monitor.Log(ex is WebException && ex.InnerException == null
                            ? $"Error: {ex.Message}"
                            : $"Error: {ex.GetLogSummary()}"
                        );
                    }

                    // show update message on next launch
                    if (updateFound != null)
                        this.LogManager.WriteUpdateMarker(updateFound.ToString(), updateUrl ?? Constants.HomePageUrl);
                }

                // check mod versions
                if (mods.Any())
                {
                    try
                    {
                        HashSet<string> suppressUpdateChecks = this.Settings.SuppressUpdateChecks;

                        // prepare search model
                        List<ModSearchEntryModel> searchMods = new List<ModSearchEntryModel>();
                        foreach (IModMetadata mod in mods)
                        {
                            if (!mod.HasID() || suppressUpdateChecks.Contains(mod.Manifest.UniqueID))
                                continue;

                            string[] updateKeys = mod
                                .GetUpdateKeys(validOnly: true)
                                .Select(p => p.ToString())
                                .ToArray();
                            searchMods.Add(new ModSearchEntryModel(mod.Manifest.UniqueID, mod.Manifest.Version, updateKeys.ToArray(), isBroken: mod.Status == ModMetadataStatus.Failed));
                        }

                        // fetch results
                        this.Monitor.Log($"   Checking for updates to {searchMods.Count} mods...");
                        IDictionary<string, ModEntryModel> results = await client.GetModInfoAsync(searchMods.ToArray(), apiVersion: Constants.ApiVersion, gameVersion: Constants.GameVersion, platform: Constants.Platform);

                        // extract update alerts & errors
                        var updates = new List<Tuple<IModMetadata, ISemanticVersion, string>>();
                        var errors = new StringBuilder();
                        foreach (IModMetadata mod in mods.OrderBy(p => p.DisplayName))
                        {
                            // link to update-check data
                            if (!mod.HasID() || !results.TryGetValue(mod.Manifest.UniqueID, out ModEntryModel? result))
                                continue;
                            mod.SetUpdateData(result);

                            // handle errors
                            if (result.Errors.Any())
                            {
                                errors.AppendLine(result.Errors.Length == 1
                                    ? $"   {mod.DisplayName}: {result.Errors[0]}"
                                    : $"   {mod.DisplayName}:\n      - {string.Join("\n      - ", result.Errors)}"
                                );
                            }

                            // handle update
                            if (result.SuggestedUpdate != null)
                                updates.Add(Tuple.Create(mod, result.SuggestedUpdate.Version, result.SuggestedUpdate.Url));
                        }

                        // show update errors
                        if (errors.Length != 0)
                            this.Monitor.Log("Got update-check errors for some mods:\n" + errors.ToString().TrimEnd());

                        // show update alerts
                        if (updates.Any())
                        {
                            this.Monitor.Newline();
                            this.Monitor.Log($"You can update {updates.Count} mod{(updates.Count != 1 ? "s" : "")}:", LogLevel.Alert);
                            foreach ((IModMetadata mod, ISemanticVersion newVersion, string newUrl) in updates)
                                this.Monitor.Log($"   {mod.DisplayName} {newVersion}: {newUrl} (you have {mod.Manifest.Version})", LogLevel.Alert);
                        }
                        else
                            this.Monitor.Log("   All mods up to date.");
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log("Couldn't check for new mod versions. This won't affect your game, but you won't be notified of mod updates if this keeps happening.", LogLevel.Warn);
                        this.Monitor.Log(ex is WebException && ex.InnerException == null
                            ? ex.Message
                            : ex.ToString()
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log("Couldn't check for updates. This won't affect your game, but you won't be notified of SMAPI or mod updates if this keeps happening.", LogLevel.Warn);
                this.Monitor.Log(ex is WebException && ex.InnerException == null
                    ? ex.Message
                    : ex.ToString()
                );
            }
        }

        /// <summary>Create a directory path if it doesn't exist.</summary>
        /// <param name="path">The directory path.</param>
        private void VerifyPath(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                // note: this happens before this.Monitor is initialized
                Console.WriteLine($"Couldn't create a path: {path}\n\n{ex.GetLogSummary()}");
            }
        }

        /// <summary>Load and hook up the given mods.</summary>
        /// <param name="mods">The mods to load.</param>
        /// <param name="jsonHelper">The JSON helper with which to read mods' JSON files.</param>
        /// <param name="modDatabase">Handles access to SMAPI's internal mod metadata list.</param>
        private void LoadMods(IModMetadata[] mods, JsonHelper jsonHelper, ModDatabase modDatabase)
        {
            this.Monitor.Log("Loading mods...", LogLevel.Debug);

            // load mods
            IList<IModMetadata> skippedMods = new List<IModMetadata>();
            using (AssemblyLoader modAssemblyLoader = new(Constants.Platform, this.Monitor, this.Settings.ParanoidWarnings, this.Settings.RewriteMods))
            {
                // init
                HashSet<string> suppressUpdateChecks = this.Settings.SuppressUpdateChecks;
                IInterfaceProxyFactory proxyFactory = new InterfaceProxyFactory();

                // load mods
                foreach (IModMetadata mod in mods)
                {
                    if (!this.TryLoadMod(mod, mods, modAssemblyLoader, proxyFactory, jsonHelper, modDatabase, suppressUpdateChecks, out ModFailReason? failReason, out string? errorPhrase, out string? errorDetails))
                    {
                        mod.SetStatus(ModMetadataStatus.Failed, failReason.Value, errorPhrase, errorDetails);
                        skippedMods.Add(mod);
                    }
                }
            }

            IModMetadata[] loaded = this.ModRegistry.GetAll().ToArray();
            IModMetadata[] loadedContentPacks = loaded.Where(p => p.IsContentPack).ToArray();
            IModMetadata[] loadedMods = loaded.Where(p => !p.IsContentPack).ToArray();

            // unlock content packs
            this.ModRegistry.AreAllModsLoaded = true;

            // log mod info
            this.LogManager.LogModInfo(loaded, loadedContentPacks, loadedMods, skippedMods.ToArray(), this.Settings.ParanoidWarnings);

            // initialize translations
            this.ReloadTranslations(loaded);

            // initialize loaded non-content-pack mods
            this.Monitor.Log("Launching mods...", LogLevel.Debug);
            foreach (IModMetadata metadata in loadedMods)
            {
                // call entry method
                Context.HeuristicModsRunningCode.Push(metadata);
                try
                {
                    IMod mod = metadata.Mod!;
                    mod.Entry(mod.Helper!);
                }
                catch (Exception ex)
                {
                    metadata.LogAsMod($"Mod crashed on entry and might not work correctly. Technical details:\n{ex.GetLogSummary()}", LogLevel.Error);
                }

                // get mod API
                try
                {
                    object? api = metadata.Mod!.GetApi();
                    if (api != null && !api.GetType().IsPublic)
                    {
                        api = null;
                        this.Monitor.Log($"{metadata.DisplayName} provides an API instance with a non-public type. This isn't currently supported, so the API won't be available to other mods.", LogLevel.Warn);
                    }

                    if (api != null)
                        this.Monitor.Log($"   Found mod-provided API ({api.GetType().FullName}).");
                    metadata.SetApi(api);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed loading mod-provided API for {metadata.DisplayName}. Integrations with other mods may not work. Error: {ex.GetLogSummary()}", LogLevel.Error);
                }
                Context.HeuristicModsRunningCode.TryPop(out _);
            }

            // unlock mod integrations
            this.ModRegistry.AreAllModsInitialized = true;

            this.Monitor.Log("Mods loaded and ready!", LogLevel.Debug);
        }

        /// <summary>Load a given mod.</summary>
        /// <param name="mod">The mod to load.</param>
        /// <param name="mods">The mods being loaded.</param>
        /// <param name="assemblyLoader">Preprocesses and loads mod assemblies.</param>
        /// <param name="proxyFactory">Generates proxy classes to access mod APIs through an arbitrary interface.</param>
        /// <param name="jsonHelper">The JSON helper with which to read mods' JSON files.</param>
        /// <param name="modDatabase">Handles access to SMAPI's internal mod metadata list.</param>
        /// <param name="suppressUpdateChecks">The mod IDs to ignore when validating update keys.</param>
        /// <param name="failReason">The reason the mod couldn't be loaded, if applicable.</param>
        /// <param name="errorReasonPhrase">The user-facing reason phrase explaining why the mod couldn't be loaded (if applicable).</param>
        /// <param name="errorDetails">More detailed details about the error intended for developers (if any).</param>
        /// <returns>Returns whether the mod was successfully loaded.</returns>
        private bool TryLoadMod(IModMetadata mod, IModMetadata[] mods, AssemblyLoader assemblyLoader, IInterfaceProxyFactory proxyFactory, JsonHelper jsonHelper, ModDatabase modDatabase, HashSet<string> suppressUpdateChecks, [NotNullWhen(false)] out ModFailReason? failReason, out string? errorReasonPhrase, out string? errorDetails)
        {
            errorDetails = null;

            // log entry
            {
                string relativePath = mod.GetRelativePathWithRoot();
                if (mod.IsContentPack)
                    this.Monitor.Log($"   {mod.DisplayName} (from {relativePath}) [content pack]...");
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract -- mod may be invalid at this point
                else if (mod.Manifest?.EntryDll != null)
                    this.Monitor.Log($"   {mod.DisplayName} (from {relativePath}{Path.DirectorySeparatorChar}{mod.Manifest.EntryDll})..."); // don't use Path.Combine here, since EntryDLL might not be valid
                else
                    this.Monitor.Log($"   {mod.DisplayName} (from {relativePath})...");
            }

            // add warning for missing update key
            if (mod.HasID() && !suppressUpdateChecks.Contains(mod.Manifest!.UniqueID) && !mod.HasValidUpdateKeys())
                mod.SetWarning(ModWarning.NoUpdateKeys);

            // validate status
            if (mod.Status == ModMetadataStatus.Failed)
            {
                this.Monitor.Log($"      Failed: {mod.ErrorDetails ?? mod.Error}");
                failReason = mod.FailReason ?? ModFailReason.LoadFailed;
                errorReasonPhrase = mod.Error;
                return false;
            }
            IManifest manifest = mod.Manifest!;

            // validate dependencies
            // Although dependencies are validated before mods are loaded, a dependency may have failed to load.
            foreach (IManifestDependency dependency in manifest.Dependencies.Where(p => p.IsRequired))
            {
                if (this.ModRegistry.Get(dependency.UniqueID) == null)
                {
                    string dependencyName = mods
                        .FirstOrDefault(otherMod => otherMod.HasID(dependency.UniqueID))
                        ?.DisplayName ?? dependency.UniqueID;
                    errorReasonPhrase = $"it needs the '{dependencyName}' mod, which couldn't be loaded.";
                    failReason = ModFailReason.MissingDependencies;
                    return false;
                }
            }

            // load as content pack
            if (mod.IsContentPack)
            {
                IMonitor monitor = this.LogManager.GetMonitor(manifest.UniqueID, mod.DisplayName);
                IFileLookup fileLookup = this.GetFileLookup(mod.DirectoryPath);
                TranslationHelper translationHelper = new(mod, "", LanguageCode.en);
                IContentPack contentPack = new ContentPack(mod.DirectoryPath, manifest, translationHelper, jsonHelper, fileLookup);
                mod.SetMod(contentPack, monitor, translationHelper);
                this.ModRegistry.Add(mod);

                errorReasonPhrase = null;
                failReason = null;
                return true;
            }

            // load as mod
            else
            {
                // get mod info
                FileInfo assemblyFile = this.GetFileLookup(mod.DirectoryPath).GetFile(manifest.EntryDll!);

                // load mod
                Assembly modAssembly;
                try
                {
                    modAssembly = assemblyLoader.Load(mod, assemblyFile, assumeCompatible: mod.DataRecord?.Status == ModStatus.AssumeCompatible);
                    this.ModRegistry.TrackAssemblies(mod, modAssembly);
                }
                catch (IncompatibleInstructionException) // details already in trace logs
                {
                    string[] updateUrls = new[] { modDatabase.GetModPageUrlFor(manifest.UniqueID), "https://smapi.io/mods" }.Where(p => p != null).ToArray()!;
                    errorReasonPhrase = $"it's no longer compatible. Please check for a new version at {string.Join(" or ", updateUrls)}";
                    failReason = ModFailReason.Incompatible;
                    return false;
                }
                catch (SAssemblyLoadFailedException ex)
                {
                    errorReasonPhrase = $"its DLL couldn't be loaded: {ex.Message}";
                    failReason = ModFailReason.LoadFailed;
                    return false;
                }
                catch (Exception ex)
                {
                    errorReasonPhrase = "its DLL couldn't be loaded.";
                    if (ex is BadImageFormatException && !EnvironmentUtility.Is64BitAssembly(assemblyFile.FullName))
                        errorReasonPhrase = "it needs to be updated for 64-bit mode.";

                    errorDetails = $"Error: {ex.GetLogSummary()}";
                    failReason = ModFailReason.LoadFailed;
                    return false;
                }

                // initialize mod
                try
                {
                    // get mod instance
                    if (!this.TryLoadModEntry(mod, modAssembly, out Mod? modEntry, out errorReasonPhrase))
                    {
                        failReason = ModFailReason.LoadFailed;
                        return false;
                    }

                    // get content packs
                    IContentPack[] GetContentPacks()
                    {
                        if (!this.ModRegistry.AreAllModsLoaded)
                            throw new InvalidOperationException("Can't access content packs before SMAPI finishes loading mods.");

                        return this.ModRegistry
                            .GetAll(assemblyMods: false)
                            .Where(p => p.IsContentPack && mod.HasID(p.Manifest.ContentPackFor!.UniqueID))
                            .Select(p => p.ContentPack!)
                            .ToArray();
                    }

                    // init mod helpers
                    IMonitor monitor = this.LogManager.GetMonitor(manifest.UniqueID, mod.DisplayName);
                    TranslationHelper translationHelper = new(mod, "", LanguageCode.en);
                    IModHelper modHelper;
                    {
                        IContentPackHelper contentPackHelper = new ContentPackHelper(
                            mod: mod,
                            contentPacks: new Lazy<IContentPack[]>(GetContentPacks),
                            createContentPack: (dirPath, fakeManifest) => this.CreateFakeContentPack(dirPath, fakeManifest, mod)
                        );
                        IDataHelper dataHelper = new DataHelper(mod, mod.DirectoryPath, jsonHelper);
                        IReflectionHelper reflectionHelper = new ReflectionHelper(mod, mod.DisplayName, this.Reflection);
                        IModRegistry modRegistryHelper = new ModRegistryHelper(mod, this.ModRegistry, proxyFactory, monitor);

                        modHelper = new ModHelper(mod, mod.DirectoryPath, contentPackHelper, dataHelper, modRegistryHelper, reflectionHelper, translationHelper);
                    }

                    // init mod
                    modEntry.ModManifest = manifest;
                    modEntry.Helper = modHelper;
                    modEntry.Monitor = monitor;

                    // track mod
                    mod.SetMod(modEntry, translationHelper);
                    this.ModRegistry.Add(mod);
                    failReason = null;
                    return true;
                }
                catch (Exception ex)
                {
                    errorReasonPhrase = $"initialization failed:\n{ex.GetLogSummary()}";
                    failReason = ModFailReason.LoadFailed;
                    return false;
                }
            }
        }

        /// <summary>Create a fake content pack instance for a parent mod.</summary>
        /// <param name="packDirPath">The absolute path to the fake content pack's directory.</param>
        /// <param name="packManifest">The fake content pack's manifest.</param>
        /// <param name="parentMod">The mod for which the content pack is being created.</param>
        private IContentPack CreateFakeContentPack(string packDirPath, IManifest packManifest, IModMetadata parentMod)
        {
            // create fake mod info
            string relativePath = Path.GetRelativePath(Constants.ModsPath, packDirPath);
            IModMetadata fakeMod = new ModMetadata(
                displayName: packManifest.Name,
                directoryPath: packDirPath,
                rootPath: Constants.ModsPath,
                manifest: packManifest,
                dataRecord: null,
                isIgnored: false
            );

            // create mod helpers
            IMonitor packMonitor = this.LogManager.GetMonitor(packManifest.UniqueID, packManifest.Name);
            TranslationHelper packTranslationHelper = new(fakeMod, "", LanguageCode.en);

            // add content pack
            IFileLookup fileLookup = this.GetFileLookup(packDirPath);
            ContentPack contentPack = new(packDirPath, packManifest, packTranslationHelper, this.Toolkit.JsonHelper, fileLookup);
            this.ReloadTranslationsForTemporaryContentPack(parentMod, contentPack);
            parentMod.FakeContentPacks.Add(new WeakReference<ContentPack>(contentPack));

            // log change
            string pathLabel = packDirPath.Contains("..") ? packDirPath : relativePath;
            this.Monitor.Log($"{parentMod.DisplayName} created dynamic content pack '{packManifest.Name}' (unique ID: {packManifest.UniqueID}{(packManifest.Name.Contains(pathLabel) ? "" : $", path: {pathLabel}")}).");

            return contentPack;
        }

        /// <summary>Load a mod's entry class.</summary>
        /// <param name="metadata">The mod metadata whose entry class is being loaded.</param>
        /// <param name="modAssembly">The mod assembly.</param>
        /// <param name="mod">The loaded instance.</param>
        /// <param name="error">The error indicating why loading failed (if applicable).</param>
        /// <returns>Returns whether the mod entry class was successfully loaded.</returns>
        private bool TryLoadModEntry(IModMetadata metadata, Assembly modAssembly, [NotNullWhen(true)] out Mod? mod, [NotNullWhen(false)] out string? error)
        {
            mod = null;

            // find type
            TypeInfo[] modEntries = modAssembly.DefinedTypes.Where(type => typeof(Mod).IsAssignableFrom(type) && !type.IsAbstract).Take(2).ToArray();
            if (modEntries.Length == 0)
            {
                error = $"its DLL has no '{nameof(Mod)}' subclass.";
                return false;
            }
            if (modEntries.Length > 1)
            {
                error = $"its DLL contains multiple '{nameof(Mod)}' subclasses.";
                return false;
            }

            // get implementation
            Context.HeuristicModsRunningCode.Push(metadata);
            try
            {
                mod = (Mod?)modAssembly.CreateInstance(modEntries[0].ToString());
            }
            finally
            {
                Context.HeuristicModsRunningCode.TryPop(out _);
            }

            if (mod == null)
            {
                error = "its entry class couldn't be instantiated.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Reload translations for the given mods.</summary>
        /// <param name="mods">The mods for which to reload translations.</param>
        private void ReloadTranslations(IEnumerable<IModMetadata> mods)
        {
            // core SMAPI translations
            {
                var translations = this.ReadTranslationFiles(Path.Combine(Constants.InternalFilesPath, "i18n"), out IList<string> errors);
                if (errors.Any() || !translations.Any())
                {
                    this.Monitor.Log("SMAPI couldn't load some core translations. You may need to reinstall SMAPI.", LogLevel.Warn);
                    foreach (string error in errors)
                        this.Monitor.Log($"  - {error}", LogLevel.Warn);
                }
                this.Translator.SetTranslations(translations);
            }

            // mod translations
            foreach (IModMetadata metadata in mods)
            {
                // top-level mod
                {
                    var translations = this.ReadTranslationFiles(Path.Combine(metadata.DirectoryPath, "i18n"), out IList<string> errors);
                    if (errors.Any())
                    {
                        metadata.LogAsMod("Mod couldn't load some translation files:", LogLevel.Warn);
                        foreach (string error in errors)
                            metadata.LogAsMod($"  - {error}", LogLevel.Warn);
                    }

                    metadata.Translations!.SetTranslations(translations);
                }

                // fake content packs
                foreach (ContentPack pack in metadata.GetFakeContentPacks())
                    this.ReloadTranslationsForTemporaryContentPack(metadata, pack);
            }
        }

        /// <summary>Load or reload translations for a temporary content pack created by a mod.</summary>
        /// <param name="parentMod">The parent mod which created the content pack.</param>
        /// <param name="contentPack">The content pack instance.</param>
        private void ReloadTranslationsForTemporaryContentPack(IModMetadata parentMod, ContentPack contentPack)
        {
            var translations = this.ReadTranslationFiles(Path.Combine(contentPack.DirectoryPath, "i18n"), out IList<string> errors);
            if (errors.Any())
            {
                parentMod.LogAsMod($"Generated content pack at '{PathUtilities.GetRelativePath(Constants.ModsPath, contentPack.DirectoryPath)}' couldn't load some translation files:", LogLevel.Warn);
                foreach (string error in errors)
                    parentMod.LogAsMod($"  - {error}", LogLevel.Warn);
            }

            contentPack.TranslationImpl.SetTranslations(translations);
        }

        /// <summary>Read translations from a directory containing JSON translation files.</summary>
        /// <param name="folderPath">The folder path to search.</param>
        /// <param name="errors">The errors indicating why translation files couldn't be parsed, indexed by translation filename.</param>
        private IDictionary<string, IDictionary<string, string>> ReadTranslationFiles(string folderPath, out IList<string> errors)
        {
            JsonHelper jsonHelper = this.Toolkit.JsonHelper;

            // read translation files
            var translations = new Dictionary<string, IDictionary<string, string>>();
            errors = new List<string>();
            DirectoryInfo translationsDir = new(folderPath);
            if (translationsDir.Exists)
            {
                foreach (FileInfo file in translationsDir.EnumerateFiles("*.json"))
                {
                    string locale = Path.GetFileNameWithoutExtension(file.Name.ToLower().Trim());
                    try
                    {
                        if (!jsonHelper.ReadJsonFileIfExists(file.FullName, out IDictionary<string, string>? data))
                        {
                            errors.Add($"{file.Name} file couldn't be read"); // mainly happens when the file is corrupted or empty
                            continue;
                        }

                        translations[locale] = data;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{file.Name} file couldn't be parsed: {ex.GetLogSummary()}");
                    }
                }
            }

            // validate translations
            foreach (string locale in translations.Keys.ToArray())
            {
                // handle duplicates
                HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> duplicateKeys = new(StringComparer.OrdinalIgnoreCase);
                foreach (string key in translations[locale].Keys.ToArray())
                {
                    if (!keys.Add(key))
                    {
                        duplicateKeys.Add(key);
                        translations[locale].Remove(key);
                    }
                }
                if (duplicateKeys.Any())
                    errors.Add($"{locale}.json has duplicate translation keys: [{string.Join(", ", duplicateKeys)}]. Keys are case-insensitive.");
            }

            return translations;
        }

        /// <summary>Get a file lookup for the given directory.</summary>
        /// <param name="rootDirectory">The root path to scan.</param>
        private IFileLookup GetFileLookup(string rootDirectory)
        {
            return this.Settings.UseCaseInsensitivePaths
                ? CaseInsensitiveFileLookup.GetCachedFor(rootDirectory)
                : MinimalFileLookup.GetCachedFor(rootDirectory);
        }

        /// <summary>Get the absolute path to the next available log file.</summary>
        private string GetLogPath()
        {
            // default path
            {
                FileInfo defaultFile = new(Path.Combine(Constants.LogDir, $"{Constants.LogFilename}.{Constants.LogExtension}"));
                if (!defaultFile.Exists)
                    return defaultFile.FullName;
            }

            // get first disambiguated path
            for (int i = 2; i < int.MaxValue; i++)
            {
                FileInfo file = new(Path.Combine(Constants.LogDir, $"{Constants.LogFilename}.player-{i}.{Constants.LogExtension}"));
                if (!file.Exists)
                    return file.FullName;
            }

            // should never happen
            throw new InvalidOperationException("Could not find an available log path.");
        }

        /// <summary>Delete normal (non-crash) log files created by SMAPI.</summary>
        private void PurgeNormalLogs()
        {
            DirectoryInfo logsDir = new(Constants.LogDir);
            if (!logsDir.Exists)
                return;

            foreach (FileInfo logFile in logsDir.EnumerateFiles())
            {
                // skip non-SMAPI file
                if (!logFile.Name.StartsWith(Constants.LogNamePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // skip crash log
                if (logFile.FullName == Constants.FatalCrashLog)
                    continue;

                // delete file
                try
                {
                    FileUtilities.ForceDelete(logFile);
                }
                catch (IOException)
                {
                    // ignore file if it's in use
                }
            }
        }

        /// <summary>Immediately exit the game without saving. This should only be invoked when an irrecoverable fatal error happens that risks save corruption or game-breaking bugs.</summary>
        /// <param name="message">The fatal log message.</param>
        private void ExitGameImmediately(string message)
        {
            this.Monitor.LogFatal(message);
            this.LogManager.WriteCrashLog();

            this.IsExiting = true;
            this.Game.Exit();
        }

        /// <summary>Get the screen ID that should be logged to distinguish between players in split-screen mode, if any.</summary>
        private int? GetScreenIdForLog()
        {
            if (Context.ScreenId != 0 || (Context.IsWorldReady && Context.IsSplitScreen))
                return Context.ScreenId;

            return null;
        }
    }
}
