# Cadence

An agentic coding pipeline built on top of
[Tandem](https://github.com/maxanstey-meridian/tandem).

Cadence turns one delivery packet into one reviewed candidate commit. The configured
pipeline is the lifecycle: prepare an isolated workspace, let an Executor implement
with Planner authority, verify the captured candidate deterministically, and require
Reviewer acceptance before anything becomes publishable.

```csharp
using Cadence;
using Tandem;

// CadenceParticipantsFactory creates the real agents, stages, interactions, and outputs.
var participants = participantsFactory.Create();
var cadence = Pipeline
    .Start(
        at: participants.PrepareWorkspace,
        name: "cadence",
        description: "The Executor implements with Planner guidance and Reviewer approval."
    )
    .Persist()
    .Route(participants.PrepareWorkspace.Success, participants.Executor, "workspace prepared")
    .Route(participants.PrepareWorkspace.Failed, participants.FailRun, "workspace failed")
    .Route(
        participants.Executor.Success,
        state => state.ExecutorTransition is ExecutorTransition.PlannerRequested,
        participants.Planner,
        "planner requested"
    )
    .Route(
        participants.Executor.Success,
        state => state.ExecutorTransition is ExecutorTransition.OutcomeLedgerUpdated,
        participants.Executor,
        "outcome ledger updated"
    )
    .Route(
        participants.Executor.Success,
        state => state.ExecutorTransition is ExecutorTransition.ReportSubmitted,
        participants.CaptureCandidate,
        "report submitted"
    )
    .Route(
        participants.Executor.Success,
        state => state.ExecutorTransition is ExecutorTransition.CheckpointWritten,
        participants.Planner,
        "checkpoint written"
    )
    .Route(participants.Executor.Failed, participants.FailRun, "agent failed")
    .Route(participants.Planner.Success, IsPlannerProceed, participants.Executor, "proceed")
    .Route(participants.Planner.Success, IsPlannerRevision, participants.Executor, "revise approach")
    .Route(participants.Planner.Success, IsPlannerNeedsHuman, participants.PlannerHumanInput, "needs human")
    .Route(participants.Planner.Success, IsPlannerStop, participants.FailRun, "stop")
    // ...planner recovery, verification, review, Human interaction, and terminal routes.
    .Build(participants.CompleteRun, participants.FailRun);
```

The complete production graph lives in
[`DeliveryComposition.cs`](src/Cadence/DeliveryComposition.cs).

## How it works

1. Cadence checks out the packet's base in a separate workspace.
2. The Executor inspects the repository and proposes an approach.
3. The Planner approves it, adds constraints, asks for a revision, or escalates to you.
4. Only then can the Executor edit the workspace and produce a candidate commit.
5. Cadence runs every verification command from the packet.
6. The Reviewer independently inspects the exact verified change.
7. Failed checks or review findings go back to the Executor for another pass.
8. An accepted candidate is recorded by its exact commit SHA, ready to publish when you choose.

The source working tree is never used as the run workspace. Cadence does not merge on
your behalf, and it never publishes a different commit from the one the Reviewer
accepted.

## Who does what

| Role | Responsibility | Limits |
| --- | --- | --- |
| **Executor** | Understands the task, implements the change, and reports progress against each outcome | Cannot edit until the Planner approves the current approach |
| **Planner** | Challenges the approach, adds constraints, redirects weak plans, and asks you when judgement is required | Can inspect the repository but cannot edit it or run packet commands |
| **Reviewer** | Reviews the exact candidate and checks every requested outcome | Can request repairs but cannot alter the candidate |

Verification commands come from the packet and run once, deterministically, after candidate capture. No role receives an unrestricted shell.

## Before Cadence accepts a change

A candidate is accepted only when:

- every packet outcome has been addressed;
- every configured verification command passes against the candidate;
- the Reviewer has inspected the complete change from the pinned base;
- the accepted commit is still the candidate that was verified and reviewed.

Cadence checks these conditions itself rather than trusting a model to say it performed
them. If implementation evidence changes, earlier verification and review no longer
count. If a check fails, the actual failure is sent back for repair.

## When Cadence asks you

The Planner can ask for product, business, security, or other Human judgement. The
Reviewer can also ask you to decide a finding or whether to continue after the repair
limit. Cadence waits for the answer in the terminal and resumes the same live run.

Cadence is a single-run pipeline, not a queue or background service. It does not manage
campaigns or merge changes automatically. Its only process-recovery surface is explicit
executor-phase resume from the retained workspace and accepted Tandem ledger state.

## Packet

```markdown
---
title: Implement example
repository: /absolute/path/to/repository
base: main
outcomes:
  - id: example
    description: Deliver the requested behavior
commands:
  - task format
acceptance:
  - id: concrete-proof
    outcome: example
    requirement: A focused test proves the requested behavior for the concrete scenario
verification:
  - dotnet test
constraints:
  - Preserve the public API
---

Inspect the relevant implementation and tests before choosing the change surface.
```

The frontmatter is the delivery contract. `title`, `repository`, and `base` must be
nonblank. `outcomes` is an ordered, nonempty list with unique nonblank IDs and nonblank
descriptions. `acceptance` is an ordered, nonempty list of unique nonblank IDs, declared
outcome references, and nonblank concrete proof requirements; every outcome needs at least
one criterion. `commands` is an optional ordered list of exact nonblank repository commands
available only to Executor after Planner authorizes mutation. `verification` is an ordered,
nonempty list of exact nonblank read-only commands; duplicates remain separate checks.
`constraints` is optional and its text is preserved exactly.

Outcomes describe delivered capability. Acceptance criteria state independently reviewable
behavioral or test proof obligations. Constraints bound every valid implementation.
Verification entries are exact deterministic commands Cadence runs. Keep the Markdown body
to bounded architecture, ownership, inspection seams, and non-obvious rationale; requirements
that affect acceptance must appear in structured `acceptance`, not only in body prose. Quote commands and constraints that YAML would otherwise interpret as values,
including `true`, `false`, `null`, and numbers. Unknown fields and duplicate YAML keys
are rejected.

`repository` may be absolute or relative to the packet file. Cadence requires that the
resolved directory exists before loading configuration or creating a run; workspace
preparation later proves that `base` resolves in Git. The Markdown body is trimmed at
its outer edges, normalized to `\n`, and supplied unchanged as implementation context
to Executor, Planner, and Reviewer.

Packet `commands` support repository-owned generation and other implementation workflows.
They may modify the isolated workspace, are not rerun as verification, and do not grant an
unrestricted shell. Prefer checked-in task or package scripts; otherwise declare the exact
framework command and arguments required by the delivery.

Packets whose outcomes already hold may produce an allow-empty candidate commit; they
still pass complete verification and Reviewer inspection. A packet-authoring Agent Skill
and production-validated example live at [`skills/packet-authoring`](skills/packet-authoring)
and [`examples/packet.md`](examples/packet.md).

## Configure

Cadence reads `$CADENCE_HOME/config.json`, defaulting to `~/.cadence/config.json`:

```json
{
  "gitTimeoutSeconds": 120,
  "reviewerDoctrineFile": "reviewer-doctrine.json",
  "skillDirectories": [
    "/absolute/path/to/meridian",
    "skills/repository-specific"
  ],
  "providers": {
    "local": {
      "baseUrl": "http://localhost:11434/v1",
      "apiKeyEnvironmentVariable": null,
      "wireApi": "completions"
    }
  },
  "profiles": {
    "executor": {
      "provider": "local",
      "model": "executor-model",
      "contextWindowTokens": 200000,
      "maxOutputTokens": 32000,
      "checkpointAtPercent": 80,
      "disableCompaction": false
    },
    "planner": {
      "provider": "local",
      "model": "planner-model",
      "contextWindowTokens": 200000,
      "maxOutputTokens": 32000,
      "checkpointAtPercent": 80,
      "disableCompaction": false
    },
    "reviewer": {
      "provider": "local",
      "model": "reviewer-model",
      "contextWindowTokens": 200000,
      "maxOutputTokens": 32000,
      "checkpointAtPercent": 80,
      "disableCompaction": false
    }
  }
}
```

`disableCompaction` controls framework conversation compaction for agents using the profile's
checkpoint policy. Cadence currently applies that policy to Executor; Planner and Reviewer do not
configure in-session checkpoint compaction.

`reviewerDoctrineFile` is required. Relative paths resolve against the configuration
directory. Cadence loads the current JSON doctrine document once for the run:

```json
{
  "clauses": [
    {
      "id": "material-correctness",
      "text": "Prioritize correctness, behavioral regressions, and missing material tests."
    }
  ]
}
```

Clause IDs are nonblank, unique, case-sensitive operator-authored labels, and clause text must be
nonblank. The document has no schema version or source-byte identity. Reviewer findings describe a
concrete defect and its precise repository location directly.

`skillDirectories` is optional. Every configured directory must contain `SKILL.md`.
Executor, Planner, and Reviewer can load these shared instructions when relevant. Skills
do not grant permission to edit the workspace or run extra commands.

## Prepare Tandem

Cadence consumes `Tandem`, `Tandem.Advanced`, `Tandem.Generators`, `Tandem.Ledger`,
`Tandem.OpenAICompatible`, `Tandem.Packets`, and `Tandem.Terminal` through package references.
Until those packages are published, refresh the ignored local feed:

```sh
task prepare
```

Set `TANDEM_REPOSITORY` or `TANDEM_VERSION` to override the local repository and package
version. Cadence has no Tandem source-project reference.

## Run

Install the CLI from the local source and package feed:

```sh
task install
```

The install task replaces any existing global Cadence tool so rebuilding the same local
package version cannot leave an older binary installed.

Then run Cadence from any directory:

```sh
cadence run packet.md
```

Executor-phase runs left `Running`, `Failed`, `Interrupted`, or `Faulted` can resume from their retained workspace and
accepted state in `~/.cadence/runs/<run-id>/ledger.sqlite3`, with fresh agent sessions. Pass the run ID. Ready and cancelled runs remain terminal:

```sh
cadence resume <run-id>
```

Resume always uses the packet persisted for that run. Start a new run to change delivery intent.

Resume preserves the run ID and dirty workspace, starts a distinct execution attempt, closes
mutation authority, and routes through Planner before Executor continues. It does not replay
the interrupted model session. The retained workspace is validated against the exact accepted
base SHA. Legacy `records.json` runs are not imported or resumable.

Pass `--publish` to publish immediately after Reviewer acceptance, or publish later
with the printed run ID:

```sh
cadence publish <run-id>
```

Publication pushes exactly the Reviewer-accepted candidate SHA to an isolated
`cadence/...` branch. It does not modify the source working tree or merge.

## Development

Run the complete repository gate:

```sh
task check
```

Use `task test`, `task build`, `task format`, or `task format:check` for individual
checks. `task check` refreshes the local Tandem packages, checks formatting and
analyzers, runs the tests, builds with warnings as errors, and checks the repository's
architecture rules.

See [`PLAN.md`](PLAN.md) for the complete lifecycle contract and behavioral proof list.
