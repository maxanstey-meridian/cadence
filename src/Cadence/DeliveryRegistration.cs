using Cadence.Git;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence;

public sealed record CadenceAgentProfile(
    int ContextWindowTokens,
    int MaxOutputTokens,
    int CheckpointAtPercent,
    bool DisableCompaction = false
);

public sealed record CadenceOptions(
    Func<string, IChatClient> ChatClients,
    Func<string, CadenceAgentProfile> Profiles,
    ReviewerDoctrine ReviewerDoctrine,
    TimeSpan? GitTimeout = null,
    IReadOnlyList<AgentSkill>? Skills = null,
    TimeProvider? TimeProvider = null
);

public static class CadenceRegistration
{
    public static IServiceCollection AddCadence(
        this IServiceCollection services,
        CadenceOptions options
    )
    {
        services.TryAddSingleton(_ => new GitProcess(timeout: options.GitTimeout));
        services.TryAddSingleton(options.TimeProvider ?? TimeProvider.System);
        services.AddSingleton<DirtyWorkCheckpointPolicy>();
        services.AddSingleton(sp =>
            CadenceCapabilities.Create(
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<DirtyWorkCheckpointPolicy>()
            )
        );
        services.AddSingleton<WorkspacePreparation>();
        var skills = (options.Skills ?? []).ToArray();
        services.AddSingleton<CadenceParticipantsFactory>(sp =>
        {
            var capabilities = sp.GetRequiredService<CadenceCapabilitySet>();
            return new CadenceParticipantsFactory(
                options.ChatClients,
                options.Profiles,
                options.ReviewerDoctrine,
                skills,
                sp.GetRequiredService<WorkspacePreparation>(),
                sp.GetRequiredService<GitProcess>(),
                sp.GetRequiredService<DirtyWorkCheckpointPolicy>(),
                capabilities.AskPlanner,
                capabilities.UpdateOutcomes,
                capabilities.SubmitReport,
                capabilities.WriteCheckpoint,
                capabilities.ResetContext
            );
        });
        services.AddSingleton<CadenceComposition>();
        return services;
    }
}
