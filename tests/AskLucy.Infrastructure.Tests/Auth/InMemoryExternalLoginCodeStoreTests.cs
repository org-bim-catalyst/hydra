using AskLucy.Infrastructure.Auth;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Auth;

public sealed class InMemoryExternalLoginCodeStoreTests
{
    private readonly InMemoryExternalLoginCodeStore _store = new();

    [Fact]
    public void TryConsume_ShouldReturnTheIssuedUserId()
    {
        var code = _store.Issue("user-1", TimeSpan.FromMinutes(1));

        _store.TryConsume(code).Should().Be("user-1");
    }

    [Fact]
    public void TryConsume_ShouldBeSingleUse()
    {
        var code = _store.Issue("user-1", TimeSpan.FromMinutes(1));

        _store.TryConsume(code);
        var second = _store.TryConsume(code);

        second.Should().BeNull();
    }

    [Fact]
    public void TryConsume_ShouldReturnNull_ForAnUnknownCode()
    {
        _store.TryConsume("never-issued").Should().BeNull();
    }

    [Fact]
    public void TryConsume_ShouldReturnNull_WhenTheCodeHasExpired()
    {
        var code = _store.Issue("user-1", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(20);

        _store.TryConsume(code).Should().BeNull();
    }

    [Fact]
    public void Issue_ShouldReturnDistinctCodesEachTime()
    {
        var first = _store.Issue("user-1", TimeSpan.FromMinutes(1));
        var second = _store.Issue("user-1", TimeSpan.FromMinutes(1));

        first.Should().NotBe(second);
    }
}
