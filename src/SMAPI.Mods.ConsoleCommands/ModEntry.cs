using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Mods.ConsoleCommands.Framework.Commands;

namespace StardewModdingAPI.Mods.ConsoleCommands
{
    /// <summary>The main entry point for the mod.</summary>
    public class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        /// <summary>The commands to handle.</summary>
        private IConsoleCommand[] Commands = null!;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            // register commands
            this.Commands = this.ScanForCommands().ToArray();
            foreach (IConsoleCommand command in this.Commands)
                helper.ConsoleCommands.Add(command.Name, command.Description, (name, args) => this.HandleCommand(command, name, args));
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Handle a console command.</summary>
        /// <param name="command">The command to invoke.</param>
        /// <param name="commandName">The command name specified by the user.</param>
        /// <param name="args">The command arguments.</param>
        private void HandleCommand(IConsoleCommand command, string commandName, string[] args)
        {
            ArgumentParser argParser = new(commandName, args, this.Monitor);
            command.Handle(this.Monitor, commandName, argParser);
        }

        /// <summary>Find all commands in the assembly.</summary>
        private IEnumerable<IConsoleCommand> ScanForCommands()
        {
            return (
                from type in this.GetType().Assembly.GetTypes()
                where !type.IsAbstract && typeof(IConsoleCommand).IsAssignableFrom(type)
                select (IConsoleCommand)Activator.CreateInstance(type)!
            );
        }
    }
}
