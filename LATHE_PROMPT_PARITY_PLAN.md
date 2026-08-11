# Lathe Prompt Parity Plan

## Goal

Bring Cadence's single-run Executor, Planner, and Reviewer contracts up to the
battle-tested behavioral standard encoded in Lathe's prompts without importing
Lathe's campaign, daemon, queue, or cross-run orchestration mechanics.

Cadence may translate Lathe semantics into typed Tandem state, capabilities,
routes, interactions, guards, and acceptance policies. It must not replace precise
decision rules with general statements such as "inspect carefully", "be sound", or
"ground findings" when Lathe defines the actual discriminator.

## Source Of Truth

Use the implementation, not summaries or this plan, when checking parity:

- `../lathe/packages/core/src/domain/prompts.ts`
- `../lathe/packages/core/tests/prompts.test.ts`
- `../lathe/packages/core/src/domain/review.ts`
- `../lathe/packages/core/src/domain/convergence.ts`
- `src/Cadence/Agents/Executor/ExecutorPrompts.cs`
- `src/Cadence/Agents/Planner/PlannerPrompts.cs`
- `src/Cadence/Agents/Reviewer/ReviewerPrompts.cs`
- the corresponding Cadence decision records, validators, policies, state
  transitions, composition routes, capabilities, interactions, and tests

Lathe is the behavioral baseline. A difference is acceptable only when it is:

1. deliberately excluded below;
2. replaced by a stronger typed or mechanical Cadence guarantee; or
3. recorded as an explicit Tandem API gap.

## Confirmed Defects

### Planner cannot reject and replace an unsafe approach

Cadence currently has `Proceed`, `ProceedWithConstraints`, `NeedsHuman`, and
`Stop`. It has no non-authorizing equivalent of Lathe's `revise_slice`.

Add `ReviseApproach` with these semantics:

- the current proposed approach is rejected;
- the Planner returns a corrected approach and concrete safe next action;
- mutation authority remains closed;
- composition routes back to Executor;
- Executor must submit a new approach revision to Planner;
- no edit is authorized until that revised approach receives `Proceed` or
  `ProceedWithConstraints`.

Do not encode known breakage, an incomplete implementation surface, or a false
premise as an authorizing constraint.

### Non-accepted decisions erase live constraints

Change state transition behavior so:

- `Proceed` replaces active constraints with an empty list;
- `ProceedWithConstraints` replaces active constraints with its complete list;
- `ReviseApproach`, `NeedsHuman`, and `Stop` preserve the previously accepted
  constraint list.

Add state and routing tests for every decision.

### Checkpoints trust model-authored facts

Remove model authority over objective lifecycle facts.

Executor checkpoints should contain only:

- a successor-oriented summary;
- uncertainties;
- a precise next action.

Cadence must derive changed files, inspected files, outcome status, active
constraints, candidate state, and verification state from typed state and runtime
evidence. Do not ask Executor to reproduce those facts in checkpoint prose.

## Planner Contract

Add or strengthen:

- `ReviseApproach`;
- typed `QuestionType`;
- `CurrentSlice` on Planner requests;
- `SafeNextAction` on every Planner response;
- a corrected approach on `ReviseApproach`;
- validator rules that distinguish authorizing, revising, Human, and terminal
  decisions;
- preservation of accepted constraints on non-authorizing decisions.

Question types should represent the same-run decisions Cadence actually supports,
including at least:

- architecture or engineering direction;
- repository procedure;
- implementation-surface or slice review;
- verification strategy;
- diff or obligation closure audit;
- handoff interpretation;
- stop-condition review.

Do not add Lathe-only reconciliation, campaign, or promotion statuses merely for
name parity.

## Planner Prompt

Port the operational clauses, not merely their theme:

- Executor evidence is an untrusted pointer, not proof.
- Inspect material repository facts before deciding.
- Audit the complete approach, not only the literal question.
- Correct XY problems and false premises directly.
- Derive requirements from packet intent and existing repository invariants.
- Decide whether the executable surface must expand, contract, or change owner.
- Constraints cannot authorize known breakage.
- A corrected or materially changed approach requires another approval cycle.
- A Planner consultation is not evidence that prior obligations are closed.
- Existing constraints remain open until repository evidence proves closure.
- If a prior instruction failed, treat that failure as contradictory evidence.
- Do not repeat a failed instruction without explaining why the prior attempt did
  not test it.
