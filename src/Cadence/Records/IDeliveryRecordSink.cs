namespace Cadence;

public interface ICadenceRecordSink
{
    public ValueTask<CadenceLedgerContext> ReadContextAsync(
        CadenceLedgerRole role,
        CancellationToken cancellationToken
    );

    public ValueTask AcceptCheckpointAsync(
        string acceptedCallId,
        ProgressCheckpointRecord checkpoint,
        CancellationToken cancellationToken
    );

    public ValueTask AcceptOutcomeLedgerAsync(
        string acceptedCallId,
        IReadOnlyList<OutcomeLedgerEntry> outcomes,
        CancellationToken cancellationToken
    );

    public ValueTask AcceptPublicationCandidateAsync(
        string acceptedCandidateId,
        PublicationCandidateDocument candidate,
        CancellationToken cancellationToken
    );

    public ValueTask<PublicationCandidateDocument?> ReadPublicationCandidateAsync(
        CancellationToken cancellationToken
    );

    public ValueTask AcceptPublicationResultAsync(
        PublicationResultRecord result,
        CancellationToken cancellationToken
    );

    public ValueTask AcceptVerificationResultAsync(
        string acceptedResultId,
        string candidateSha,
        VerificationResult result,
        CancellationToken cancellationToken
    );
}
