using System.Text.Json;

namespace Cadence.Host;

internal sealed class RunRecordStore(string path, Guid? executionAttemptId = null)
    : ICadenceRecordSink,
        IPipelinePersistenceObserver
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async ValueTask InitializeAsync(Packet packet, CancellationToken cancellationToken) =>
        await UpdateAsync(
            record =>
                record with
                {
                    Packet = packet,
                    Outcomes = new OutcomeProgressDocument(
                        "packet",
                        packet
                            .Outcomes.Select(x => new OutcomeProgress(
                                x.Id,
                                x.Description,
                                OutcomeStatus.NotStarted,
                                [],
                                "No implementation has been recorded.",
                                "Inspect the repository and determine the smallest implementation step."
                            ))
                            .ToArray()
                    ),
                },
            cancellationToken
        );

    public async ValueTask<RecoveryRecord> ReadRecoveryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Run record not found: {path}");
        }
        var record = await ReadAsync(cancellationToken);
        return new RecoveryRecord(
            record.Packet,
            record.PinnedBaseSha,
            record.Outcomes,
            record.Checkpoints.LastOrDefault(),
            record.PlannerDecisions.LastOrDefault(),
            record.ActivePlannerConstraints,
            record.PlannerFailureCount,
            record.VerificationResults,
            record.PublicationCandidate
        );
    }

    public async ValueTask AcceptWorkspaceAsync(
        WorkspacePreparationRecord workspace,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            record => record with { PinnedBaseSha = workspace.PinnedBaseSha },
            cancellationToken
        );

    public async ValueTask AcceptPlannerFailureCountAsync(
        int failureCount,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            record => record with { PlannerFailureCount = failureCount },
            cancellationToken
        );

    public async ValueTask<CadenceLedgerContext> ReadContextAsync(
        CadenceLedgerRole role,
        CancellationToken cancellationToken
    )
    {
        var record = await ReadAsync(cancellationToken);
        return new CadenceLedgerContext(
            record.Outcomes,
            role == CadenceLedgerRole.Reviewer ? record.Report : null,
            role == CadenceLedgerRole.Executor ? record.Checkpoints.LastOrDefault() : null,
            record.ActivePlannerConstraints,
            record.PlannerDecisions.TakeLast(5).ToArray(),
            role == CadenceLedgerRole.Reviewer ? record.Reviews.TakeLast(5).ToArray() : [],
            role is CadenceLedgerRole.Executor or CadenceLedgerRole.Reviewer
                ? record.VerificationResults.TakeLast(5).Select(x => x.Result).ToArray()
                : [],
            role == CadenceLedgerRole.Reviewer ? record.HumanAnswers.TakeLast(5).ToArray() : []
        );
    }

    public async ValueTask AcceptCheckpointAsync(
        string acceptedCallId,
        ProgressCheckpointRecord checkpoint,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x.AcceptedCheckpointIds.Contains(
                    AcceptanceId(acceptedCallId),
                    StringComparer.Ordinal
                )
                    ? x
                    : x with
                    {
                        AcceptedCheckpointIds = Add(
                            x.AcceptedCheckpointIds,
                            AcceptanceId(acceptedCallId)
                        ),
                        Checkpoints = Add(x.Checkpoints, checkpoint),
                    },
            cancellationToken
        );

    public async ValueTask AcceptOutcomeLedgerAsync(
        string acceptedCallId,
        IReadOnlyList<OutcomeLedgerEntry> outcomes,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x.AcceptedOutcomeLedgerIds.Contains(
                    AcceptanceId(acceptedCallId),
                    StringComparer.Ordinal
                )
                    ? x
                    : x with
                    {
                        AcceptedOutcomeLedgerIds = Add(
                            x.AcceptedOutcomeLedgerIds,
                            AcceptanceId(acceptedCallId)
                        ),
                        Outcomes = new OutcomeProgressDocument(
                            acceptedCallId,
                            outcomes
                                .Select(outcome => new OutcomeProgress(
                                    outcome.OutcomeId,
                                    outcome.Description,
                                    outcome.Status,
                                    outcome.Evidence,
                                    outcome.ImplementationState,
                                    outcome.NextAction
                                ))
                                .ToArray()
                        ),
                    },
            cancellationToken
        );

    public async ValueTask AcceptPublicationCandidateAsync(
        string acceptedCandidateId,
        PublicationCandidateDocument candidate,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x.AcceptedCandidateIds.Contains(acceptedCandidateId, StringComparer.Ordinal)
                    ? x
                    : x with
                    {
                        AcceptedCandidateIds = Add(x.AcceptedCandidateIds, acceptedCandidateId),
                        PublicationCandidate = candidate,
                    },
            cancellationToken
        );

    public async ValueTask<PublicationCandidateDocument?> ReadPublicationCandidateAsync(
        CancellationToken cancellationToken
    ) => (await ReadAsync(cancellationToken)).PublicationCandidate;

    public async ValueTask AcceptPublicationResultAsync(
        PublicationResultRecord result,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x.PublicationResults.Contains(result)
                    ? x
                    : x with
                    {
                        PublicationResults = Add(x.PublicationResults, result),
                    },
            cancellationToken
        );

    public async ValueTask AcceptVerificationResultAsync(
        string acceptedResultId,
        string candidateSha,
        VerificationResult result,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x.AcceptedVerificationIds.Contains(acceptedResultId, StringComparer.Ordinal)
                    ? x
                    : x with
                    {
                        AcceptedVerificationIds = Add(x.AcceptedVerificationIds, acceptedResultId),
                        VerificationResults = Add(
                            x.VerificationResults,
                            new VerificationResultRecord(candidateSha, result)
                        ),
                    },
            cancellationToken
        );

    public async ValueTask RecordPlannerHumanAnswerAsync(
        PipelineInteractionContext<PlannerHumanQuestion, PlannerHumanAnswer> context,
        PlannerHumanAnswer answer,
        CancellationToken cancellationToken
    ) =>
        await RecordHumanAnswerAsync(
            context.RequestId,
            context.InteractionId,
            context.Request.Question,
            answer.Text,
            cancellationToken
        );

    public async ValueTask RecordReviewerHumanAnswerAsync(
        PipelineInteractionContext<ReviewerHumanRequest, ReviewerHumanAnswer> context,
        ReviewerHumanAnswer answer,
        CancellationToken cancellationToken
    ) =>
        await RecordHumanAnswerAsync(
            context.RequestId,
            context.InteractionId,
            context.Request.Question,
            answer switch
            {
                ReviewerHumanAnswer.HumanDecision decision => decision.Text,
                ReviewerHumanAnswer.ContinueRepairs => nameof(ReviewerHumanAnswer.ContinueRepairs),
                ReviewerHumanAnswer.Stop => nameof(ReviewerHumanAnswer.Stop),
                _ => throw new ArgumentOutOfRangeException(nameof(answer)),
            },
            cancellationToken
        );

    private async ValueTask RecordHumanAnswerAsync(
        string requestId,
        string interactionId,
        string question,
        string answer,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x with
                {
                    HumanAnswers = Add(
                        x.HumanAnswers,
                        new HumanAnswerRecord(requestId, interactionId, question, answer)
                    ),
                },
            cancellationToken
        );

    public async ValueTask ObserveAsync(
        PipelineObservation observation,
        CancellationToken cancellationToken
    )
    {
        switch (observation)
        {
            case OutputAccepted<PlannerDecision>
            {
                StepId: CadenceIds.Planner,
                AcceptedValue: var plannerDecision
            }:
                await RecordPlannerDecisionAsync(plannerDecision, cancellationToken);
                break;
            case OutputAccepted<ReviewDecision>
            {
                StepId: CadenceIds.Reviewer,
                AcceptedValue: var review
            }:
                await UpdateAsync(
                    x => x with { Reviews = Add(x.Reviews, review) },
                    cancellationToken
                );
                break;
            case CapabilityAccepted<SubmitReportRequest>
            {
                CapabilityName: "submit_report",
                AcceptedValue: var report
            }:
                await UpdateAsync(x => x with { Report = report }, cancellationToken);
                break;
        }
    }

    private async ValueTask RecordPlannerDecisionAsync(
        PlannerDecision plannerDecision,
        CancellationToken cancellationToken
    ) =>
        await UpdateAsync(
            x =>
                x with
                {
                    PlannerDecisions = Add(x.PlannerDecisions, plannerDecision),
                    PlannerFailureCount = 0,
                    ActivePlannerConstraints = plannerDecision.Decision
                        is PlannerDecisionValue.Proceed
                            or PlannerDecisionValue.ProceedWithConstraints
                        ? plannerDecision.Constraints
                        : x.ActivePlannerConstraints,
                },
            cancellationToken
        );

    public async ValueTask<PublicationResultRecord?> ReadLatestPublicationAsync(
        CancellationToken cancellationToken
    ) => (await ReadAsync(cancellationToken)).PublicationResults.LastOrDefault();

    private async ValueTask<RunRecord> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask UpdateAsync(
        Func<RunRecord, RunRecord> update,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = update(await ReadUnsafeAsync(cancellationToken));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(record, _json),
                cancellationToken
            );
            File.Move(temporary, path, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RunRecord> ReadUnsafeAsync(CancellationToken cancellationToken) =>
        !File.Exists(path)
            ? new RunRecord()
            : JsonSerializer.Deserialize<RunRecord>(
                await File.ReadAllTextAsync(path, cancellationToken),
                _json
            ) ?? throw new InvalidOperationException($"Run record is empty: {path}");

    private static IReadOnlyList<T> Add<T>(IReadOnlyList<T> values, T value) => [.. values, value];

    private string AcceptanceId(string acceptedId) =>
        executionAttemptId is { } attempt ? $"{attempt:N}:{acceptedId}" : acceptedId;

    private sealed record RunRecord
    {
        public Packet? Packet { get; init; }
        public string? PinnedBaseSha { get; init; }
        public OutcomeProgressDocument? Outcomes { get; init; }
        public SubmitReportRequest? Report { get; init; }
        public IReadOnlyList<ProgressCheckpointRecord> Checkpoints { get; init; } = [];
        public IReadOnlyList<string> AcceptedCheckpointIds { get; init; } = [];
        public IReadOnlyList<string> AcceptedOutcomeLedgerIds { get; init; } = [];
        public IReadOnlyList<PlannerDecision> PlannerDecisions { get; init; } = [];
        public IReadOnlyList<string> ActivePlannerConstraints { get; init; } = [];
        public int PlannerFailureCount { get; init; }
        public IReadOnlyList<ReviewDecision> Reviews { get; init; } = [];
        public IReadOnlyList<VerificationResultRecord> VerificationResults { get; init; } = [];
        public IReadOnlyList<string> AcceptedVerificationIds { get; init; } = [];
        public IReadOnlyList<HumanAnswerRecord> HumanAnswers { get; init; } = [];
        public PublicationCandidateDocument? PublicationCandidate { get; init; }
        public IReadOnlyList<string> AcceptedCandidateIds { get; init; } = [];
        public IReadOnlyList<PublicationResultRecord> PublicationResults { get; init; } = [];
    }
}