- State one concrete safe next action for every response.
- Escalate only decisions genuinely owned by the Human.
- Stop only when no safe engineering next action can be stated after inspection.

## Executor Contract

Add a typed incremental outcome-ledger capability. It should record, per packet
outcome:

- current status;
- concrete evidence;
- current implementation state;
- next action where work remains.

The ledger is authoritative for progress, checkpoints, and final reporting.
Executor prose is not.

Keep these typed lifecycle capabilities:

- ask Planner;
- update outcome ledger;
- write checkpoint;
- submit implementation report.

Add a deliberate failure path for Planner transport failure:

- retry once;
- do not improvise if Planner remains unavailable;
- preserve state, constraints, and ledger evidence;
- route to a typed blocked result or Human interaction after the retry fails.

## Executor Prompt

Add or restore these exact obligations:

- make the smallest change that satisfies the packet;
- follow the nearest established repository pattern;
- do not perform unrelated refactors;
- do not create formatting churn;
- inspect before editing;
- treat uncertainty, surprise, and a changed plan as Planner-routing signals;
- keep ordinary red-green iteration local;
- consult Planner before a second conceptual fix attempt for the same problem;
- never mutate Git;
- use lifecycle capabilities rather than claiming transitions in prose.

When a Planner instruction fails, require:

- the exact prior instruction;
- the exact attempted change;
- the exact failing command and relevant output;
- an explanation of how the evidence contradicts the instruction;
- the revised understanding and proposed next approach.

When resuming from a checkpoint or predecessor context:

- treat completion claims as claims, not proof;
- spot-check claimed-done outcomes against the worktree;
- reopen any outcome whose evidence does not hold;
- continue from the authoritative ledger and repository state.

## Reviewer Rubric

Supply the Human Operator's engineering doctrine as an explicit review rubric.
The rubric must define the standards used to judge:

- architecture and ownership boundaries;
- hidden dependencies and type lies;
- behavior preservation;
- test quality;
- data and security safety;
- acceptable non-blocking nits.

Doctrine source decision: a required configured file. Cadence resolves relative
paths against the configuration directory, loads the file once during run setup,
rejects missing or blank content, and computes SHA-256 over the exact loaded bytes.
The immutable doctrine is threaded through options, participant construction,
Reviewer, validation, and publication without storing its body in `CadenceState`.

## Reviewer Prompt

Port these judgment rules:

- treat Executor reports and Planner approval as claims, not proof;
- inspect the exact pinned-base-to-candidate diff;
- inspect relevant unchanged integration seams;
- derive the real requirement from packet intent and repository invariants;
- reject requested-shape compliance when behavior or downstream coherence is
  wrong;
- green verification is necessary but insufficient;
- do not manufacture findings or block on taste.

Require an explicit test-quality audit:

- inspect every added or changed test;
- inspect new branches and error paths;
- identify exact untested symbols or branches;
- reject mock soup;
- reject tests that only assert mock interaction;
- reject fake integration coverage represented as real behavior;
- assess whether regression coverage proves the delivered behavior.

## Reviewer Evidence

Strengthen evidence from arbitrary non-empty strings to reproducible references.
Support evidence grounded in:

- file and line;
- symbol;
- exact verification command and output;
- exact packet outcome;
- exact Planner constraint;
- quoted doctrine clause.

Require:

- every outcome assessment to cite where delivery exists or is absent;
- every constraint assessment to cite closure evidence;
- every finding to identify the precise defect and proof;
- command findings to name the exact command;
- repository findings to name the exact file, line, symbol, or behavior.

## Reviewer Decisions

Permit acceptance with useful non-blocking findings:

- `Critical` or `High` findings block acceptance;
- `Medium` or `Low` findings may remain on `Accept` when genuinely non-blocking;
- `RequestChanges` requires concrete Executor-fixable work;
- Human escalation remains restricted to Human-owned domains;
- repair-budget exhaustion remains a pipeline interaction, not a fake Human
  domain decision.

Preserve non-blocking findings in the final review-ready result rather than forcing
Reviewer to discard them or manufacture another repair pass.

