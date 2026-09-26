using Xunit;

namespace AskLucy.Web.Tests.Authorization;

/// <summary>
/// Serialises the test classes that flip or read the built-in Administrator role's View user content
/// grant. It is one row shared by the whole database, so while <c>ContentPermissionGrantPathsTests</c>
/// has it switched on, a parallel class asserting an Administrator can't read content sees it granted.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AdministratorContentAccessCollection
{
    public const string Name = "Administrator content access";
}
