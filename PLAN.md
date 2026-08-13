# Cadence

Cadence is a first-class coding pipeline application built with Tandem. It replaces
Lathe's custom harness with a small, typed, single-run pipeline while preserving
the safeguards that make autonomous implementation trustworthy.

## Pipeline

```text
Packet
  -> Prepare isolated workspace
  -> Executor <-> Planner
  -> Capture exact candidate
  -> Mechanical verification
  -> Reviewer
       -> accept
       -> request changes -> Executor
       -> needs human
  -> Publish accepted SHA
  -> Human inspects and merges
```

The canonical role names are **Executor**, **Planner**, and **Reviewer**. These
names are used everywhere: code, types, state, routes, prompts, runtime identities,
profiles, configuration, logs, tests, and documentation. Legacy familial aliases
are forbidden and have no compatibility or wire-format role in Cadence.

## Scope

Bring over Lathe's one-run semantics:

- Isolated workspace pinned to an exact base commit.
- Executor must inspect before editing.
- First mutation requires Planner approval.
- A materially changed plan requires renewed Planner approval.
- Planner independently inspects repository evidence before approval.
- Executor cannot own architecture, human decisions, verification, review, or git.
- Typed outcome reporting with concrete evidence.
- Pipeline-owned candidate capture and git lifecycle.
- Deterministic verification owned by the pipeline.
- Reviewer independently reviews the exact verified candidate.
- Reviewer applies a required configured doctrine identified by source and exact-byte SHA-256.
- Every packet outcome is assessed exactly once.
- Findings are grounded, specific, and actionable.
- Human-only decisions route through typed interactions.
- Checkpoint and context-rotation safeguards.
- Exact-SHA publication for manual human review and merge.

Explicitly exclude:

- Campaigns and cumulative campaign contracts.
- Generated repair or follow-up packets.
- Cross-run convergence.
- Pass lineage and contract hashes.
- Parent/child staged chains.
- Model promotion passes.
- Lathe's queue and daemon machinery.
- MCP lifecycle bridges.
- A custom imperative turn loop.
- General durable replay orchestration.
- Automatic merge.

Explicit executor-phase recovery is not general replay. `cadence resume <run-id>` reuses
the retained workspace and accepted ledger facts, starts fresh model sessions, closes
mutation authority, and routes through Planner before Executor continues. Candidate and
verification phases remain non-resumable. The stable Cadence run ID names the durable
delivery; each process attempt has distinct acceptance identity so resumed capability calls
cannot collide with earlier accepted calls.

Repairs remain inside one run:

```text
Reviewer requests changes
  -> Executor repairs
  -> capture a new candidate
  -> rerun all verification
  -> fresh Reviewer review
```

## Application State

Cadence owns strongly typed lifecycle facts:

- Packet and packet outcomes.
- Pinned base SHA and isolated workspace path.
- Current approach revision.
- Planner-approved approach revision.
- Current Planner constraints.
- Typed outcome ledger.
- Latest Executor checkpoint.
- Last accepted continuity timestamp.
- Candidate SHA.
- Verification results bound to the candidate SHA.
- Reviewer decision bound to the candidate SHA.
- Review-attempt count and limit.
- `ReviewRepairRequired`, set only by `RequestChanges` and cleared only by a
  materially changed outcome-ledger update.
- Typed human questions and answers.

Control flow belongs in the Tandem graph, not hidden inside state or an
application-level coordinator.

## Executor

Executor implements. Its prompt and policies must establish that it:

- Reads repository files before editing them.
- Treats the repository as the source of truth for code facts.
- Calls `ask_planner` with a proposed approach and inspected evidence before its
  first mutation.
- Satisfies every active Planner constraint.
- Calls Planner again when the implementation plan materially changes.
- Owns ordinary red-green iteration.
- Calls Planner after a conceptual fix fails before attempting another conceptual
  fix.
