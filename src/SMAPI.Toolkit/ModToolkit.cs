using System.Collections.Generic;
using System.IO;
using StardewModdingAPI.Toolkit.Framework.GameScanning;
using StardewModdingAPI.Toolkit.Serialization;

namespace StardewModdingAPI.Toolkit
{
    /// <summary>A convenience wrapper for the various tools.</summary>
    public class ModToolkit
    {
        /*********
        ** Accessors
        *********/
        /// <summary>Encapsulates SMAPI's JSON parsing.</summary>
        public JsonHelper JsonHelper { get; } = new();


        /*********
        ** Public methods
        *********/
        /// <summary>Find valid Stardew Valley install folders.</summary>
        /// <remarks>This checks default game locations, and on Windows checks the Windows registry for GOG/Steam install data. A folder is considered 'valid' if it contains the Stardew Valley executable for the current OS.</remarks>
        public IEnumerable<DirectoryInfo> GetGameFolders()
        {
            return new GameScanner().Scan();
        }
    }
}
