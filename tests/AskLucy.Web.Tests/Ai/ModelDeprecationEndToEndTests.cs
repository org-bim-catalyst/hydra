using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Application.Ai.Queries.GetModelDeprecations;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>Boots the host with a vendor that has dropped one model, so the diff can see it.</summary>
public sealed class ModelDeprecationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var resolver = Substitute.For<IAIProviderResolver>();
            var provider = Substitute.For<IAIProvider>();
            // The vendor now lists only "new" — "old" is the retirement the admin will confirm.
            provider.ListAvailableModelsAsync(Arg.Any<CancellationToken>()).Returns(
            [
                new ProviderModelInfo("new", "New Model", null, null,
                    new AIModelCapabilities(true, false, false, false, false, false, false, false, false)),
            ]);
            resolver.Resolve(Arg.Any<string>()).Returns(provider);
            services.RemoveAll<IAIProviderResolver>();
            services.AddSingleton(resolver);
        });
    }
}

/// <summary>
/// specs/069 quickstart S1–S5 through the real endpoints and database: the impact preview,
/// deprecation with the default moved, the user-item switch and notice, validation, and the lock.
/// </summary>
[Collection(DatabaseSerialGroup.Name)]
public sealed class ModelDeprecationEndToEndTests(ModelDeprecationFactory factory)
    : IClassFixture<ModelDeprecationFactory>, IAsyncLifetime
{
    private const string Base = "/api/v1/admin/ai";
    private readonly List<string> _userIds = [];

    private Guid _providerId;
    private Guid _retiredId;
    private Guid _replacementId;
    private Guid _thirdId;
    private string _ownerId = string.Empty;
    private Guid _chatId;
    private Guid batchIdOfProvider;

    public async ValueTask InitializeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        _ownerId = await SeedUserAsync(userManager);
        var provider = AIProvider.Create($"dep-{Guid.NewGuid():N}", "Deprecation Test Provider", "test");
        var retired = AIModel.Create(provider.Id, "old", "Old Model", 128_000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "test");
        var replacement = AIModel.Create(provider.Id, "new", "New Model", 128_000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "test");
        var third = AIModel.Create(provider.Id, "third", "Third Model", 128_000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "test");
        var chat = UserChat.Create("Chat on the old model", _ownerId, null, _ownerId);
        chat.SetModelSelection(provider.Id, retired.Id, null, _ownerId);

        db.AIProviders.Add(provider);
        db.AIModels.AddRange(retired, replacement, third);
        db.UserChats.Add(chat);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The default points back at a model on this provider, so it is set in a second save.
        provider.SetDefaultModel(retired.Id, "test");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _providerId = provider.Id;
        _retiredId = retired.Id;
        _replacementId = replacement.Id;
        _thirdId = third.Id;
        _chatId = chat.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.UserChats.IgnoreQueryFilters().Where(c => c.Id == _chatId).ExecuteDeleteAsync();
        await db.ItemSwitches.IgnoreQueryFilters().Where(s => s.ItemId == _chatId).ExecuteDeleteAsync();
        var batches = await db.ModelDeprecationBatches.Where(b => b.ProviderId == _providerId).Select(b => b.Id).ToListAsync();
        var records = await db.ModelDeprecations.Where(d => batches.Contains(d.BatchId)).Select(d => d.Id).ToListAsync();
        await db.PlatformDefaultChanges.IgnoreQueryFilters().Where(c => records.Contains(c.DeprecationId)).ExecuteDeleteAsync();
        await db.ItemSwitches.IgnoreQueryFilters().Where(s => batches.Contains(s.BatchId)).ExecuteDeleteAsync();
        await db.ModelDeprecations.IgnoreQueryFilters().Where(d => batches.Contains(d.BatchId)).ExecuteDeleteAsync();
        await db.ModelDeprecationBatches.IgnoreQueryFilters().Where(b => batches.Contains(b.Id)).ExecuteDeleteAsync();
        var providerRow = await db.AIProviders.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == _providerId);
        if (providerRow is not null)
        {
            providerRow.SetDefaultModel(null, "test");
            await db.SaveChangesAsync();
        }

        await db.AIModels.IgnoreQueryFilters().Where(m => m.ProviderId == _providerId).ExecuteDeleteAsync();
        await db.AIProviders.IgnoreQueryFilters().Where(p => p.Id == _providerId).ExecuteDeleteAsync();
        await db.NotificationOutboxEvents.IgnoreQueryFilters()
            .Where(e => e.EventKey != null && e.EventKey.StartsWith("ai-model-deprecation:"))
            .Where(e => e.EventKey.Contains(_providerId.ToString()))
            .ExecuteDeleteAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in _userIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.Create(
                _ownerId,
                ["Administrator"],
                ["admin.ai-providers.manage", "admin.ai-providers.view"]));
        return client;
    }

    private async Task<string> SeedUserAsync(UserManager<ApplicationUser> userManager)
    {
        var email = $"dep-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        // Permissions of a real account are read from the database, not the token.
        (await userManager.AddToRoleAsync(user, "Administrator")).Succeeded.Should().BeTrue();
        _userIds.Add(user.Id);
        return user.Id;
    }

    [Fact]
    public async Task S1_DiffShowsTheImpactAndApplyDeprecates_MovingTheDefault()
    {
        var client = AdminClient();

        var diff = await client.PostAsync(
            $"{Base}/providers/{_providerId}/models/actions/sync", content: null, TestContext.Current.CancellationToken);
        diff.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await diff.Content.ReadFromJsonAsync<ProviderModelSyncDiffDto>(TestContext.Current.CancellationToken);
        var row = body!.RemovedFromVendor.Should().ContainSingle(r => r.Id == _retiredId).Subject;
        (row.Impact!.ProposedReplacement!.Id == _replacementId || row.Impact.ProposedReplacement.Id == _thirdId).Should().BeTrue();
        (row.Impact.Conversations + row.Impact.Agents + row.Impact.Prompts + row.Impact.WorkflowSteps).Should().Be(1);
        row.Impact.DistinctUsers.Should().Be(1);

        var apply = await client.PostAsJsonAsync(
            $"{Base}/providers/{_providerId}/models/actions/sync/apply",
            new { added = Array.Empty<object>(), removedFromVendor = new[] { new { id = _retiredId, modelKey = "old", displayName = "Old Model" } } },
            TestContext.Current.CancellationToken);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await apply.Content.ReadFromJsonAsync<ApplyProviderModelSyncResultDto>(TestContext.Current.CancellationToken);
        result!.Failed.Should().BeEmpty();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        (await db.AIModels.SingleAsync(m => m.Id == _retiredId, TestContext.Current.CancellationToken)).Status
            .Should().Be(AIModelStatus.Deprecated);
        var provider = await db.AIProviders.SingleAsync(p => p.Id == _providerId, TestContext.Current.CancellationToken);
        (provider.DefaultModelId == _replacementId || provider.DefaultModelId == _thirdId).Should().BeTrue();
        var record = await db.ModelDeprecations.SingleAsync(d => d.ModelId == _retiredId, TestContext.Current.CancellationToken);
        record.ValidationStatus.Should().Be(ReplacementValidationStatus.AwaitingValidation);

        // S5: the lock holds at the endpoint, not just in the screen.
        var unlock = await client.PatchAsJsonAsync(
            $"{Base}/models/{_retiredId}", new { status = AIModelStatus.Available }, TestContext.Current.CancellationToken);
        unlock.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var reDeprecate = await client.PatchAsJsonAsync(
            $"{Base}/models/{_replacementId}", new { status = AIModelStatus.Deprecated }, TestContext.Current.CancellationToken);
        reDeprecate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // S3: apply enqueued the impact job. Wait for Hangfire to run it instead of calling the processor
        // here — a second in-process run would race the enqueued one on the same item row versions.
        Guid batchId;
        await using (var jobScope = factory.Services.CreateAsyncScope())
        {
            batchId = await jobScope.ServiceProvider.GetRequiredService<AskLucyDbContext>()
                .ModelDeprecationBatches.Where(b => b.ProviderId == _providerId)
                .Select(b => b.Id).SingleAsync(TestContext.Current.CancellationToken);
        }
        batchIdOfProvider = batchId;

        var impactDeadline = DateTime.UtcNow.AddSeconds(60);
        UserImpactStatus impactStatus;
        do
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
            await using var pollScope = factory.Services.CreateAsyncScope();
            impactStatus = await pollScope.ServiceProvider.GetRequiredService<AskLucyDbContext>()
                .ModelDeprecationBatches.AsNoTracking()
                .Where(b => b.Id == batchId)
                .Select(b => b.UserImpactStatus)
                .SingleAsync(TestContext.Current.CancellationToken);
        }
        while (impactStatus != UserImpactStatus.Completed && DateTime.UtcNow < impactDeadline);

        impactStatus.Should().Be(UserImpactStatus.Completed);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var db2 = verifyScope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            (await db2.UserChats.SingleAsync(c => c.Id == _chatId, TestContext.Current.CancellationToken)).ModelId
                .Should().Be(provider.DefaultModelId);
            var expectedKey = $"ai-model-deprecation:{batchIdOfProvider}:{_ownerId}";
            (await db2.NotificationOutboxEvents.CountAsync(e => e.EventKey == expectedKey, TestContext.Current.CancellationToken))
                .Should().Be(1);
        }

        // S4: the replacement is accepted and stays accepted; a second accept conflicts.
        var accept = await client.PostAsync(
            $"{Base}/deprecations/{record.Id}/actions/accept-replacement", content: null, TestContext.Current.CancellationToken);
        accept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = await client.PostAsync(
            $"{Base}/deprecations/{record.Id}/actions/accept-replacement", content: null, TestContext.Current.CancellationToken);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var check = factory.Services.CreateAsyncScope())
        {
            var stored = await check.ServiceProvider.GetRequiredService<AskLucyDbContext>()
                .ModelDeprecations.AsNoTracking().SingleAsync(d => d.Id == record.Id, TestContext.Current.CancellationToken);
            stored.ValidationStatus.Should().Be(ReplacementValidationStatus.Accepted);
        }

        // The API serializes enums as strings API-wide; the default reader expects numbers.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var raw = await client.GetStringAsync($"{Base}/deprecations/{record.Id}", TestContext.Current.CancellationToken);
        raw.Should().Contain("\"validationStatus\":\"Accepted\"", raw);
        var detail = JsonSerializer.Deserialize<ModelDeprecationDetailDto>(raw, jsonOptions);
        detail!.ValidationStatus.Should().Be(ReplacementValidationStatus.Accepted);
        detail.NotifiedUserCount.Should().Be(1);
    }


}
