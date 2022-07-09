using System;
using System.IO;
using System.Reflection;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Toolkit.Framework;
using StardewModdingAPI.Toolkit.Utilities;
using StardewValley;

namespace StardewModdingAPI
{
    /// <summary>Contains constants that are accessed before the game itself has been loaded.</summary>
    /// <remarks>Most code should use <see cref="Constants"/> instead of this class directly.</remarks>
    internal static class EarlyConstants
    {
        //
        // Note: this class *must not* depend on any external DLL beyond .NET Framework itself.
        // That includes the game or SMAPI toolkit, since it's accessed before those are loaded.
        //
        // Adding an external dependency may seem to work in some cases, but will prevent SMAPI
        // from showing a human-readable error if the game isn't available. To test this, just
        // rename "Stardew Valley.exe" in the game folder; you should see an error like "Oops!
        // SMAPI can't find the game", not a technical exception.
        //

        /*********
        ** Accessors
        *********/
        /// <summary>The path to the game folder.</summary>
        public static string GamePath { get; } = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        /// <summary>The absolute path to the folder containing SMAPI's internal files.</summary>
        public static readonly string InternalFilesPath = Path.Combine(EarlyConstants.GamePath, "smapi-internal");

        /// <summary>The target game platform.</summary>
        internal static GamePlatform Platform { get; } = (GamePlatform)Enum.Parse(typeof(GamePlatform), LowLevelEnvironmentUtility.DetectPlatform());

        /// <summary>The game framework running the game.</summary>
        internal static GameFramework GameFramework { get; } = GameFramework.MonoGame;

        /// <summary>The game's assembly name.</summary>
        internal static string GameAssemblyName { get; } = "Stardew Valley";

        /// <summary>SMAPI's current raw semantic version.</summary>
        internal static string RawApiVersion = "3.15.1";
    }

    /// <summary>Contains SMAPI's constants and assumptions.</summary>
    public static class Constants
    {
        /*********
        ** Accessors
        *********/
        /****
        ** Public
        ****/
        /// <summary>SMAPI's current semantic version.</summary>
        public static ISemanticVersion ApiVersion { get; } = new Toolkit.SemanticVersion(EarlyConstants.RawApiVersion);

        /// <summary>The target game platform.</summary>
        public static GamePlatform TargetPlatform { get; } = EarlyConstants.Platform;

        /// <summary>The path to the game folder.</summary>
        public static string GamePath { get; } = EarlyConstants.GamePath;

        /// <summary>The directory path containing Stardew Valley's app data.</summary>
        public static string DataPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley");

        /// <summary>The directory path where all saves are stored.</summary>
        public static string SavesPath { get; } = Path.Combine(Constants.DataPath, "Saves");

        /****
        ** Internal
        ****/
        /// <summary>Whether SMAPI was compiled in debug mode.</summary>
        internal const bool IsDebugBuild =
#if DEBUG
            true;
#else
            false;
#endif

        /// <summary>The URL of the SMAPI home page.</summary>
        internal const string HomePageUrl = "https://smapi.io";

        /// <summary>The absolute path to the folder containing SMAPI's internal files.</summary>
        internal static readonly string InternalFilesPath = EarlyConstants.InternalFilesPath;

        /// <summary>The file path for the SMAPI configuration file.</summary>
        internal static string ApiConfigPath => Path.Combine(Constants.InternalFilesPath, "config.json");

        /// <summary>The file path for the overrides file for <see cref="ApiConfigPath"/>, which is applied over it.</summary>
        internal static string ApiUserConfigPath => Path.Combine(Constants.InternalFilesPath, "config.user.json");

        /// <summary>The game's current semantic version.</summary>
        internal static ISemanticVersion GameVersion { get; } = new GameVersion(Game1.version);

        /// <summary>The target game platform as a SMAPI toolkit constant.</summary>
        internal static Platform Platform { get; } = (Platform)Constants.TargetPlatform;


        /*********
        ** Private methods
        *********/
        /// <summary>Get a display label for the game's build number.</summary>
        internal static string GetBuildVersionLabel()
        {
            string version = typeof(Game1).Assembly.GetName().Version?.ToString() ?? "unknown";

            if (version.StartsWith($"{Game1.version}."))
                version = version.Substring(Game1.version.Length + 1);

            return version;
        }
    }
}
