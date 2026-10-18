using Xunit;

namespace AskLucy.Web.Tests;

/// <summary>
/// Serialises every test class that talks to the shared CI database. Tests inside this collection run one after another, so they
/// never load the shared SQL host at the same time; test classes outside it still run in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseSerialGroup
{
    public const string Name = "Database serial group";
}
