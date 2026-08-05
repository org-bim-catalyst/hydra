using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Infrastructure.Retrieval;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Retrieval;

/// <summary>ADR-0007 — proves <see cref="VectorStoreResolver"/> resolves the matching <see cref="IVectorStore"/> by <see cref="VectorStoreProvider"/> and fails loudly for an unregistered one.</summary>
public sealed class VectorStoreResolverTests
{
    [Fact]
    public void Resolve_ShouldReturnTheStoreMatchingTheRequestedProvider()
    {
        var sqlServerStore = Substitute.For<IVectorStore>();
        sqlServerStore.Provider.Returns(VectorStoreProvider.SqlServer);

        var pineconeStore = Substitute.For<IVectorStore>();
        pineconeStore.Provider.Returns(VectorStoreProvider.Pinecone);

        var resolver = new VectorStoreResolver([sqlServerStore, pineconeStore]);

        resolver.Resolve(VectorStoreProvider.Pinecone).Should().BeSameAs(pineconeStore);
        resolver.Resolve(VectorStoreProvider.SqlServer).Should().BeSameAs(sqlServerStore);
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenNoStoreIsRegisteredForTheProvider()
    {
        var resolver = new VectorStoreResolver([]);

        var act = () => resolver.Resolve(VectorStoreProvider.Pinecone);

        act.Should().Throw<InvalidOperationException>();
    }
}
