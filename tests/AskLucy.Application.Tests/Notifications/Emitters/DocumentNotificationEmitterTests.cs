using AskLucy.Application.Notifications.Legacy;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Tests.Notifications.Emitters;

/// <summary>
/// T077 (specs/067) — every <see cref="DocumentNotificationEventType"/> maps to its <c>document.*</c>
/// catalog key. The OCR-vs-processing-failed emission split lives in <c>ProcessingNotifierTests</c>
/// (Web.Tests), since that behavior is in <c>ProcessingNotifier</c> (Infrastructure), which this
/// project doesn't reference.
/// </summary>
public sealed class DocumentNotificationEmitterTests
{
    [Theory]
    [InlineData(DocumentNotificationEventType.UploadCompleted, NotificationTypeKeys.DocumentUploadCompleted)]
    [InlineData(DocumentNotificationEventType.ProcessingCompleted, NotificationTypeKeys.DocumentProcessingCompleted)]
    [InlineData(DocumentNotificationEventType.ProcessingFailed, NotificationTypeKeys.DocumentProcessingFailed)]
    [InlineData(DocumentNotificationEventType.OcrFailed, NotificationTypeKeys.DocumentOcrFailed)]
    [InlineData(DocumentNotificationEventType.VersionCreated, NotificationTypeKeys.DocumentVersionCreated)]
    [InlineData(DocumentNotificationEventType.StorageLimitReached, NotificationTypeKeys.DocumentStorageLimitReached)]
    public void ToCatalogKey_ShouldMapEveryEventType_ToItsDocumentCatalogKey(DocumentNotificationEventType eventType, string expectedKey) =>
        Assert.Equal(expectedKey, LegacyNotificationTypeMap.ToCatalogKey(eventType));
}
