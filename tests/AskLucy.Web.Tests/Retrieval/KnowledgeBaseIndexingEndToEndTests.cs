using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Domain.Retrieval;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Retrieval;

/// <summary>
/// specs/067-notifications-communication-hub T236 — the Independent Test above T235: upload a
/// Markdown file through the real API against a real database, with only the embedding vendor
/// faked; the document's chunks/embeddings must exist, the knowledge base's index status must go
/// Indexing → Indexed with a hub event per transition, and exactly one <c>document.indexing.completed</c>
/// / one <c>knowledge-base.indexing.completed</c> notification must reach the owner. With a failing
/// embedding vendor, the job/knowledge base must end Failed, one <c>*.indexing.failed</c> notification
/// of each kind must arrive, and the failure must land on the spec 074 admin trail.
///
/// <para>The upload request itself runs the real
/// <see cref="AskLucy.Application.Retrieval.Indexing.KnowledgeBaseDocumentUploadedIndexingHandler"/>
/// inline (it is an <c>INotificationHandler</c>, awaited by <c>UploadDocumentCommandHandler</c>'s
/// own publish call); the actual <c>KnowledgeBaseIndexingJob.RunAsync</c> run is driven by this test
/// resolving <see cref="IKnowledgeBaseIndexingJob"/> from a fresh scope with the job id captured off
/// the faked <see cref="IBackgroundJobClient"/>, rather than waiting on a real Hangfire server (see
/// <see cref="RetrievalIndexingApiFactory"/>'s own doc comment).</para>
/// </summary>
[Collection(nameof(RetrievalIndexingTestGroup))]
public sealed class KnowledgeBaseIndexingEndToEndTests(RetrievalIndexingApiFactory factory) : IClassFixture<RetrievalIndexingApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Indexing_ShouldChunkEmbedAndNotifyTheOwnerOnce_WhenTheEmbeddingVendorSucceeds()
    {
        factory.Reset();
        var ct = TestContext.Current.CancellationToken;
        var ownerId = await SeedUserAsync();
        try
        {
            AuthenticateAs(ownerId);
            await EnsureDefaultCloudEmbeddingProviderAsync();

            var knowledgeBaseId = await CreateKnowledgeBaseAsync();
            await UseSqlServerVectorStoreAsync(knowledgeBaseId);

            var jobId = await UploadMarkdownAndCaptureJobIdAsync(knowledgeBaseId);

            await RunIndexingJobAsync(jobId);

            var (chunkCount, embeddingCount, indexStatus) = await QueryAsync(async db =>
            {
                var chunks = await db.DocumentChunks.Where(c => c.KnowledgeBaseId == knowledgeBaseId).ToListAsync(ct);
                var embeddings = await db.Embeddings.Where(e => chunks.Select(c => c.Id).Contains(e.DocumentChunkId)).CountAsync(ct);
                var status = (await db.KnowledgeBases.SingleAsync(k => k.Id == knowledgeBaseId, ct)).IndexStatus;
                return (chunks.Count, embeddings, status);
            });

            chunkCount.Should().BeGreaterThan(0);
            embeddingCount.Should().Be(chunkCount);
            indexStatus.Should().Be(KnowledgeBaseIndexStatus.Indexed);

            await factory.IndexingNotifier.Received(1).NotifyStageChangedAsync(
                ownerId, knowledgeBaseId, jobId, "Indexing", "InProgress", Arg.Any<CancellationToken>());
            await factory.IndexingNotifier.Received(1).NotifyStageChangedAsync(
                ownerId, knowledgeBaseId, jobId, "Indexing", "Completed", Arg.Any<CancellationToken>());
            await factory.IndexingNotifier.Received(1).NotifyIndexStatusChangedAsync(
                ownerId, knowledgeBaseId, "Indexed", Arg.Any<CancellationToken>());

            await FlushOutboxAsync();
            (await CountNotificationsAsync(ownerId, "document.indexing.completed")).Should().Be(1);
            (await CountNotificationsAsync(ownerId, "knowledge-base.indexing.completed")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(ownerId);
        }
    }

    [Fact]
    public async Task Indexing_ShouldFailTheJobAndKnowledgeBaseAndRecordAnIncident_WhenTheEmbeddingVendorFails()
    {
        factory.Reset();
        var ct = TestContext.Current.CancellationToken;
        var ownerId = await SeedUserAsync();
        try
        {
            AuthenticateAs(ownerId);
            await EnsureDefaultCloudEmbeddingProviderAsync();

            var knowledgeBaseId = await CreateKnowledgeBaseAsync();
            await UseSqlServerVectorStoreAsync(knowledgeBaseId);

            var jobId = await UploadMarkdownAndCaptureJobIdAsync(knowledgeBaseId);

            factory.EmbeddingServiceResolver.ShouldFail = true;
            await RunIndexingJobAsync(jobId);

            var (jobStatus, indexStatus) = await QueryAsync(async db =>
            {
                var job = await db.IndexingJobs.SingleAsync(j => j.Id == jobId, ct);
                var kb = await db.KnowledgeBases.SingleAsync(k => k.Id == knowledgeBaseId, ct);
                return (job.Status, kb.IndexStatus);
            });

            jobStatus.Should().Be(IndexingJobStatus.Failed);
            indexStatus.Should().Be(KnowledgeBaseIndexStatus.Failed);

            await FlushOutboxAsync();
            (await CountNotificationsAsync(ownerId, "document.indexing.failed")).Should().Be(1);
            (await CountNotificationsAsync(ownerId, "knowledge-base.indexing.failed")).Should().Be(1);

            var incident = await PollForIncidentAsync(knowledgeBaseId);
            incident.Should().NotBeNull();
            incident!.Engine.Should().Be(OperationalFailureEngine.DocumentProcessing);
        }
        finally
        {
            await CleanupAsync(ownerId);
        }
    }

    // --- setup helpers ---

    private void AuthenticateAs(string userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId));

    private async Task<string> SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"retrieval-indexing-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };

        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>The shared test database can be missing the migration-seeded baseline rows (project memory: the persistence-test fixture wipes EF-model tables between runs) — restored the same way <c>DevBaselineSeeder</c> does, idempotently.</summary>
    private async Task EnsureDefaultCloudEmbeddingProviderAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var providers = scope.ServiceProvider.GetRequiredService<IEmbeddingProviderRepository>();
        if (await providers.GetDefaultAsync(EmbeddingHostingType.Cloud, TestContext.Current.CancellationToken) is not null)
        {
            return;
        }

        providers.Add(EmbeddingProvider.Create("OpenAI", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, isDefault: true, "test-seed"));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CreateKnowledgeBaseAsync()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/knowledge-bases",
            new CreateKnowledgeBaseRequest($"T236 {Guid.NewGuid():N}", null, null, null, null, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await response.Content.ReadFromJsonAsync<KnowledgeBaseCreatedResponse>(TestContext.Current.CancellationToken);
        return body!.Id;
    }

    /// <summary>New knowledge bases default to <see cref="VectorStoreProvider.Pinecone"/> (ADR-0007); this test needs the DB-backed store so the run stays self-contained (no live Pinecone call).</summary>
    private async Task UseSqlServerVectorStoreAsync(Guid knowledgeBaseId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseRepository>();
        var knowledgeBase = await repository.GetByIdAsync(knowledgeBaseId, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        knowledgeBase.UpdateRetrievalSettings(
            AskLucy.Domain.Retrieval.ChunkingStrategy.Recursive, embeddingProviderId: null, EmbeddingHostingType.Cloud,
            VectorStoreProvider.SqlServer, requiresDataResidency: false, "test-seed");

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> UploadMarkdownAndCaptureJobIdAsync(Guid knowledgeBaseId)
    {
        Job? captured = null;
        factory.Jobs.Create(Arg.Do<Job>(j => captured = j), Arg.Any<IState>()).Returns("hangfire-job-1");

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("# T236\n\nSome indexable prose for the end-to-end test to chunk and embed.\n"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(fileContent, "file", "t236.md");

        using var response = await _client.PostAsync(
            $"/api/v1/knowledge-bases/{knowledgeBaseId}/documents", form, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        captured.Should().NotBeNull("the upload's DocumentUploadedNotification handler enqueues the indexing job inline");
        return (Guid)captured!.Args[0]!;
    }

    private async Task RunIndexingJobAsync(Guid jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexingJob>().RunAsync(jobId, TestContext.Current.CancellationToken);
    }

    /// <summary>Drains the real outbox (<see cref="OutboxDispatchService"/>) into <c>Notifications</c> rows, since nothing here runs the dispatcher's own <c>BackgroundService</c> loop.</summary>
    private async Task FlushOutboxAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatchService>();

        int claimed;
        var iterations = 0;
        do
        {
            claimed = await dispatcher.DispatchBatchAsync("t236-test-worker", TestContext.Current.CancellationToken);
            iterations++;
        }
        while (claimed > 0 && iterations < 10);
    }

    private async Task<int> CountNotificationsAsync(string ownerId, string type) => await QueryAsync(db =>
        db.Notifications.CountAsync(n => n.RecipientUserId == ownerId && n.Type == type, TestContext.Current.CancellationToken));

    private async Task<OperationalFailureIncident?> PollForIncidentAsync(Guid knowledgeBaseId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var incident = await QueryAsync(db => db.OperationalFailureIncidents.AsNoTracking()
                .Where(i => i.SubjectType == "KnowledgeBase" && i.SubjectId == knowledgeBaseId)
                .OrderByDescending(i => i.LastSeenUtc)
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken));

            if (incident is not null)
            {
                return incident;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return null;
    }

    private async Task<T> QueryAsync<T>(Func<AskLucyDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AskLucyDbContext>());
    }

    /// <summary>
    /// This is a real-DB shared-host suite (<see cref="RetrievalIndexingApiFactory"/>'s own doc
    /// comment), so every row this test creates — down to the seeded <see cref="ApplicationUser"/> —
    /// must be removed again, in FK-safe order (every knowledge-base child table uses
    /// <c>DeleteBehavior.Restrict</c>, so children must go before the knowledge base itself).
    /// </summary>
    private async Task CleanupAsync(string ownerId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        var knowledgeBaseIds = await db.KnowledgeBases.Where(k => k.OwnerId == ownerId).Select(k => k.Id).ToListAsync(ct);
        var documentIds = await db.DocumentChunks.Where(c => knowledgeBaseIds.Contains(c.KnowledgeBaseId)).Select(c => c.DocumentId).Distinct().ToListAsync(ct);

        await db.DocumentChunks.Where(c => knowledgeBaseIds.Contains(c.KnowledgeBaseId)).ExecuteDeleteAsync(ct);
        await db.IndexingJobs.Where(j => knowledgeBaseIds.Contains(j.KnowledgeBaseId)).ExecuteDeleteAsync(ct);
        await db.KnowledgeBaseDocuments.Where(d => knowledgeBaseIds.Contains(d.KnowledgeBaseId)).ExecuteDeleteAsync(ct);
        await db.DocumentVersions.Where(v => documentIds.Contains(v.DocumentId)).ExecuteDeleteAsync(ct);
        await db.Documents.Where(d => documentIds.Contains(d.Id)).ExecuteDeleteAsync(ct);
        await db.KnowledgeBases.Where(k => knowledgeBaseIds.Contains(k.Id)).ExecuteDeleteAsync(ct);
        await db.OperationalFailureIncidents.Where(i => i.SubjectType == "KnowledgeBase" && i.SubjectId != null && knowledgeBaseIds.Contains(i.SubjectId.Value)).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.RecipientUserId == ownerId).ExecuteDeleteAsync(ct);

        await using var identityScope = factory.Services.CreateAsyncScope();
        var userManager = identityScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(ownerId);
        if (user is not null)
        {
            await userManager.DeleteAsync(user);
        }
    }

    private sealed record KnowledgeBaseCreatedResponse(Guid Id);
}

/// <summary>xunit runs classes in a collection sequentially; not strictly required with one class today, but keeps this suite from ever running in parallel with another that shares <see cref="RetrievalIndexingApiFactory"/>'s real database writes.</summary>
[CollectionDefinition(nameof(RetrievalIndexingTestGroup))]
public sealed class RetrievalIndexingTestGroup;
