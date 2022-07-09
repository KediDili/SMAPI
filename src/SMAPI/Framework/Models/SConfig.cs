using System;
using System.Collections.Generic;
using StardewModdingAPI.Toolkit.Utilities;

namespace StardewModdingAPI.Framework.Models
{
    /// <summary>The SMAPI configuration settings.</summary>
    internal class SConfig
    {
        /********
        ** Fields
        ********/
        /// <summary>The default config values, for fields that should be logged if different.</summary>
        private static readonly IDictionary<string, object> DefaultValues = new Dictionary<string, object>
        {
            [nameof(CheckForUpdates)] = true,
            [nameof(ParanoidWarnings)] = Constants.IsDebugBuild,
            [nameof(UseBetaChannel)] = Constants.ApiVersion.IsPrerelease(),
            [nameof(GitHubProjectName)] = "Pathoschild/SMAPI",
            [nameof(WebApiBaseUrl)] = "https://smapi.io/api/",
            [nameof(RewriteMods)] = true,
            [nameof(UseRawImageLoading)] = true,
            [nameof(UseCaseInsensitivePaths)] = Constants.Platform is Platform.Android or Platform.Linux
        };

        /// <summary>The default values for <see cref="SuppressUpdateChecks"/>, to log changes if different.</summary>
        private static readonly HashSet<string> DefaultSuppressUpdateChecks = new(StringComparer.OrdinalIgnoreCase)
        {
            "SMAPI.ConsoleCommands",
            "SMAPI.ErrorHandler",
            "SMAPI.SaveBackup"
        };


        /********
        ** Accessors
        ********/
        //
        // Note: properties must be writable to support merging config.user.json into it.
        //

        /// <summary>Whether to check for newer versions of SMAPI and mods on startup.</summary>
        public bool CheckForUpdates { get; set; }

        /// <summary>Whether to add a section to the 'mod issues' list for mods which which directly use potentially sensitive .NET APIs like file or shell access.</summary>
        public bool ParanoidWarnings { get; set; }

        /// <summary>Whether to show beta versions as valid updates.</summary>
        public bool UseBetaChannel { get; set; }

        /// <summary>SMAPI's GitHub project name, used to perform update checks.</summary>
        public string GitHubProjectName { get; set; }

        /// <summary>The base URL for SMAPI's web API, used to perform update checks.</summary>
        public string WebApiBaseUrl { get; set; }

        /// <summary>Whether SMAPI should rewrite mods for compatibility.</summary>
        public bool RewriteMods { get; set; }

        /// <summary>Whether to use raw image data when possible, instead of initializing an XNA Texture2D instance through the GPU.</summary>
        public bool UseRawImageLoading { get; set; }

        /// <summary>Whether to make SMAPI file APIs case-insensitive, even on Linux.</summary>
        public bool UseCaseInsensitivePaths { get; set; }

        /// <summary>The mod IDs SMAPI should ignore when performing update checks or validating update keys.</summary>
        public HashSet<string> SuppressUpdateChecks { get; set; }


        /********
        ** Public methods
        ********/
        /// <summary>Construct an instance.</summary>
        /// <param name="checkForUpdates">Whether to check for newer versions of SMAPI and mods on startup.</param>
        /// <param name="paranoidWarnings">Whether to add a section to the 'mod issues' list for mods which which directly use potentially sensitive .NET APIs like file or shell access.</param>
        /// <param name="useBetaChannel">Whether to show beta versions as valid updates.</param>
        /// <param name="gitHubProjectName">SMAPI's GitHub project name, used to perform update checks.</param>
        /// <param name="webApiBaseUrl">The base URL for SMAPI's web API, used to perform update checks.</param>
        /// <param name="rewriteMods">Whether SMAPI should rewrite mods for compatibility.</param>
        /// <param name="useRawImageLoading">Whether to use raw image data when possible, instead of initializing an XNA Texture2D instance through the GPU.</param>
        /// <param name="useCaseInsensitivePaths">>Whether to make SMAPI file APIs case-insensitive, even on Linux.</param>
        /// <param name="suppressUpdateChecks">The mod IDs SMAPI should ignore when performing update checks or validating update keys.</param>
        public SConfig(bool? checkForUpdates, bool? paranoidWarnings, bool? useBetaChannel, string gitHubProjectName, string webApiBaseUrl, bool? rewriteMods, bool? useRawImageLoading, bool? useCaseInsensitivePaths, string[]? suppressUpdateChecks)
        {
            this.CheckForUpdates = checkForUpdates ?? (bool)SConfig.DefaultValues[nameof(this.CheckForUpdates)];
            this.ParanoidWarnings = paranoidWarnings ?? (bool)SConfig.DefaultValues[nameof(this.ParanoidWarnings)];
            this.UseBetaChannel = useBetaChannel ?? (bool)SConfig.DefaultValues[nameof(this.UseBetaChannel)];
            this.GitHubProjectName = gitHubProjectName;
            this.WebApiBaseUrl = webApiBaseUrl;
            this.RewriteMods = rewriteMods ?? (bool)SConfig.DefaultValues[nameof(this.RewriteMods)];
            this.UseRawImageLoading = useRawImageLoading ?? (bool)SConfig.DefaultValues[nameof(this.UseRawImageLoading)];
            this.UseCaseInsensitivePaths = useCaseInsensitivePaths ?? (bool)SConfig.DefaultValues[nameof(this.UseCaseInsensitivePaths)];
            this.SuppressUpdateChecks = new HashSet<string>(suppressUpdateChecks ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Get the settings which have been customized by the player.</summary>
        public IDictionary<string, object?> GetCustomSettings()
        {
            Dictionary<string, object?> custom = new();

            foreach ((string? name, object defaultValue) in SConfig.DefaultValues)
            {
                object? value = typeof(SConfig).GetProperty(name)?.GetValue(this);
                if (!defaultValue.Equals(value))
                    custom[name] = value;
            }

            if (!this.SuppressUpdateChecks.SetEquals(SConfig.DefaultSuppressUpdateChecks))
                custom[nameof(this.SuppressUpdateChecks)] = $"[{string.Join(", ", this.SuppressUpdateChecks)}]";

            return custom;
        }
    }
}