- Routes product, UX, business, security, permission, tenancy, data, migration,
  legal, and compliance decisions to the Human through Planner.
- Never owns verification, review, git lifecycle, or completion.
- Uses typed lifecycle capabilities instead of claiming progress in prose.

Executor receives these typed capabilities:

- `ask_planner`
- `update_outcomes`
- `submit_report`
- `write_checkpoint`

An accepted capability call ends the current Executor visit. It does not inherently
discard or rotate Executor's session. The pipeline routes using the resulting typed
state, and a later Executor visit resumes the retained conversation unless a policy
explicitly resets it.

In particular, `ask_planner` behaves as follows:

```text
Executor session A calls ask_planner
  -> the current Executor visit ends
  -> the pipeline routes to Planner
  -> Planner returns a typed decision
  -> the pipeline routes back to Executor
  -> Executor resumes session A with its prior conversation
```

## Planner

Planner owns engineering direction. Its prompt and policies must establish that it:

- Treats Executor's supplied evidence as pointers, not proof.
- Independently inspects material repository facts before approval.
- Audits the complete proposed approach, not only the literal question.
- Corrects false premises, incomplete surfaces, and unsafe implementation plans.
- Does not implement or mutate the workspace.
- Escalates only decisions genuinely owned by the Human.

Planner returns one typed decision:

- `Proceed`
- `ProceedWithConstraints`
- `NeedsHuman`
- `Stop`

Approval is rejected unless Planner performed repository inspection during that
consult. Each accepted Planner decision replaces the active constraint list.

## Mutation Authority

Mutation authority is revision-scoped, not sticky:

```text
Executor proposes approach revision N
  -> mutation closes
Planner approves revision N
  -> mutation opens for revision N
Executor asks about revision N+1
  -> mutation closes immediately
```

Tandem's state guard intercepts workspace mutations before execution. Reads remain
available. Cadence owns the blocked message instructing Executor to call
`ask_planner`. Before approval, prose-only responses specifically require
`ask_planner`. After approval, prose-only responses retain access to
`ask_planner`, `write_checkpoint`, and `submit_report`, and fail closed if Executor
refuses to choose a lifecycle action.

## Outcome Ledger

Executor's report uses typed claims rather than free-form outcome strings:

```text
Outcome ID
Status
Evidence
```

`submit_report` is accepted only when:

- Every packet outcome appears exactly once.
- No unknown or duplicate outcome IDs exist.
- Every outcome is complete.
- Every completed outcome has concrete evidence.
- Every exact combined packet and active Planner constraint is addressed once.
- Regression-test evidence is present where applicable.
- Any retained changed-file classification contract is satisfied.

Only an accepted report can leave Executor implementation.

## Candidate And Git

After report acceptance, a deterministic pipeline stage:

1. Stages all workspace changes.
2. Creates the candidate commit, including an allow-empty commit when the packet
   is correctly delivered without workspace changes.
3. Records its exact SHA.
4. Resets verification state.
5. Invalidates review state tied to an older candidate.

Executor never controls git. A repair always creates a new candidate SHA.

## Repository Commands

Delivery packets may declare exact repository commands used during implementation, such
as checked-in generation or migration workflows. Cadence exposes each command as a fixed,
argument-free tool only to Executor and only after Planner authorizes the current approach.
Commands may mutate the isolated workspace. They are not verification and are not exposed
to Planner or Reviewer.

## Verification

Delivery packets require at least one verification command. Commands execute in
deterministic stages and produce evidence bound to the current candidate SHA.

```text
command passes and commands remain -> next command
all commands pass                 -> Reviewer
any command fails                 -> Executor
```

Executor's claims about verification are non-authoritative. A new candidate
invalidates all prior verification results.

## Reviewer

Reviewer independently judges the exact verified candidate. Its prompt and
policies must establish that it:

