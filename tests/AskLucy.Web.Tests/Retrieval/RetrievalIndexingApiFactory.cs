using AskLucy.Application.Abstractions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ClearExtensions;

namespace AskLucy.Web.Tests.Retrieval;

/// <summary>
/// One real host for the whole class, with a real database (T236's explicit requirement, unlike
/// <c>NotificationsApiFactory</c>/<c>CustomModelsApiFactory</c>, which substitute every
/// repository) — knowledge bases, documents, chunks, embeddings, notifications and operational
/// failures are all written and read through the real EF Core repositories/<c>AskLucyDbContext</c>.
///
/// <para>Three things are still faked, each for a reason that has nothing to do with "real DB":
/// <see cref="EmbeddingServiceResolver"/> stands in for whichever cloud/local embedding vendor a
/// knowledge base's provider row names, so a test controls the embedding outcome (success/failure)
/// without a live vendor call — this is the fake tasks.md T236 names explicitly.
/// <see cref="Jobs"/> replaces <see cref="IBackgroundJobClient"/> so the enqueued
/// <c>KnowledgeBaseIndexingJob.RunAsync</c> call is captured rather than actually handed to a
/// Hangfire server (the same reason <c>CustomModelsApiFactory</c> fakes it) — the test resolves
/// <see cref="IKnowledgeBaseIndexingJob"/> itself from a fresh scope and invokes it with the
/// captured job id, so the run still exercises the real job class against the real database.
/// <see cref="IndexingNotifier"/> replaces <see cref="IRetrievalIndexingNotifier"/> (a pure
/// SignalR push, ADR: no persistence of its own) with a spy, since asserting "a hub event was
/// sent" through a live SignalR client connection would test the hub/transport, not this feature.
/// </para>
/// </summary>
public sealed class RetrievalIndexingApiFactory : CustomWebApplicationFactory
{
    public FakeEmbeddingServiceResolver EmbeddingServiceResolver { get; } = new();

    public IBackgroundJobClient Jobs { get; } = Substitute.For<IBackgroundJobClient>();

    public IRetrievalIndexingNotifier IndexingNotifier { get; } = Substitute.For<IRetrievalIndexingNotifier>();

    /// <summary>Forgets every stub and received call left by the previous test.</summary>
    public void Reset()
    {
        EmbeddingServiceResolver.Reset();
        Jobs.ClearSubstitute(ClearOptions.All);
        IndexingNotifier.ClearSubstitute(ClearOptions.All);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingServiceResolver>();
            services.AddSingleton<IEmbeddingServiceResolver>(EmbeddingServiceResolver);
            services.RemoveAll<IBackgroundJobClient>();
            services.AddSingleton(Jobs);
            services.RemoveAll<IRetrievalIndexingNotifier>();
            services.AddSingleton(IndexingNotifier);
        });
    }
}
