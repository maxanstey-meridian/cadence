using Microsoft.Extensions.AI;
using Tandem.Advanced;

namespace Cadence;

internal sealed class CadenceAgentFactory(
    Func<string, IChatClient> chatClients,
    Func<string, CadenceAgentProfile> profileResolver,
    AgentWorkspace<CadenceState> executorWorkspace,
    AgentWorkspace<CadenceState> reviewerWorkspace,
    IReadOnlyList<AgentSkill> skills
)
{
    internal AgentDefinition<CadenceState> Create(
        string participantId,
        string profileName,
        string instructions,
        Func<AgentBuilder<CadenceState>, AgentBuilder<CadenceState>> configure
    )
    {
        var profile = profileResolver(profileName);
        var builder = AgentProfiles
            .Create<CadenceState>(
                participantId,
                profileName,
                instructions,
                chatClients(profileName),
                chatClients
            )
            .UseHarness(CadenceHarnessInstructions.Value);

        foreach (var skill in skills)
        {
            builder.WithSkill(skill);
        }

        return configure(builder).Build();
    }

    internal CadenceAgentProfile ResolveProfile(string profileName) => profileResolver(profileName);

    internal AgentWorkspace<CadenceState> ExecutorWorkspace => executorWorkspace;
    internal AgentWorkspace<CadenceState> ReviewerWorkspace => reviewerWorkspace;
}
