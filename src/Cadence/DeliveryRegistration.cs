using Cadence.Git;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence;

public sealed record CadenceAgentProfile(
    int ContextWindowTokens,
    int MaxOutputTokens,
    int CheckpointAtPercent
);

public sealed record CadenceOptions(
    Func<string, IChatClient> ChatClients,
    Func<string, CadenceAgentProfile> Profiles,
    ICadenceRecordSink Records,
    ReviewerDoctrine ReviewerDoctrine,
    TimeProvider? TimeProvider = null,
    TimeSpan? GitTimeout = null
);

public static class CadenceRegistration
{
    public static IServiceCollection AddCadence(
        this IServiceCollection services,
        CadenceOptions options
    )
    {
        services.AddSingleton(options.Records);
        services.AddSingleton(options.TimeProvider ?? TimeProvider.System);
        services.TryAddSingleton(_ => new GitProcess(timeout: options.GitTimeout));
        services.AddSingleton<DirtyWorkCheckpointPolicy>();
        services.AddSingleton<CheckpointAcceptance>();
        services.AddSingleton(sp =>
            CadenceCapabilities.Create(
                sp.GetRequiredService<CheckpointAcceptance>(),
                options.Records,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<DirtyWorkCheckpointPolicy>()
            )
        );
        services.AddSingleton<WorkspacePreparation>();
        services.AddSingleton<CadenceParticipantsFactory>(sp =>
        {
            var capabilities = sp.GetRequiredService<CadenceCapabilitySet>();
            return new CadenceParticipantsFactory(
                options.ChatClients,
                options.Profiles,
                options.Records,
                options.ReviewerDoctrine,
                sp.GetRequiredService<WorkspacePreparation>(),
                sp.GetRequiredService<GitProcess>(),
                sp.GetRequiredService<DirtyWorkCheckpointPolicy>(),
                capabilities.AskPlanner,
                capabilities.UpdateOutcomes,
                capabilities.SubmitReport,
                capabilities.WriteCheckpoint
            );
        });
        services.AddSingleton<CadenceComposition>();
        return services;
    }
}
