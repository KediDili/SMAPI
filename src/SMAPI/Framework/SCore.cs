using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
#if SMAPI_FOR_WINDOWS
#endif
using Newtonsoft.Json;
using StardewModdingAPI.Framework.Logging;
using StardewModdingAPI.Framework.Models;
using StardewModdingAPI.Framework.Serialization;
using StardewModdingAPI.Framework.Utilities;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Serialization;
using StardewModdingAPI.Toolkit.Utilities;
using StardewModdingAPI.Toolkit.Utilities.PathLookups;
using StardewModdingAPI.Utilities;
using StardewValley;
using PathUtilities = StardewModdingAPI.Toolkit.Utilities.PathUtilities;

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
        /// <summary>Whether the game is currently running.</summary>
        private bool IsGameRunning;

        /// <summary>Whether the program has been disposed.</summary>
        private bool IsDisposed;

        /// <summary>The maximum number of consecutive attempts SMAPI should make to recover from an update error.</summary>
        private readonly Countdown UpdateCrashTimer = new(60); // 60 ticks = roughly one second

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

                // override game
                this.Game = new SGameRunner(
                    onGameContentLoaded: this.OnInstanceContentLoaded,
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
        /// <summary>Raised after an instance finishes loading its initial content.</summary>
        private void OnInstanceContentLoaded()
        {
            // log GPU info
#if SMAPI_FOR_WINDOWS
            this.Monitor.Log($"Running on GPU: {Game1.game1.GraphicsDevice?.Adapter?.Description ?? "<unknown>"}");
#endif
        }

        /// <summary>Raised before the game exits.</summary>
        private void OnGameExiting()
        {
            this.Dispose();
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

        /// <summary>Get the screen ID that should be logged to distinguish between players in split-screen mode, if any.</summary>
        private int? GetScreenIdForLog()
        {
            if (Context.ScreenId != 0 || (Context.IsWorldReady && Context.IsSplitScreen))
                return Context.ScreenId;

            return null;
        }
    }
}