- Treats Executor's report and Planner's approval as claims to verify.
- Reviews the exact candidate diff.
- Uses paginated read-only Git tools to discover and inspect the complete diff;
  Cadence does not inject the diff into the Reviewer message.
- Can inspect the entire read-only repository.
- Checks every packet outcome and active Planner constraint.
- Checks relevant existing behavior and integration seams.
- Checks regression coverage and test quality.
- Grounds blockers in repository, command, packet, or constraint evidence.
- Uses typed reproducible file-line, symbol, verification-command, packet-outcome,
  Planner-constraint, and doctrine-clause references rather than evidence prose.
- Audits requirement sanity, downstream coherence, changed tests, branches, error
  paths, mock soup, and fake integration coverage; green verification alone is
  insufficient.
- Does not block on unsupported taste.

Reviewer returns one typed decision:

- `Accept`
- `RequestChanges`
- `NeedsHuman`

Mechanical rules:

- Every non-Human decision requires a completed read-only `git_changed_files` call
  with the exact pinned base and candidate SHAs, followed by a completed repository-wide
  `git_diff` for the same range. Both must begin at the first page.
  The latest invocation of each Git tool is authoritative, and the latest manifest
  must precede the latest repository-wide diff.
- Every generated `run_verification_N` command must be attempted after Git grounding
  in packet order. The latest invocation for each command is current: later success
  may replace failure and later failure invalidates success.
- `Accept` requires each current attempt to be completed with process exit code zero
  with no timeout or truncation. `RequestChanges` may stop at the first complete runtime
  `Failed` command only when a Critical/High finding exactly reproduces that packet
  command and runtime exit code, stdout, and stderr. Blocked and faulted attempts never
  qualify.
- Every `VerificationCommand` evidence reference in outcomes, constraint assessments,
  and findings must exactly match deterministic verification results or a complete
  runtime invocation of the corresponding declared fixed command.
- Every packet outcome is assessed exactly once.
- `Accept` requires every outcome to be delivered.
- Every finding is grounded in an exact doctrine clause and reproducible defect evidence.
- `Accept` rejects Critical and High findings but preserves Medium and Low findings.
- `RequestChanges` requires at least one Critical or High finding.
- The decision is bound to the current candidate SHA.
- Human questions are restricted to Human-owned decisions.

## Bounded Repairs

Cadence tracks `RequestChanges` attempts in application state:

```text
RequestChanges and attempts remain -> Executor
RequestChanges at the cap          -> Human
```

Each repair must pass candidate capture and the complete verification sequence
before Reviewer reviews it. No repair packet or campaign is created.
`Accept` and `NeedsHuman` do not consume repair budget. After `RequestChanges`, a
direct or no-op report resubmission is rejected; a materially changed ledger update
clears `ReviewRepairRequired` without forcing another Planner approval.

## Checkpoints

Cadence has two independent checkpoint policies.

### Dirty-Work Continuity Checkpoint

```text
workspace has uncheckpointed changes
and five minutes have elapsed since the last continuity marker
  -> block the next mutation
  -> require write_checkpoint
  -> save the typed checkpoint
  -> reset the continuity timestamp
  -> continue the same Executor session
```

Implementation:

- Cadence state records `LastContinuityAt` using an injected `TimeProvider`.
- The timestamp is initialized when the run starts.
- An accepted `ask_planner` or `write_checkpoint` call resets the timestamp.
- Invalid or merely attempted capability calls do not reset it.
- A Tandem `ToolInterceptor` checks for a dirty workspace and elapsed time before
  workspace mutation.
- Cadence provides the blocked message and remediation instruction.
- The existing typed `write_checkpoint` capability records the checkpoint and
  ends the Executor visit.
- Composition routes back to Executor with its conversation retained.
- No session reset occurs for this policy.

The purpose is not to measure an exact amount of code. It prevents substantial
implementation intent from living only in Executor's conversation for too long. Git
already preserves file changes; the checkpoint preserves Executor's current
understanding, uncertainties, and next action.