## Verification And Git Review

Keep deterministic pipeline verification as the authoritative mechanical floor.
In addition, require Reviewer to:

- independently run the declared verification commands;
- run additional repository-appropriate read-only checks when needed;
- inspect changed-file discovery using the exact pinned base and candidate SHAs;
- follow changed-file pagination to completion;
- follow every diff page to completion;
- inspect every changed path;
- inspect relevant unchanged source, tests, contracts, and configuration.

Already-preserved invariants:

- Executor has no writable Git tools;
- candidate capture is pipeline-owned;
- verification and review are bound to the exact candidate SHA;
- publication uses the Reviewer-accepted SHA;
- publication does not merge or mutate the Human's working tree.

## Tandem Evidence Gap

Tandem currently proves that a tool category or name was used, but cannot prove:

- exact invocation arguments;
- exact base and candidate values passed to Git tools;
- completion of every pagination cursor;
- inspection of every returned changed path;
- semantic use of every returned result.

Cadence can prompt those requirements today, but only partially gate them. Before
claiming mechanical parity, either:

1. add richer typed tool-invocation evidence to Tandem and enforce it from
   Cadence output acceptance; or
2. explicitly document which Git-inspection clauses remain prompt-enforced.

Do not claim that a `git_changed_files` and `git_diff` tool-name observation proves
complete exact-SHA review.

Cadence now requires `git_changed_files` and `git_diff` for every non-Human Reviewer
decision and requires every generated command to be run. `Accept` requires all
successful command-name observations. `RequestChanges` may instead carry a
Critical/High finding with an exact declared command and nonzero model-reported
stdout/stderr. Tandem does not expose failed invocation details, exact invocation
arguments, pagination/path completeness, semantic use, or typed file/symbol
existence; those limitations remain prompt-enforced.

## Prompt Contract Tests

Add clause-level tests so later prompt cleanup cannot erase the scar tissue.

At minimum, prove:

- `ReviseApproach` does not authorize mutation;
- a corrected approach requires another Planner approval;
- non-accepted decisions preserve active constraints;
- known breakage cannot be encoded as an authorizing constraint;
- failed Planner instructions are treated as contradictory evidence;
- Planner responses contain a safe next action;
- checkpoints contain no model-authored objective lifecycle facts;
- resumed work distrusts predecessor completion claims;
- smallest-change and no-churn obligations remain present;
- Reviewer receives the selected doctrine;
- Reviewer performs the explicit test-quality audit;
- Reviewer requires precise, reproducible evidence;
- `Accept` permits only non-blocking findings;
- Git and pagination obligations remain present;
- Planner transport retries once and then fails closed;
- all new decision fields and status-specific combinations are validated;
- composition routes every status without hidden coordinator logic.

Prefer behavioral tests over snapshots of entire prompt strings. Pin the clauses
and transitions that protect behavior while allowing wording to improve.

## Deliberate Exclusions

Do not port these Lathe lifecycle mechanics into Cadence:

- campaigns;
- cumulative campaign contracts;
- generated repair or follow-up packets;
- cross-run convergence;
- model promotion passes;
- staged parent/child chains;
- queue and daemon behavior;
- MCP lifecycle tool naming;
- automatic merge;
- convergence-owned commit-message authoring.

These exclusions do not justify dropping same-run engineering judgment,
verification, evidence, checkpoint, or mutation-authority safeguards.

## Implementation Order

1. Add Planner decision and state-transition semantics.
2. Fix accepted-only constraint replacement.
3. Add revision-safe `ReviseApproach` composition routes.
4. Simplify checkpoint ownership and add the outcome ledger.
5. Add Planner failure retry and blocked handling.
6. Port Planner and Executor operational clauses.
7. Decide and inject the Reviewer doctrine source.
8. Strengthen Reviewer evidence and decision validation.
9. Port test-quality, requirement-sanity, verification, and Git-review clauses.
10. Add clause-level prompt and end-to-end route tests.
11. Address or explicitly qualify the Tandem invocation-evidence gap.
12. Perform the final Lathe-to-Cadence parity pass below.

## Final Lathe-To-Cadence Parity Pass

Do not close this work from memory or from this plan. Re-open Lathe and compare the
actual prompt implementation clause by clause after all Cadence changes are in
place.

