#nullable enable
using System;

namespace Rustgrid.Tactics.Core
{
    /// <summary>A level or rules file is malformed or inconsistent.</summary>
    public sealed class DefinitionException : Exception
    {
        public DefinitionException(string message) : base(message) { }
        public DefinitionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>A command is not legal in the current state. The state is left unchanged.</summary>
    public sealed class InvalidCommandException : Exception
    {
        public InvalidCommandException(string message) : base(message) { }
    }
}
