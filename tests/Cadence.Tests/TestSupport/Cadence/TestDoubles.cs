namespace Cadence.Tests;

internal sealed class FakeTimeProvider(DateTimeOffset value) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = value;

    public override DateTimeOffset GetUtcNow() => Value;
}

internal sealed class FakeRecordSink : ICadenceRecordSink
{
    public PublicationCandidateDocument? Candidate { get; private set; }
    public List<ProgressCheckpointRecord> Checkpoints { get; } = [];
    public List<IReadOnlyList<OutcomeLedgerEntry>> OutcomeLedgers { get; } = [];
    public List<PublicationResultRecord> PublicationResults { get; } = [];
    public List<VerificationResultRecord> VerificationResults { get; } = [];
    public CadenceLedgerContext? Context { get; init; }

    public ValueTask<CadenceLedgerContext> ReadContextAsync(
        CadenceLedgerRole role,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(
            Context ?? new CadenceLedgerContext(null, null, null, [], [], [], [], [])
        );

    public ValueTask AcceptCheckpointAsync(
        string acceptedCallId,
        ProgressCheckpointRecord checkpoint,
        CancellationToken cancellationToken
    )
    {
        Checkpoints.Add(checkpoint);
        return ValueTask.CompletedTask;
    }

    public ValueTask AcceptOutcomeLedgerAsync(
        string acceptedCallId,
        IReadOnlyList<OutcomeLedgerEntry> outcomes,
        CancellationToken cancellationToken
    )
    {
        OutcomeLedgers.Add(outcomes);
        return ValueTask.CompletedTask;
    }

    public ValueTask AcceptPublicationCandidateAsync(
        string acceptedCandidateId,
        PublicationCandidateDocument candidate,
        CancellationToken cancellationToken
    )
    {
        Candidate = candidate;
        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicationCandidateDocument?> ReadPublicationCandidateAsync(
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(Candidate);

    public ValueTask AcceptPublicationResultAsync(
        PublicationResultRecord result,
        CancellationToken cancellationToken
    )
    {
        PublicationResults.Add(result);
        return ValueTask.CompletedTask;
    }

    public ValueTask AcceptVerificationResultAsync(
        string acceptedResultId,
        string candidateSha,
        VerificationResult result,
        CancellationToken cancellationToken
    )
    {
        VerificationResults.Add(new VerificationResultRecord(candidateSha, result));
        return ValueTask.CompletedTask;
    }
}
