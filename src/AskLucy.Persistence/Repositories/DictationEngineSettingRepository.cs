using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DictationEngineSettingRepository(AskLucyDbContext dbContext) : IDictationEngineSettingRepository
{
    public async Task<DictationEngineSetting> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var id = DictationEngineSetting.SingletonId;
        var setting = await dbContext.DictationEngineSettings.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (setting is not null)
        {
            return setting;
        }

        // The default row is inserted on its own rather than through Add + SaveChanges, which would
        // also flush whatever else the request is tracking (the switch-off observer runs in the
        // middle of UpdateAiProvider, before that handler's own save).
        var defaults = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (SELECT 1 FROM [DictationEngineSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id})
                INSERT INTO [DictationEngineSettings] ([Id], [PrimaryEngine], [PushToTalkEngine], [State], [CreatedAtUtc], [CreatedBy])
                VALUES ({id}, {defaults.PrimaryEngine.ToString()}, {defaults.PushToTalkEngine.ToString()}, {defaults.State.ToString()}, {defaults.CreatedAtUtc}, {defaults.CreatedBy})
                """,
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent first read inserted the row between our check and insert; read theirs.
        }

        return await dbContext.DictationEngineSettings.SingleAsync(s => s.Id == id, cancellationToken);
    }

    public Task<Guid?> GetSelectedLocalWhisperModelIdAsync(CancellationToken cancellationToken = default) =>
        dbContext.DictationEngineSettings.AsNoTracking()
            .Where(s => s.Id == DictationEngineSetting.SingletonId)
            .Select(s => s.LocalWhisperModelId)
            .FirstOrDefaultAsync(cancellationToken);
}
