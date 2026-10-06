using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetSystemAnnouncements;

/// <summary>contracts/admin-notifications-api.md GET /notifications/announcements: newest first.</summary>
public sealed record GetSystemAnnouncementsQuery(string? Cursor = null, int Limit = 25) : IRequest<AdminPage<AdminAnnouncementDto>>
{
    public const int MaxLimit = 100;
}
