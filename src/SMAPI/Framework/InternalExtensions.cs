using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace StardewModdingAPI.Framework
{
    /// <summary>Provides extension methods for SMAPI's internal use.</summary>
    internal static class InternalExtensions
    {
        /*********
        ** Public methods
        *********/
        /****
        ** IMonitor
        ****/
        /// <summary>Log a message for the player or developer the first time it occurs.</summary>
        /// <param name="monitor">The monitor through which to log the message.</param>
        /// <param name="hash">The hash of logged messages.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log severity level.</param>
        public static void LogOnce(this IMonitor monitor, HashSet<string> hash, string message, LogLevel level = LogLevel.Trace)
        {
            if (!hash.Contains(message))
            {
                monitor.Log(message, level);
                hash.Add(message);
            }
        }

        /****
        ** IModMetadata
        ****/
        /// <summary>Log a message using the mod's monitor.</summary>
        /// <param name="metadata">The mod whose monitor to use.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log severity level.</param>
        public static void LogAsMod(this IModMetadata metadata, string message, LogLevel level = LogLevel.Trace)
        {
            if (metadata.Monitor is null)
                throw new InvalidOperationException($"Can't log as mod {metadata.DisplayName}: mod is broken or a content pack. Logged message:\n[{level}] {message}");

            metadata.Monitor.Log(message, level);
        }

        /// <summary>Log a message using the mod's monitor, but only if it hasn't already been logged since the last game launch.</summary>
        /// <param name="metadata">The mod whose monitor to use.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log severity level.</param>
        public static void LogAsModOnce(this IModMetadata metadata, string message, LogLevel level = LogLevel.Trace)
        {
            metadata.Monitor?.LogOnce(message, level);
        }

        /****
        ** Sprite batch
        ****/
        /// <summary>Get whether the sprite batch is between a begin and end pair.</summary>
        /// <param name="spriteBatch">The sprite batch to check.</param>
        public static bool IsOpen(this SpriteBatch spriteBatch)
        {
            return spriteBatch.GetType().GetField("_beginCalled", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(spriteBatch) as bool? ?? false;
        }
    }
}
