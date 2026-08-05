using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Web.Tests;

/// <summary>
/// Regression guard for a real, previously-undiscovered bug found while building specs/015 US7:
/// <c>AddControllers()</c> alone leaves enums serializing as their raw numeric ordinal
/// (verified empirically — a <c>DocumentProcessingStatus.Completed</c> field produced
/// <c>{"status":3}</c>, not <c>{"status":"Completed"}</c>), while every DTO across every module
/// returns enums directly and every frontend TypeScript comparison assumes a string. Fixed with
/// a global <see cref="JsonStringEnumConverter"/> in <c>Program.cs</c>. Asserts the DI-resolved
/// MVC JSON configuration itself (rather than a live controller round-trip) since this
/// environment has no database available for a full end-to-end request.
/// </summary>
public sealed class JsonEnumSerializationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public void MvcJsonOptions_ShouldIncludeAStringEnumConverter()
    {
        using var scope = factory.Services.CreateScope();
        var jsonOptions = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        jsonOptions.JsonSerializerOptions.Converters.Should().ContainSingle(c => c is JsonStringEnumConverter);
    }
}
