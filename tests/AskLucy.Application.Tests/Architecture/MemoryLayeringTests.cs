using System.Reflection;
using AskLucy.Application.Memory;
using FluentAssertions;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Architecture;

/// <summary>
/// tasks.md T100 (constitution §2.I structural check) — no <c>AskLucy.Domain</c>/<c>AskLucy.Application</c>
/// type in the <c>Memory</c>/<c>Projects</c> bounded contexts references a specific AI vendor SDK
/// or raw SQL vector syntax directly, mirroring the same guarantee already proved (informally, via
/// each project's own <c>.csproj</c> package list) for the rest of this codebase's Clean
/// Architecture boundary.
///
/// <para>Two complementary checks: (1) neither assembly references a SQL client or AI vendor SDK
/// package at all (reflection over <see cref="Assembly.GetReferencedAssemblies"/> — if the whole
/// assembly never references one, no type within it possibly could either); (2) the actual
/// <c>Memory</c>/<c>Projects</c> source files under <c>AskLucy.Domain</c>/<c>AskLucy.Application</c>
/// never contain the raw-SQL vector syntax (<c>VECTOR_DISTANCE</c>/<c>CREATE VECTOR INDEX</c>) that
/// legitimately exists only in <c>AskLucy.Persistence</c>'s <c>SqlServerMemoryVectorStore</c>.</para>
/// </summary>
public sealed class MemoryLayeringTests
{
    private static readonly string[] BannedAssemblyNameSubstrings =
    [
        "Microsoft.Data.SqlClient", "System.Data.SqlClient", "Npgsql",
        "OpenAI", "Anthropic", "Pinecone",
    ];

    private static readonly string[] BannedSourceSubstrings =
    [
        "VECTOR_DISTANCE", "CREATE VECTOR INDEX", "SqlConnection", "SqlCommand",
    ];

    [Fact]
    public void DomainAssembly_ShouldNeverReference_ASqlClientOrAiVendorSdkPackage()
    {
        var referencedAssemblyNames = typeof(MemoryEntity).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referencedAssemblyNames.Should().NotContain(name =>
            BannedAssemblyNameSubstrings.Any(banned => name.Contains(banned, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ApplicationAssembly_ShouldNeverReference_ASqlClientOrAiVendorSdkPackage()
    {
        var referencedAssemblyNames = typeof(MemoryService).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referencedAssemblyNames.Should().NotContain(name =>
            BannedAssemblyNameSubstrings.Any(banned => name.Contains(banned, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MemoryAndProjectsSourceFiles_InDomainAndApplication_ShouldNeverContainRawSqlVectorSyntax()
    {
        var repoRoot = FindRepoRoot();
        var directoriesToScan = new[]
        {
            Path.Combine(repoRoot, "src", "AskLucy.Domain", "Memory"),
            Path.Combine(repoRoot, "src", "AskLucy.Domain", "Projects"),
            Path.Combine(repoRoot, "src", "AskLucy.Application", "Memory"),
            Path.Combine(repoRoot, "src", "AskLucy.Application", "Projects"),
        };

        var violations = new List<string>();
        foreach (var directory in directoriesToScan.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                violations.AddRange(
                    BannedSourceSubstrings.Where(banned => content.Contains(banned, StringComparison.Ordinal))
                        .Select(banned => $"{file}: contains '{banned}'"));
            }
        }

        violations.Should().BeEmpty();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ask Lucy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Ask Lucy.sln not found above the test output directory).");
    }
}
