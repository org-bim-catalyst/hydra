using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Commands.CreateKnowledgeBase;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class CreateKnowledgeBaseCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly KnowledgeBaseDashboardSummaryCache _dashboardSummaryCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task Handle_ShouldCreateDraftKnowledgeBase_OwnedByCurrentUser()
    {
        _currentUser.UserId.Returns("user-1");
        var handler = new CreateKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _dashboardSummaryCache, _unitOfWork, _currentUser);

        var result = await handler.Handle(
            new CreateKnowledgeBaseCommand("BIM Standards", "desc", "#FFFFFF", "folder", null, ["revit", "standards"]),
            CancellationToken.None);

        result.Name.Should().Be("BIM Standards");
        result.Status.Should().Be(KnowledgeBaseStatus.Draft);
        result.Tags.Should().BeEquivalentTo(["revit", "standards"]);
        _repository.Received(1).Add(Arg.Is<KnowledgeBase>(k => k.OwnerId == "user-1" && k.Name == "BIM Standards"));
        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(a => a.Action == KnowledgeBaseAuditAction.Created));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var handler = new CreateKnowledgeBaseCommandHandler(_repository, _auditLogRepository, _dashboardSummaryCache, _unitOfWork, _currentUser);

        var act = () => handler.Handle(new CreateKnowledgeBaseCommand("KB", null, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}

public sealed class CreateKnowledgeBaseCommandValidatorTests
{
    private readonly CreateKnowledgeBaseCommandValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldHaveError_WhenNameIsBlank(string blankName)
    {
        var result = await _validator.ValidateAsync(new CreateKnowledgeBaseCommand(blankName, null, null, null, null, null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateKnowledgeBaseCommand.Name));
    }

    [Fact]
    public async Task ShouldNotHaveError_WhenNameIsProvided()
    {
        var result = await _validator.ValidateAsync(new CreateKnowledgeBaseCommand("BIM Standards", null, null, null, null, null));
        result.IsValid.Should().BeTrue();
    }
}
