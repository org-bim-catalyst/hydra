using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.OperationalFailures;
using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>specs/074 T014 — a running job's id wins, then the request's, otherwise null (research D11).</summary>
public sealed class CorrelationIdAccessorTests
{
    [Fact]
    public async Task Current_ShouldPreferTheJobId_ThenTheRequestId_ThenNull()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdKeys.ItemsKey] = "request-id";
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(context);
        var accessor = new CorrelationIdAccessor(httpContextAccessor);

        // Its own async flow, so the AsyncLocal set here never leaks into another test.
        await Task.Run(() =>
        {
            JobCorrelationContext.Current = "job-id";
            accessor.Current.Should().Be("job-id");

            JobCorrelationContext.Current = null;
            accessor.Current.Should().Be("request-id");
        }, TestContext.Current.CancellationToken);

        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Current_ShouldBeNull_WhenTheRequestHasNoId()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        new CorrelationIdAccessor(httpContextAccessor).Current.Should().BeNull();
    }

    [Fact]
    public async Task Middleware_ShouldStoreTheIdUnderTheSharedKey()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "inbound";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Items[CorrelationIdKeys.ItemsKey].Should().Be("inbound");
    }
}