### Token Rotation

```text
context reaches 80%
  -> native Tandem checkpoint gate
  -> require write_checkpoint
  -> save the checkpoint
  -> reset the Executor session
  -> fresh Executor resumes from durable context
```

Tandem's native token-based `CheckpointPolicy` owns this behavior. The dirty-work
policy does not rotate; the token policy does.

Authoritative changed files, outcomes, constraints, candidate state, and
verification state are derived by Cadence rather than trusted from Executor's
checkpoint prose.

## Human Boundary

Planner and Reviewer route Human-owned decisions through typed Tandem
interactions. The Human owns:

- Product and UX decisions.
- Business and security policy.
- Permissions and tenancy.
- Data and migration policy.
- Legal and compliance decisions.
- Final inspection and merge.

Repository facts and engineering choices remain Planner or Reviewer
responsibilities.

## Completion And Publication

Reviewer acceptance produces a review-ready result containing:

- Pinned base SHA.
- Accepted candidate SHA.
- Outcome assessments.
- Verification evidence.
- Reviewer decision.
- Reviewer doctrine source and SHA-256.
- All Reviewer findings, including non-blocking findings on acceptance.

Publication:

- Publishes exactly the accepted candidate SHA.
- Uses an isolated Cadence branch.
- Reconciles idempotently.
- Does not merge.
- Does not modify the Human's working tree.

The Human inspects and merges manually.

## Ownership

Tandem supplies generic pipeline mechanics:

- Typed state, steps, outcomes, predicates, and routes.
- Typed capabilities and capability validation.
- Agent-visit termination after capability acceptance.
- State guards, tool effects, and tool interception.
- Consumer-defined blocked messages and remediation capabilities.
- Structured-output parsing, validation, and acceptance policies.
- Conversation retention and reset.
- Native token checkpoints.
- Typed Human interactions.
- Deterministic stages.
- Observation and persistence.
- Bounded local-process execution through Advanced `LocalProcess`.

Cadence owns coding-pipeline semantics:

- Executor, Planner, and Reviewer prompts.
- Packet and lifecycle state.
- Revision-scoped mutation authority.
- Outcome ledger and report contract.
- Five-minute dirty-work continuity policy.
- Candidate capture and git policy.
- Git workflows, platform shell selection, and verification policy.
- Review contract and repair limit.
- Publication semantics.

The observer reports what happened. Policies decide what must happen. Guards
enforce it. Capabilities release or transition it. Stages perform bounded
deterministic operations and return typed state. Composition owns orchestration.

## Tandem Implementation Map

Cadence must use Tandem as an ordinary external package consumer. It must not use
Tandem internals, privileged project references, copied runtime machinery, or a
second orchestration engine. If a required behavior cannot be expressed through
the public package seam, treat that as a Tandem API gap rather than smuggling the
implementation into Cadence.

| Cadence behavior | Tandem concept |
| --- | --- |
| Executor, Planner, and Reviewer | `AgentDefinition<CadenceState>` |
| Executor asks Planner | Typed `AgentCapability<CadenceState>` |
| An accepted ask ends Executor's visit | Capability conclude-on-accept semantics |
| Executor routes to Planner and back | Typed state predicates and `.Route(...)` |
| Executor retains its session across Planner | `.ContinueSession()` plus conversation-retention policy |
| First-edit and changed-plan authority | `WithWorkspace(...)` plus `WithStateGuard(...)` |
| Blocked-edit guidance | Consumer-defined `AgentStateGuard.Message` and remediation capability |
| Prose-only correction | `WithContinuationPolicy(...)` and `RequiredToolName` |
| Planner and Reviewer decisions | `WithOutput(...)` plus FluentValidation |
| Mandatory repository inspection | `RequireOutputAcceptance(...)` over `ToolEvidence.RepositoryInspection` |
| Human escalation | `PipelineInteraction<TState, TRequest, TResponse>` |
| Candidate capture | Deterministic `[PipelineStage]` |
| Verification loop | Deterministic stage plus typed state predicates |
| Same-run repair loop | Explicit routes over typed review state |
| Five-minute dirty-work checkpoint | `ToolInterceptor`, `TimeProvider`, and `write_checkpoint` |
| Token checkpoint and rotation | Native `CheckpointPolicy` and session reset |
| Exact publication | Separate deterministic application operation |
| Persistence and observation | `.Persist()` and run observer, never lifecycle control |

