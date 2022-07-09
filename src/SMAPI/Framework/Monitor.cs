using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Internal.ConsoleWriting;

namespace StardewModdingAPI.Framework
{
    /// <summary>Encapsulates monitoring and logic for a given module.</summary>
    internal class Monitor : IMonitor
    {
        /*********
        ** Fields
        *********/
        /// <summary>The name of the module which logs messages using this instance.</summary>
        private readonly string Source;

        /// <summary>The maximum length of the <see cref="LogLevel"/> values.</summary>
        private static readonly int MaxLevelLength = (from level in Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>() select level.ToString().Length).Max();

        /// <summary>A cache of messages that should only be logged once.</summary>
        private readonly HashSet<string> LogOnceCache = new();

        /// <summary>Get the screen ID that should be logged to distinguish between players in split-screen mode, if any.</summary>
        private readonly Func<int?> GetScreenIdForLog;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="source">The name of the module which logs messages using this instance.</param>
        /// <param name="getScreenIdForLog">Get the screen ID that should be logged to distinguish between players in split-screen mode, if any.</param>
        public Monitor(string source, Func<int?> getScreenIdForLog)
        {
            // validate
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("The log source cannot be empty.");

            // initialize
            this.Source = source;
            this.GetScreenIdForLog = getScreenIdForLog;
        }

        /// <inheritdoc />
        public void Log(string message, LogLevel level = LogLevel.Trace)
        {
            this.LogImpl(this.Source, message, (ConsoleLogLevel)level);
        }

        /// <inheritdoc />
        public void LogOnce(string message, LogLevel level = LogLevel.Trace)
        {
            if (this.LogOnceCache.Add($"{message}|{level}"))
                this.LogImpl(this.Source, message, (ConsoleLogLevel)level);
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Write a message line to the log.</summary>
        /// <param name="source">The name of the mod logging the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log level.</param>
        private void LogImpl(string source, string message, ConsoleLogLevel level)
        {
            // generate message
            string prefix = this.GenerateMessagePrefix(source, level);
            string consoleMessage = $"{prefix} {message}";

            // write to console
            Console.WriteLine(consoleMessage, level);
        }

        /// <summary>Generate a message prefix for the current time.</summary>
        /// <param name="source">The name of the mod logging the message.</param>
        /// <param name="level">The log level.</param>
        private string GenerateMessagePrefix(string source, ConsoleLogLevel level)
        {
            string levelStr = level.ToString().ToUpper().PadRight(Monitor.MaxLevelLength);
            int? playerIndex = this.GetScreenIdForLog();

            return $"[{DateTime.Now:HH:mm:ss} {levelStr}{(playerIndex != null ? $" screen_{playerIndex}" : "")} {source}]";
        }
    }
}
