using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One real host, real database: the notification center is read through the real HTTP API.</summary>
public sealed class NotificationCenterPerformanceFactory : CustomWebApplicationFactory;


/// <summary>
/// T224 — specs/067 SC-004: "the unread count and the first page of the notification center load in under 1 second
/// for a user with 100,000 historical notifications". Seeds that history for one user with set-based SQL, then reads it
/// through the HTTP API (auth, rate limiter, MediatR, repository, keyset query, mapping, serialization) and asserts the
/// 95th percentile of three timings, each against SC-004's 1 second: the first page, deep keyset pages (the cursor
/// taken at 10%..95% through the history, where a bad plan would scan), and the unread count.
/// <para>
/// SC-004 gives one number, 1 s, so every p95 assertion uses it (not the 300 ms / 100 ms defaults, which apply only
/// when the spec is silent). 40 samples each after warm-up; the endpoints are limited to 120 requests a minute per user,
/// so a 429 is waited out and retried untimed — it is the limiter, not the query, answering. Gated with the other
/// wall-clock tests: set <c>RUN_SCALE_PERFORMANCE_TESTS=1</c> (deviation recorded in plan.md Complexity Tracking).
/// <c>NOTIFICATION_SCALE_FRACTION</c> shrinks the history for a smoke run.
/// </para>
/// </summary>
[Collection(NotificationScaleTestGroup.Name)]
public sealed class NotificationCenterPerformanceTests(NotificationCenterPerformanceFactory factory, ITestOutputHelper output)
    : IClassFixture<NotificationCenterPerformanceFactory>
{
    private const int FullHistory = 100_000;
    private const int Samples = 40;
    private const int PageSize = 25;
    private const double TargetSeconds = 1.0;

    [Fact(Skip = AccountEmailLatencyGate.SkipReason, SkipWhen = nameof(AccountEmailLatencyGate.NotRequested), SkipType = typeof(AccountEmailLatencyGate))]
    public async Task UnreadCountAndPages_StayUnderOneSecond_ForAUserWith100kNotifications()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var total = NotificationScaleTestSupport.Scaled(FullHistory, 2_000);
        var users = await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "ctr", run, 1);
        var userId = users[0];

        try
        {
            // Newest first: item i is i seconds older than the base. Every fifth item is unread.
            var newest = DateTime.UtcNow.AddMinutes(-5);
            var seedWatch = Stopwatch.StartNew();
            await SeedHistoryAsync(userId, total, newest, ct);
            output.WriteLine($"Seeded {total:N0} notifications in {seedWatch.Elapsed.TotalSeconds:F1}s.");

            var keys = await LoadKeysAsync(userId, ct);
            keys.Should().HaveCount(total);
            var expectedUnread = (total + 4) / 5;

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId));

            // Deep cursors: "after the item at offset k", exactly what the client sends for the next page.
            var offsets = Enumerable.Range(0, Samples).Select(j => (int)((total - PageSize - 2) * (0.10 + 0.85 * j / (Samples - 1)))).ToList();

            // Warm-up (plans, JIT, connection pool): untimed, and the answers are still checked.
            (await GetAsync(client, "/api/v1/notifications?limit=25", ct)).Body.Should().NotBeNull();
            (await GetAsync(client, $"/api/v1/notifications?limit=25&cursor={Uri.EscapeDataString(CursorAfter(keys[offsets[0]]))}", ct)).Body.Should().NotBeNull();
            (await GetAsync(client, "/api/v1/notifications/unread-count", ct)).Body.Should().NotBeNull();

            var firstPage = new List<double>();
            var deepPages = new List<double>();
            var unreadCount = new List<double>();

            for (var j = 0; j < Samples; j++)
            {
                var first = await GetAsync(client, "/api/v1/notifications?limit=25", ct);
                firstPage.Add(first.Seconds);
                ItemIds(first.Body!).Should().Equal(keys.Take(PageSize).Select(k => k.Id), "the first page is the 25 newest, in order");

                var offset = offsets[j];
                var deep = await GetAsync(client, $"/api/v1/notifications?limit=25&cursor={Uri.EscapeDataString(CursorAfter(keys[offset]))}", ct);
                deepPages.Add(deep.Seconds);
                ItemIds(deep.Body!).Should().Equal(keys.Skip(offset + 1).Take(PageSize).Select(k => k.Id), $"the page after offset {offset} continues exactly there");

                var unread = await GetAsync(client, "/api/v1/notifications/unread-count", ct);
                unreadCount.Add(unread.Seconds);
                unread.Body!.RootElement.GetProperty("count").GetInt32().Should().Be(expectedUnread);
            }

            output.WriteLine($"SC-004 over {total:N0} notifications ({NotificationScaleTestSupport.Fraction:P0} of 100k), {Samples} samples each (seconds):");
            Report("first page", firstPage);
            Report("deep page ", deepPages);
            Report("unread    ", unreadCount);

            NotificationScaleTestSupport.Percentile(firstPage, 0.95).Should().BeLessThan(TargetSeconds, "SC-004: the first page loads in under 1 s");
            NotificationScaleTestSupport.Percentile(deepPages, 0.95).Should().BeLessThan(TargetSeconds, "SC-004: deep keyset pages stay fast, too");
            NotificationScaleTestSupport.Percentile(unreadCount, 0.95).Should().BeLessThan(TargetSeconds, "SC-004: the unread count loads in under 1 s");
        }
        finally
        {
            await NotificationScaleTestSupport.CleanupAsync(factory.Services, users, eventKeyPrefix: null);
        }
    }

    private void Report(string name, List<double> samples) =>
        output.WriteLine($"  {name}: p50={NotificationScaleTestSupport.Percentile(samples, 0.5):F3} p95={NotificationScaleTestSupport.Percentile(samples, 0.95):F3} max={samples.Max():F3}");

    /// <summary>Times one successful GET. A 429 (the endpoint's 120/min limit) is waited out and retried without being timed.</summary>
    private static async Task<(double Seconds, JsonDocument? Body)> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15), ct);
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {url} failed: {body}");
            return (seconds, JsonDocument.Parse(body));
        }

        throw new TimeoutException($"GET {url} was still rate limited after 5 waits.");
    }

    private static List<Guid> ItemIds(JsonDocument page) =>
        page.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    /// <summary>
    /// The keyset cursor the API issues: base64 of <c>{"CreatedAtUtcTicks":..,"Id":".."}</c>
    /// (<c>NotificationCursor</c>, internal to the persistence assembly, so the shape is repeated here). A wrong shape
    /// fails fast: the API answers 400 and the page assertions would not match.
    /// </summary>
    private static string CursorAfter((DateTime CreatedAtUtc, Guid Id) key) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { CreatedAtUtcTicks = key.CreatedAtUtc.Ticks, key.Id })));

    private async Task<List<(DateTime CreatedAtUtc, Guid Id)>> LoadKeysAsync(string userId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.ShowInCenter)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Select(n => new { n.CreatedAtUtc, n.Id })
            .ToListAsync(ct);
        return rows.Select(r => (DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc), r.Id)).ToList();
    }

    /// <summary>
    /// Set-based seeding on the server, 20,000 rows a statement: a row-by-row EF insert of 100k rows over a remote
    /// connection is the slow part of a test, not the thing being measured. The columns are the ones the model
    /// requires; deliveries are not seeded because the center's list and count queries never read them.
    /// </summary>
    private async Task SeedHistoryAsync(string userId, int total, DateTime newest, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        const int chunk = 20_000;
        var type = NotificationTypeKeys.WorkflowExecutionCompleted;
        var category = nameof(NotificationCategory.Workflow);
        var priority = nameof(NotificationPriority.Normal);
        var unread = nameof(NotificationStatus.Delivered);
        var read = nameof(NotificationStatus.Read);

        for (var start = 0; start < total; start += chunk)
        {
            var take = Math.Min(chunk, total - start);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                ;WITH n AS (
                    SELECT TOP ({take}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 + {start} AS i
                    FROM sys.all_columns a CROSS JOIN sys.all_columns b)
                INSERT INTO Notifications
                    (Id, RecipientUserId, Category, [Type], Title, [Message], Priority, [Status], [Language], CorrelationId,
                     ShowInCenter, ReadAtUtc, CreatedAtUtc, CreatedBy)
                SELECT NEWID(), {userId}, {category}, {type},
                    N'Seeded notification ' + CAST(i AS nvarchar(20)), N'Seeded history item ' + CAST(i AS nvarchar(20)),
                    {priority}, CASE WHEN i % 5 = 0 THEN {unread} ELSE {read} END, N'en', N'scale-seed', 1,
                    CASE WHEN i % 5 = 0 THEN NULL ELSE DATEADD(SECOND, -i, {newest}) END,
                    DATEADD(SECOND, -i, {newest}), N'scale-seed'
                FROM n
                """, ct);
        }
    }
}