Core APIs describe application meaning: state, participants, capabilities,
outcomes, routes, and Human interactions. Advanced APIs are used only for runtime
mechanics such as workspace authority, tool interception, output acceptance,
checkpoint policy, conversation retention, and execution evidence.

## Potential Tandem Hardening

Richer inspection observations could eventually prove that Planner and Reviewer
successfully inspected the exact material paths cited in their decisions. This is
useful hardening, but it is not required to implement Cadence's initial pipeline.

Do not introduce generic coding-specific concepts into Tandem Core without a
second demonstrated consumer.

Tandem records ordered invocation arguments, statuses, and fixed-command process
results. Cadence mechanically checks the exact Reviewer candidate range, authoritative
latest manifest and repository-wide diff order, verification packet order, latest-attempt
status/completeness, and every verification evidence reference. Invocation evidence does
not prove pagination completion, use of every returned path, or semantic use; those
obligations remain prompt-enforced. Typed file-line and symbol evidence validates shape,
not repository existence; that also remains prompt-enforced.

## Implementation Order

1. Bootstrap Cadence as an external Tandem consumer and extract the useful
   `Tandem.Delivery` implementation through public package seams.
2. Port and merge the Executor, Planner, and Reviewer prompts and identities.
3. Introduce revision-scoped mutation authority and fix Planner constraint
   replacement.
4. Add the typed outcome ledger and strict report acceptance.
5. Add the five-minute dirty-work interceptor and continuity timestamp.
6. Bind candidate capture, verification, and review to exact SHAs.
7. Strengthen Reviewer's findings and acceptance contract.
8. Add bounded same-run repair routing and Human escalation.
9. Harden exact-SHA publication.
10. Complete end-to-end behavioral proofs and remove obsolete Lathe-shaped
    concepts.

## Behavioral Proofs

At minimum, prove:

1. A pre-approval Executor write is rejected and no file changes.
2. A valid `ask_planner` call ends Executor's visit, routes to Planner, and later
   resumes the same Executor session.
3. Invalid capability input remains with Executor for correction.
4. Planner approval without repository inspection is rejected.
5. Planner approval opens mutation only for the current approach revision.
6. A new Planner request immediately closes mutation.
7. New Planner constraints replace old constraints.
8. Reports with missing, duplicate, or unknown outcomes are rejected.
9. A candidate SHA is captured before verification.
10. Red verification routes back to Executor with exact evidence.
11. A new candidate invalidates old verification and review facts.
12. Reviewer acceptance without read-only changed-file and diff inspection is rejected.
13. Reviewer cannot accept an undelivered outcome.
14. Request changes loops through Executor, candidate capture, and full verification.
15. The repair cap routes to the Human.
16. A dirty-work checkpoint blocks mutation after five minutes, resets on an
    accepted checkpoint or Planner request, and retains the Executor session.
17. A token checkpoint records state and rotates to a fresh Executor session.
18. Publication refuses a SHA different from the accepted candidate.
19. The end-to-end happy path reaches a review-ready publication candidate.
20. Human escalation suspends and resumes through typed interactions.
21. A valid no-change packet still produces an exact candidate SHA for verification
    and review.

## Governing Rule

> Cadence defines the coding lifecycle as typed state, participants, bounded
> operations, and explicit routes. Tandem executes it. No second coordinator is
> introduced.
