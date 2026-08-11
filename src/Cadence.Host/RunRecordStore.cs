using System.Text.Json;

namespace Cadence.Host;

internal sealed class RunRecordStore(string path) : ICadenceRecordSink, IPipelinePersistenceObserver
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
                x.AcceptedCheckpointIds.Contains(acceptedCallId, StringComparer.Ordinal)
                    ? x
                    : x with
                    {
                        AcceptedCheckpointIds = Add(x.AcceptedCheckpointIds, acceptedCallId),
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
                x.AcceptedOutcomeLedgerIds.Contains(acceptedCallId, StringComparer.Ordinal)
                    ? x
                    : x with
                    {
                        AcceptedOutcomeLedgerIds = Add(x.AcceptedOutcomeLedgerIds, acceptedCallId),
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
            case PipelineStructuredOutputAccepted
            {
                StepId: CadenceIds.Planner,
                Payload: { } payload
            }:
                var plannerDecision = Deserialize<PlannerDecision>(payload);
                await UpdateAsync(
                    x =>
                        x with
                        {
                            PlannerDecisions = Add(x.PlannerDecisions, plannerDecision),
                            ActivePlannerConstraints = plannerDecision.Decision
                                is PlannerDecisionValue.Proceed
                                    or PlannerDecisionValue.ProceedWithConstraints
                                ? plannerDecision.Constraints
                                : x.ActivePlannerConstraints,
                        },
                    cancellationToken
                );
                break;
            case PipelineStructuredOutputAccepted
            {
                StepId: CadenceIds.Reviewer,
                Payload: { } payload
            }:
                var review = Deserialize<ReviewDecision>(payload);
                await UpdateAsync(
                    x => x with { Reviews = Add(x.Reviews, review) },
                    cancellationToken
                );
                break;
            case PipelineCapabilityAccepted
            {
                CapabilityName: "submit_report",
                Payload: { } payload
            }:
                await UpdateAsync(
                    x => x with { Report = Deserialize<SubmitReportRequest>(payload) },
                    cancellationToken
                );
                break;
        }
    }

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

    private T Deserialize<T>(JsonElement payload) =>
        payload.Deserialize<T>(_json)
        ?? throw new InvalidOperationException($"Accepted {typeof(T).Name} payload is invalid.");

    private static IReadOnlyList<T> Add<T>(IReadOnlyList<T> values, T value) => [.. values, value];

    private sealed record RunRecord
    {
        public OutcomeProgressDocument? Outcomes { get; init; }
        public SubmitReportRequest? Report { get; init; }
        public IReadOnlyList<ProgressCheckpointRecord> Checkpoints { get; init; } = [];
        public IReadOnlyList<string> AcceptedCheckpointIds { get; init; } = [];
        public IReadOnlyList<string> AcceptedOutcomeLedgerIds { get; init; } = [];
        public IReadOnlyList<PlannerDecision> PlannerDecisions { get; init; } = [];
        public IReadOnlyList<string> ActivePlannerConstraints { get; init; } = [];
        public IReadOnlyList<ReviewDecision> Reviews { get; init; } = [];
        public IReadOnlyList<VerificationResultRecord> VerificationResults { get; init; } = [];
        public IReadOnlyList<string> AcceptedVerificationIds { get; init; } = [];
        public IReadOnlyList<HumanAnswerRecord> HumanAnswers { get; init; } = [];
        public PublicationCandidateDocument? PublicationCandidate { get; init; }
        public IReadOnlyList<string> AcceptedCandidateIds { get; init; } = [];
        public IReadOnlyList<PublicationResultRecord> PublicationResults { get; init; } = [];
    }
}
