using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.UpdateMemoryPreferences;

public sealed class UpdateMemoryPreferencesCommandHandler(
    IMemoryPreferenceRepository preferenceRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateMemoryPreferencesCommand>
{
    public async Task Handle(UpdateMemoryPreferencesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (request.MemoryEnabled is not null)
        {
            var preference = await preferenceRepository.GetByUserIdAsync(userId, cancellationToken);
            if (preference is null)
            {
                preference = MemoryPreference.CreateDefault(userId, userId);
                preferenceRepository.Add(preference);
            }

            preference.SetMemoryEnabled(request.MemoryEnabled.Value, userId);
        }

        foreach (var update in request.Categories)
        {
            var categoryPreference = await preferenceRepository.GetCategoryPreferenceAsync(userId, update.Category, cancellationToken);
            if (categoryPreference is null)
            {
                categoryPreference = MemoryCategoryPreference.CreateDefault(userId, update.Category, userId);
                preferenceRepository.AddCategoryPreference(categoryPreference);
            }

            categoryPreference.Update(update.ApprovalMode, update.IsEnabled, userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
