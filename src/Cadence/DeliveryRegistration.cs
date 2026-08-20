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
    IReadOnlyList<AgentSkill>? Skills = null
);

public static class CadenceRegistration
{
    public static IServiceCollection AddCadence(
        this IServiceCollection services,
        CadenceOptions options
    )
    {
        services.TryAddSingleton(_ => new GitProcess(timeout: options.GitTimeout));
        services.AddSingleton(_ => CadenceCapabilities.Create());
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
                capabilities.AskPlanner,
                capabilities.SubmitReport,
                capabilities.WriteCheckpoint
            );
        });
        services.AddSingleton<CadenceComposition>();
        return services;
    }
}