### Required Lathe inputs

Read in full:

- `../lathe/packages/core/src/domain/prompts.ts`
- `../lathe/packages/core/tests/prompts.test.ts`
- `../lathe/packages/core/src/domain/review.ts`
- the relevant same-run rules in
  `../lathe/packages/core/src/domain/convergence.ts`

Specifically map these Lathe prompt surfaces:

- `BRIDGE_CONTRACT`
- `q1InitialInspection`
- `q2BeforeFirstEdit`
- `q3Reconciliation`
- `q4ProgressAndCompletion`
- `q5QuestionsAndEscalation`
- `q6ScopeAndGitDiscipline`
- `q7IterationAndTesting`
- `q8Finish`
- `renderPlannerNudge`
- `renderCheckpointNudge`
- `renderPeriodicCheckpointNudge`
- `renderReorientHandoff`
- `renderPlannerDecisionDelivery`
- `renderDaddySeed`
- `renderPlannerQuestion`
- `renderSuperReview`
- `renderFinalReview`

Review `renderFollowupAuthoring` only to ensure campaign-only behavior remains
deliberately excluded; do not port repair-packet mechanics into Cadence.

### Required Cadence inputs

Compare against:

- all three Cadence prompt classes;
- all prompt message builders;
- Planner and Reviewer response records and validators;
- Executor capability request records and validators;
- mutation-authority state transitions;
- checkpoint and outcome-ledger state;
- composition routes;
- Human interactions;
- output-acceptance and tool-evidence policies;
- exact-SHA candidate, verification, review, and publication stages;
- prompt contract tests and end-to-end behavioral tests.

### Required output

Produce a checked-in parity matrix with one row per material Lathe clause:

| Lathe clause | Cadence implementation | Status | Evidence | Rationale |
| --- | --- | --- | --- | --- |
| Exact operational rule | Prompt/type/route/policy/test | Preserved, Stronger, Deliberately Excluded, Gap | File and line references | Why the mapping is valid |

Rules for the matrix:

- `Preserved` requires equivalent prompt wording plus any required typed behavior.
- `Stronger` requires a mechanical Cadence guarantee and a behavioral test.
- `Deliberately Excluded` must cite Cadence's single-run scope and explain why no
  same-run safeguard is lost.
- `Gap` must become implementation work or an explicit documented Tandem
  limitation before completion.
- General thematic similarity is not parity.
- A type without routing, a prompt without validation, or a policy without a test
  is incomplete.

### Final completion gate

The work is complete only when:

- every material Lathe single-run clause has a parity-matrix disposition;
- every `Gap` is resolved or explicitly accepted as a Tandem limitation;
- every `Stronger` claim has a test proving the mechanical guarantee;
- deliberate exclusions contain no hidden same-run safety regression;
- Cadence's prompts remain direct and operational rather than generic;
- the full Cadence verification suite passes;
- any required Tandem hardening passes Tandem's own full package-consumer and
  public-API gates.

## Completion Status (2026-08-11)

Implementation and the final source-based audit are complete subject to the explicit
prompt-enforced limitations below. The
`LATHE_PROMPT_PARITY_MATRIX.md` dispositions cover `BRIDGE_CONTRACT`, q1-q8,
Planner/checkpoint/periodic nudges, same-run Reorient, Planner/Daddy, final review,
super review, and all deliberate exclusions.

The bounded Planner decision history requirement is Preserved/Stronger rather than
new work: `RunRecordStore.ReadContextAsync` selects `TakeLast(5)`,
`CadenceLedgerContextFormatter` renders those decisions, and
`CadenceAgentFactory.WithMessageAugmentation` injects the durable context into every
fresh Executor session. The Reorient behavior test confirms that prior conversation
is absent while ledger, checkpoint, constraints, and recent decisions are rehydrated.

Remaining gaps are Tandem's inability to prove exact tool arguments, exact
base/candidate argument values, pagination and changed-path completeness, semantic
use, failed invocation details, and typed file-line/symbol existence. Successful
command-name observations are gated; Reviewer red results are explicitly model
evidence for an exact packet command. These limitations do not permit Cadence to
claim exact-SHA review or repository-reference proof from tool names and typed shape
alone.
