namespace StardewModdingAPI
{
    /// <summary>Encapsulates monitoring and logging for a given module.</summary>
    public interface IMonitor
    {
        /*********
        ** Methods
        *********/
        /// <summary>Log a message for the player or developer.</summary>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log severity level.</param>
        void Log(string message, LogLevel level = LogLevel.Trace);

        /// <summary>Log a message for the player or developer, but only if it hasn't already been logged since the last game launch.</summary>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log severity level.</param>
        void LogOnce(string message, LogLevel level = LogLevel.Trace);
    }
}
