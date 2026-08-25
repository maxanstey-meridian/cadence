# Task

Replace Cadence's sentence-shaped constraint identity and scattered role-specific contract rendering with explicit typed packet and Planner constraints, one derived delivery-obligation catalog shared by prompts and coverage validation, and a lossless one-off migration of run `01a0251d6d50735582e93e9e40047b15` so its unchanged hostile candidate can be resumed as a fresh Reviewer litmus.

## Workspace

Run Threadkeeper with this workspace:

`/Users/max/Sites/cadence`

The Cadence repository is the workspace and Git-review root. This delivery also owns the following explicit external operational targets, which must be accessed through bounded shell commands because Threadkeeper file tools remain confined to the workspace:

- `/Users/max/.cadence/plans/remove-superseded-case-paths.md`: the live packet used by the retained run.
- `/Users/max/.cadence/plans/tandem-terminal-tab-width.md`: another current operator-authored Cadence packet that must be migrated only if the new production packet contract otherwise makes it invalid.
- `/Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15`: the retained workspace and ledger that form the behavioral litmus.
- `/Users/max/.cadence/migrations/01a0251d6d50735582e93e9e40047b15`: run-specific one-off migration source, mappings, backup, and reports created by this delivery.

Do not use workspace file tools with absolute external paths. Create and inspect external migration artifacts, update the two named packet files, copy and replace the named ledger, and resume the named run through explicit shell commands from the Cadence workspace. Keep each command bounded to the exact paths above.

`/Users/max/Sites/cadence` currently has substantial intentional uncommitted work spanning the current lifecycle, prompt, authority, GitNexus, terminal, tests, documentation, and packet changes. Treat the entire existing dirty Cadence worktree as the starting baseline. Preserve all existing changes unless this delivery genuinely requires a careful overlapping edit. Do not reset, discard, overwrite, broadly reformat, commit, or otherwise manage Git state.

`/Users/max/Sites/tandem` also has substantial unrelated intentional uncommitted work. It is not in scope. Do not modify it. Cadence consumes locally packed Tandem packages through its existing `task prepare`/`task check` workflow; use the current sibling package source without editing Tandem.

Do not modify any other repository, plan, run, ledger, configuration file, or external path.

## Desired Result

- Packet constraints and Planner constraints have explicit stable IDs and requirement text. Constraint prose is never used as identity.
- Packet authors own packet-constraint IDs. Planner owns the IDs of constraints it authors on `Proceed`.
- Cadence derives one deterministic, non-persisted delivery-obligation catalog from current authoritative state: packet outcomes, acceptance criteria, packet constraints, and active Planner constraints.
- Executor, Planner, and Reviewer receive the same delivery contract rendered once from that catalog, followed by role-specific lifecycle context. They no longer reconstruct or scatter the contract independently.
- Every obligation shown to an agent has one explicit bracketed reference. Capability and structured-output validation use the same references that were shown to the agent.
- Executor reporting uses one obligation-claim collection for acceptance criteria and active constraints. Outcome progress remains the separate authoritative lifecycle mechanism for outcome status.
- Reviewer decisions use one assessment collection covering the complete current obligation catalog.
- Reviewer doctrine, active repair findings, verification, reports, checkpoints, Human answers, and operator instructions remain outside the obligation catalog because they are policy or lifecycle context, not delivery requirements.
- Existing structural acceptance rules remain: exact assessment coverage, nonblank evidence, all obligations satisfied for `Accept`, complete green candidate-bound verification for `Accept`, blocking findings for `RequestChanges`, precise finding locations, and the Human-domain boundary.
- No new tool-count, file-count, pagination, shell-use, inspection-receipt, provenance, or model-attestation requirement is added.
- Old string-shaped persisted Cadence state deliberately stops being ordinarily resumable. No production compatibility reader, dual JSON shape, generated legacy ID, sentence hash, ordinal `C-1`, or automatic state migration ships.
- The one named retained run ledger is migrated in full and like for like. Every affected persisted value in every ledger entry is converted to the new representation while every event, state transition, claim, decision, and historical distinction remains intact.
- After migration, the same run resumes at the same clean, fully verified candidate and reaches a fresh Reviewer with the new coherent contract.
- The fresh Reviewer is allowed to decide independently. The expected correct result is `RequestChanges` grounded in concrete repository violations. The run must not fail because obligation IDs were absent, implicit, inferred, or structurally invalid.

## Central Invariants

### Contract ownership

- The packet defines required delivery intent.
- `PacketOutcome`, `PacketAcceptanceCriterion`, packet constraints, and Planner constraints remain distinct domain concepts with distinct owners.
- A common read model may project them for agent comprehension and coverage validation; it must not flatten them into one persisted source of truth.
- The obligation catalog is derived from `CadenceState` and is never stored as another state field.

### Constraint identity

- Packet constraint identity is an explicit Human-authored ID.
- Planner constraint identity is an explicit Planner-authored ID.
- Requirement text is content, not identity.
- Rewording requirement text without changing an ID does not create a new identity accidentally.
- Reordering constraints does not change their identity.
- No identity is generated from array position, prose, hashing, or runtime convention.

### Epistemic separation

- Delivery obligations describe what must be true.
- Verification records which configured commands passed.
- Reports, progress, checkpoints, decisions, and ledger entries record claims or lifecycle facts; they do not establish repository truth.
- Doctrine tells Reviewer how to review and is not an independently assessed delivery obligation.
- Active review findings describe defects in a prior candidate and are not silently promoted into the authored delivery contract.

### Litmus integrity

- The retained run migration changes representation only.
- No persisted claim may be removed, corrected, softened, strengthened, reordered, deduplicated, trimmed, or regenerated.
- The migration must preserve misleading Executor claims, completed outcome claims, green verification, Planner claims, and every other state fact so the fresh Reviewer faces the same hostile evidence as before.
- The repository workspace and candidate tree must not be changed as part of ledger migration or litmus preparation.

## Settled Domain Design

### Packet constraints

Replace `IReadOnlyList<string> Packet.Constraints` with a typed value equivalent to:

```csharp
public sealed record PacketConstraint(string Id, string Requirement);
```

The exact type and placement should follow existing packet-domain conventions. It must have no dependency on infrastructure, prompts, agents, or persistence implementation.

Packet validation must require:

- a non-null constraint collection;
- no null elements;
- nonblank IDs;
- nonblank requirements;
- unique packet-constraint IDs using ordinal comparison;
- a bounded stable ID format consistent with existing packet IDs;
- authored order preserved.

`PacketReader` must trim constraint IDs and requirements in the same way it normalizes outcomes and acceptance criteria.

### Planner constraints

Replace string Planner constraints with a typed value equivalent to:

```csharp
public sealed record PlannerConstraint(string Id, string Requirement);
```

Update both `PlannerDecision.Constraints` and `CadenceState.PlannerConstraints`.

Preserve existing lifecycle semantics:

- only `Proceed` may carry newly authored constraints;
- a `Proceed` replaces the complete active Planner constraint collection;
- `ReviseApproach`, `NeedsHuman`, and `Stop` carry no new constraints;
- non-authorizing decisions do not accidentally erase retained active constraints from state;
- order is preserved;
- IDs are nonblank and unique within one Planner decision;
- requirements are nonblank and meaningful.

### Derived obligation catalog

Create one pure derived catalog over current `CadenceState`. The exact local naming may follow repository style, but its entries must preserve at least:

```text
Reference
Kind
Local ID
Requirement/description
Linked outcome ID where applicable
```

Kinds:

```text
Outcome
AcceptanceCriterion
PacketConstraint
PlannerConstraint
```

Use unambiguous derived references so different owners need not coordinate globally unique local IDs:

```text
outcome:operational-ownership
acceptance:AC-1
packet-constraint:no-compatibility-paths
planner-constraint:retain-consumed-provider-capabilities
```

Catalog order is deterministic:

1. Packet outcomes in authored order.
2. Acceptance criteria in authored order.
3. Packet constraints in authored order.
4. Active Planner constraints in accepted order.

The catalog must not contain doctrine, findings, verification, progress, reports, checkpoints, Human answers, or operator instructions.

### Shared delivery-contract rendering

Executor, Planner, and Reviewer must use one shared renderer over the derived catalog. Render one coherent block equivalent to:

```text
Delivery contract

Outcomes
- [outcome:operational-ownership] ...

Acceptance criteria
- [acceptance:AC-1] for [outcome:operational-ownership]: ...

Packet constraints
- [packet-constraint:no-compatibility-paths] ...

Active Planner constraints
- [planner-constraint:retain-consumed-provider-capabilities] ...
```

Tell agents once, adjacent to this block, to use the bracketed references exactly when a capability or structured result requests an obligation ID.

Delete independent outcome, acceptance, packet-constraint, and Planner-constraint formatting from the three role prompt builders. Do not duplicate the rendered contract later in role-specific context.

Keep role-specific context separate:

- Executor: outcome progress, repair findings, latest Planner decision context, verification, candidate, unchanged-candidate state, and checkpoint.
- Planner: checkpoint, Executor proposal, verification, prior Planner decision, and Human answer.
- Reviewer: pinned base, candidate, verification, repair findings, Executor handoff, and Human answer.

Preserve the recently added autonomous-agent identity and blunt packet/state/repository/verification/ledger hierarchy. This delivery clarifies contract identity; it must not revert that prompt work or return to quasi-legal repeated evidence prose.

## Capability And Review Contracts

### Executor report

Replace separate `acceptanceClaims` and `constraintClaims` with one obligation-claim collection. Keep outcome progress separate.

The expected reportable catalog kinds are:

```text
AcceptanceCriterion
PacketConstraint
PlannerConstraint
```

Each claim uses the exact derived reference shown in the shared contract and preserves its evidence text. One validator checks:

- exactly one claim for each current reportable obligation;
- no duplicates;
- no unknown references;
- no missing references;
- nonblank evidence;
- every outcome is complete through existing `OutcomeProgress` state;
- existing review-repair and continuity gates remain unchanged.

Do not add outcome claims to the report because outcome status already has an authoritative typed lifecycle owner.

### Reviewer decision

Replace separate `outcomeAssessments`, `acceptanceAssessments`, and `constraintAssessments` with one assessment collection over the complete catalog.

Each assessment contains:

```text
exact derived obligation reference
satisfied boolean
evidence
```

Preserve decision semantics:

- `Accept`: exact complete catalog coverage, every assessment satisfied and evidenced, complete green candidate-bound verification, and no Critical or High finding.
- `RequestChanges`: exact evidenced catalog coverage and at least one concrete Executor-fixable Critical or High finding with a precise repository location.
- `NeedsHuman`: only a genuine Human-owned product, UX, business, security, permission, tenancy, data, migration, legal, or compliance decision.

Do not add mechanical proof that Reviewer inspected particular files or used a particular tool sequence. The existing structural repository-inspection policy is not part of this task unless compilation requires adapting it to the new output shape; do not strengthen it.

### Output examples and correction messages

- Update Planner, Executor capability, and Reviewer output examples to use explicit typed constraint objects and derived obligation references.
- Correction messages must identify actual missing, unknown, or duplicate references. They must never refer to sentence-shaped IDs or force the model to infer an ordinal convention.
- Keep errors concise. Do not include complete raw capability arguments in new validation errors.
- The separate existing Tandem raw-argument presentation defect is not in scope because Tandem is not being modified here.

## Packet Migration

Update `/Users/max/.cadence/plans/remove-superseded-case-paths.md` from string constraints to typed constraints without changing requirement text or order.

Use these local IDs in current order:

```text
no-compatibility-paths
direct-destructive-migration
preserve-form-intake-effects
preserve-consumed-provider-capabilities
preserve-submission-ownership
preserve-case-lifecycle-ownership
preserve-migration-history
```

The seven existing requirement strings must remain textually identical apart from YAML representation and unavoidable block-scalar serialization semantics. Do not rewrite or improve their wording during this migration.

Update other Cadence-owned packet examples and authoring guidance to the new typed shape, including:

- `/Users/max/Sites/cadence/examples/packet.md`
- `/Users/max/Sites/cadence/examples/tandem-packet-validation-locations.md` if present in the current worktree
- `/Users/max/Sites/cadence/README.md`
- `/Users/max/Sites/cadence/skills/packet-authoring/SKILL.md`

Also update `/Users/max/.cadence/plans/tandem-terminal-tab-width.md` only if production packet validation would otherwise leave an operator-authored Cadence packet invalid. Preserve its constraint text and intent exactly. Do not alter unrelated plans.

## Deliberate Persisted-State Break

The following persisted shapes intentionally change:

```text
Packet.Constraints
CadenceState.PlannerConstraints
PlannerDecision.Constraints
ExecutorTransition.ReportSubmitted request claims
ReviewDecision assessments
```

Do not ship:

- a string-or-object JSON converter;
- legacy deserialization branches;
- generated IDs for old constraints;
- sentence hashing;
- ordinal compatibility IDs such as `C-1`;
- dual old/new report properties;
- dual old/new Reviewer assessment properties;
- automatic migration during ordinary `resume`;
- a permanent migration command in the Cadence CLI.

New code may clearly reject old string-shaped state. Update documentation so the normal resume guarantee applies to state written under the current contract rather than claiming all prior internal shapes remain resumable.

## One-Off Litmus Run Migration

### Target

```text
Run ID: 01a0251d6d50735582e93e9e40047b15
Run directory: /Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15
Ledger: /Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15/ledger.sqlite3
Workspace: /Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15/workspace
Candidate: 586650a4b0ca2a1e621c6d3c0bbc5462b28bade2
Expected candidate tree: bdc367d98e2a80f2046581935a51ebc834b7a081
Pinned base: 68195809d425c5b923b90a90bc7e0fcfe6970d84
```

The candidate tree is intentionally incomplete and is identical to candidate `42b7b62597e2f51de75fb17e949d003613326119`. Do not repair or modify it before the fresh review.

### Whole-ledger migration

Migrate the complete ledger for this run. Do not select one accepted state, append a replacement state, start from the latest state, or treat historical Cadence values as inert. Every persisted value anywhere in the ledger that uses a changed Cadence contract must be transformed to the new representation.

The migration must preserve the ledger as the same run and the same history:

- same run row and run ID;
- same ledger contracts except where an actual contract-version update is required by the new persisted shape;
- same streams;
- same entry count;
- same entry IDs;
- same sequence values;
- same ordering;
- same step IDs, identities, names, effects, results, outcome kinds, usage, timestamps, statuses, and terminal state;
- same documents, document keys, versions, and timestamps;
- same accepted/rejected distinction;
- same lifecycle meaning in every payload;
- no new synthetic lifecycle event;
- no removed, collapsed, superseded, or promoted event.

Transform affected JSON payloads in a copied ledger database under one controlled migration transaction. Validate the complete migrated copy before it can replace the live ledger. Do not mutate the only copy of the live database in place, and do not append a synthetic accepted state as a substitute for migrating historical values.

The one-off migration implementation must not ship as production Cadence behavior. Place its source, explicit mappings, dry-run output, and final migration report under a run-specific operational directory outside the Cadence repository, for example:

`/Users/max/.cadence/migrations/01a0251d6d50735582e93e9e40047b15/`

Retain those migration artifacts long enough for review and reproducibility. They are warranted operational migration evidence for this named persisted-data boundary, not a generalized provenance subsystem. Do not create a reusable migration framework.

### Like-for-like invariant

Enumerate the entire ledger and identify every persisted payload or document containing `CadenceState`, `Packet`, `PlannerDecision`, Executor report claims, Reviewer assessments, or another changed Cadence contract. Transform every occurrence, including historical intermediate states, capability-accepted values, step-completed values, and any duplicated accepted state representation. Discovery is source-driven: the ledger determines what exists and therefore what must be migrated.

Permitted transformations:

```text
packet constraint string
-> PacketConstraint(explicit ID, identical requirement)

Planner constraint string
-> PlannerConstraint(explicit ID, identical requirement)

sentence-shaped persisted constraint claim reference
-> corresponding namespaced obligation reference

separate report acceptance/constraint claim arrays
-> one obligation claim array, preserving every item and its order by category

separate Reviewer assessment arrays, if present
-> one assessment array, preserving every item and its order by category
```

For historical Planner constraints, first inventory every distinct exact requirement string across the entire ledger. Create a migration-local explicit ID mapping that covers exactly that discovered set. The same exact historical requirement must map to the same local ID everywhere; different requirements must not be merged. The plan does not prescribe or reconstruct the set. Missing, extra, duplicate, or ambiguous mapping entries are fatal.

No other semantic or lifecycle change is allowed.

Preserve exactly:

- packet title, repository, base, implementation context, outcomes, acceptance, commands, verification, constraint text, and authored order;
- pinned base SHA and workspace path;
- mutation authority;
- Planner decision value, rationale, evidence, safe next action, corrected approach, Human question/domain, and constraint requirement text/order;
- active Planner constraints;
- every outcome progress status, evidence string, and next action;
- latest checkpoint and continuity timestamp;
- Executor transition and complete implementation report;
- every false, overstated, or misleading Executor claim and its evidence;
- report summary, commit message, and regression-test evidence;
- candidate SHA;
- verification index and every verification result, command, output, exit code, timeout, and duration;
- every Reviewer decision present in every affected historical state, including every assessment, finding, summary, and Human field;
- accepted candidate SHA;
- review attempt and maximum review attempts;
- Planner failure count;
- Planner and Reviewer Human answers;
- active review findings;
- resume, repair, operator-instruction, and operator-instruction-pending facts;
- nulls, collection order, and all other persisted state values.

Do not clear `ExecutorTransition`, `PlannerDecision`, report claims, outcome progress, verification, or any misleading material to make the Reviewer more likely to reject.

### Equivalence proof

Before replacing the live ledger, produce canonical semantic projections of the original and migrated databases that normalize only the approved identity representation. Compare every row and every persisted payload event-for-event. Require exact equality for every semantic value and exact equality for all unaffected ledger columns and JSON paths.

The dry run must fail closed if:

- any packet or Planner constraint anywhere in the ledger lacks an explicit mapping;
- any mapping is unused, duplicated, ambiguous, or does not correspond to an exact discovered source requirement;
- requirement text changes;
- evidence changes;
- collection order changes unexpectedly;
- any lifecycle field changes;
- candidate or verification changes;
- any affected historical value cannot deserialize under the new typed contract;
- any ledger row, document, event, or affected payload is skipped;
- row count, entry identity, sequence, ordering, timestamp, status, or accepted/rejected meaning changes;
- a synthetic migration event is added.

Produce a path-level transformation report showing every changed JSON path, old representation, new reference, and confirmation that associated text/evidence is unchanged. Any changed path outside the approved representation set is fatal.

Before migration:

- copy `ledger.sqlite3` to the run-specific migration directory without overwriting any prior backup;
- record SHA-256 of the source ledger and backup;
- prove the backup hash matches the source hash;
- record complete table row counts and stable ordered row manifests for every ledger table;
- record workspace HEAD, tree SHA, clean status, candidate SHA, and verification state.

After transforming the copied ledger:

- deserialize every affected migrated value through the corresponding new production contract;
- rerun whole-ledger semantic equivalence against the original database;
- prove every historical ledger entry and document remains present, distinct, and ordered;
- prove every table row count and ordered row manifest remains identical apart from approved payload representation and any required declared contract-version field;
- record the migrated-copy hash;
- prove workspace HEAD is still candidate `586650a4b0ca2a1e621c6d3c0bbc5462b28bade2`;
- prove its tree is still `bdc367d98e2a80f2046581935a51ebc834b7a081`;
- prove the worktree remains clean.

Only after all proofs pass, stop any process using the run ledger, retain the original database as the immutable backup, and atomically place the fully migrated copy at the original ledger path. Reopen the migrated ledger and repeat contract deserialization, whole-ledger equivalence, database integrity, run identity/status, row-manifest, and workspace checks. Do not resume the run unless every post-swap proof passes.

## Fresh Reviewer Litmus

Install the newly validated Cadence tool before migrating and resuming the run. Confirm the installed assembly timestamps and inspect the installed contracts/prompts sufficiently to prove the installed tool contains:

- typed packet and Planner constraints;
- shared delivery-contract rendering;
- explicit obligation references;
- unified report claims;
- unified Reviewer assessments;
- the autonomous Reviewer framing already present in the baseline.

Resume the same run normally after the whole-ledger migration. Do not use packet replacement because that resets packet-derived lifecycle state and would invalidate the like-for-like test. Do not add an operator instruction that tells Reviewer what defects to find.

The fresh Reviewer must receive the same hostile factual situation:

- candidate `586650a4b0ca2a1e621c6d3c0bbc5462b28bade2`;
- unchanged incomplete candidate tree;
- complete green deterministic verification;
- completed outcome-progress claims;
- confident Executor implementation report and obligation claims;
- retained latest Planner decision, rationale, evidence, and the exact empty active Planner-constraint collection;
- existing lifecycle history;
- no accepted prior Reviewer decision for this candidate;
- the new explicit, coherent delivery contract.

The correct engineering result is `RequestChanges`. Known concrete counterexamples that remain in the candidate include:

- `apps/api/CaseBridge/Common/ErrorCodes.cs`: superseded Submission-status and standalone workflow-template error codes remain.
- `apps/api/CaseBridge/Modules/Forms/INVARIANTS.md`: stale submission-status-policy and lifecycle-reference invariants remain.
- `apps/api/CaseBridge/Modules/Retention/Domain/RetentionPolicyCatalog.cs`: stale `lifecycle-effect-history` retention category remains, with tests/UI still asserting it.
- `apps/ui/app/layouts/default/composables/useBreadcrumbs.ts`: stale `case-statuses` route labels remain.

These known examples establish why `Accept` is wrong; do not inject them through an operator recovery instruction or alter the Reviewer prompt specifically for this run. Reviewer must discover repository evidence through its normal role and tools.

Service Bus infrastructure must not be blanket-classified as legacy: retained notification, file-deletion, and Case operational-effect consumers still use generic Brighter/Azure Service Bus infrastructure. The packet requires removal of obsolete Form/Submission lifecycle publications, subscriptions, and configuration, not deletion of all Service Bus support. Reviewer and implementation tests must preserve this distinction.

The litmus is successful when:

- the run reaches a fresh Reviewer rather than failing state deserialization or obligation coverage;
- Reviewer does not infer `C-1` or other undeclared IDs;
- Reviewer returns `RequestChanges` with concrete candidate-grounded Critical/High findings;
- the decision is structurally accepted by Cadence and enters the existing repair lifecycle.

The litmus fails when:

- Reviewer returns `Accept`;
- structured output fails because IDs are hidden, ambiguous, missing, or mismatched;
- migration omitted or corrected hostile prior claims;
- candidate or workspace changed before review;
- run routing resets delivery state or sends Executor/Planner first without an existing lifecycle fact requiring that route;
- a Human question is used for repository-discoverable correctness.

Do not repair the Casebridge candidate after a successful `RequestChanges` decision as part of this Threadkeeper delivery. The purpose is to establish Reviewer behavior at the migrated boundary.

## Known Repository Context

- `src/Cadence/Domain/Packet.cs` currently stores packet constraints as `IReadOnlyList<string>`.
- `src/Cadence/Agents/Planner/PlannerDecision.cs` currently stores Planner constraints as `IReadOnlyList<string>`.
- `src/Cadence/DeliveryState.cs` persists `PlannerConstraints` as strings and derives a combined `Constraints` collection by concatenating packet and Planner strings with sentence equality.
- `src/Cadence/Capabilities/DeliveryCapabilityRequests.cs` currently separates `AcceptanceClaims` and `ConstraintClaims` and represents each claim as `ObligationClaim(Id, Evidence)`.
- `SubmitReportRequestValidator` currently validates constraint claim IDs against `state.Constraints`, so the entire sentence is silently treated as an ID.
- `ReviewDecision` currently separates outcome, acceptance, and constraint assessments.
- `ReviewerPolicies.ContractComplete` validates constraint assessment IDs against `state.Constraints`, creating the same hidden sentence-ID contract.
- Executor, Planner, and Reviewer prompt builders each independently render outcomes, acceptance criteria, packet constraints, and Planner constraints.
- Reviewer currently presents packet and Planner constraints in separate unlabeled bullet lists with no IDs while structured output requires exact IDs.
- In the retained run, Reviewer attempted `Accept`, then guessed `C-1` through `C-6` after the correction message said exact constraint IDs were required. The run failed after two invalid structured outputs; it did not reject the candidate.
- Candidate `586650a4` is an empty commit over `42b7b625`; both resolve to tree `bdc367d98e2a80f2046581935a51ebc834b7a081`.
- The retained run is `Failed` at ledger status level, but its latest accepted state still owns candidate and verification facts. Resume should reopen the same run and route by the migrated domain state, not by terminal status.
- `Program.ResumeAsync` ordinarily reads the latest accepted `CadenceState`; `resume --packet` replaces packet JSON before deserialization and then creates fresh packet-derived state. That replacement path is unsuitable for this litmus because it discards the hostile retained delivery facts.
- The ledger contains historical Cadence values from the complete run, including differing Planner decisions and active-constraint states. Every affected historical representation must be migrated in place within the copied database; none may be selected, skipped, promoted, or collapsed.
- Cadence package integration is local: `task check` runs `task prepare`, packs sibling Tandem packages, restores, formats/checks analyzers, tests, builds, and runs architecture checks.

## Inspect

Start with these ownership seams and inspect their direct callers, consumers, serialization, tests, and current dirty diffs before editing:

- `/Users/max/Sites/cadence/AGENTS.md`
- `/Users/max/Sites/cadence/src/Cadence/Domain/Packet.cs`
- `/Users/max/Sites/cadence/src/Cadence/Domain/PacketValidator.cs`
- `/Users/max/Sites/cadence/src/Cadence.Host/PacketReader.cs`
- `/Users/max/Sites/cadence/src/Cadence.Host/Program.cs`
- `/Users/max/Sites/cadence/src/Cadence/DeliveryState.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Planner/PlannerDecision.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Planner/PlannerDecisionValidator.cs`
- `/Users/max/Sites/cadence/src/Cadence/Capabilities/DeliveryCapabilityRequests.cs`
- `/Users/max/Sites/cadence/src/Cadence/Capabilities/DeliveryCapabilityValidators.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Reviewer/ReviewDecision.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Reviewer/ReviewDecisionValidators.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Reviewer/ReviewerPolicies.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Executor/ExecutorPrompts.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Planner/PlannerPrompts.cs`
- `/Users/max/Sites/cadence/src/Cadence/Agents/Reviewer/ReviewerPrompts.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/CoreDeliveryContractTests.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/PromptContractTests.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/HostBoundaryTests.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/RetainedWorkspaceTests.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/LifecycleFeatureProofTests.cs`
- `/Users/max/Sites/cadence/tests/Cadence.Tests/TestSupport.cs`
- `/Users/max/Sites/cadence/README.md`
- `/Users/max/Sites/cadence/examples/packet.md`
- `/Users/max/Sites/cadence/skills/packet-authoring/SKILL.md`
- `/Users/max/.cadence/plans/remove-superseded-case-paths.md`
- `/Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15/ledger.sqlite3`
- `/Users/max/.cadence/runs/01a0251d6d50735582e93e9e40047b15/workspace`

Use GitNexus impact analysis before editing every existing function, class, method, or shared contract required by the repository instructions. Warn on HIGH or CRITICAL impact and adapt safely. Run GitNexus change detection before any commit if a later Human explicitly requests one. Do not commit as part of this Threadkeeper run.

## Required Tests

### Packet contract

- Typed packet constraints parse from YAML through the production `PacketReader`.
- IDs and requirements normalize correctly.
- Blank IDs, blank requirements, duplicate IDs, null entries, and old string-shaped constraints fail clearly.
- Authored constraint order is preserved.
- Every checked-in example and operator packet named in scope validates under the new production reader.

### Planner contract and state

- `Proceed` accepts valid typed Planner constraints and replaces active constraints.
- Duplicate/blank IDs and blank requirements are rejected.
- Non-authorizing decisions cannot carry constraints.
- Non-authorizing decisions preserve previously active Planner constraints in state.
- Serialization and deserialization preserve typed Planner constraints exactly.

### Obligation catalog

- Catalog order is deterministic.
- Derived references are namespaced and unambiguous across kinds.
- Local IDs and linked outcome IDs are preserved.
- Packet and Planner ownership remains visible.
- Doctrine, findings, verification, reports, progress, checkpoints, Human answers, and operator instructions are absent.
- Catalog is derived rather than serialized as a second state field.

### Prompt composition

- All three roles receive the same rendered delivery-contract block.
- Every current obligation appears exactly once in that block.
- Every obligation has an explicit bracketed reference.
- Acceptance criteria retain their linked outcome reference.
- Role-specific context remains present but does not repeat the delivery contract.
- Recent autonomous-agent and ledger-epistemology instructions remain intact.

### Executor report

- Exact reportable-obligation coverage passes.
- Missing, duplicate, unknown, and blank-evidence claims fail clearly.
- Outcome completeness remains independently required.
- Review-repair materiality and checkpoint gates remain unchanged.
- Production capability binding exposes the new report shape.

### Reviewer decision

- Exact complete-catalog assessment coverage passes.
- Missing, duplicate, unknown, and blank-evidence assessments fail clearly.
- `Accept` requires every assessment satisfied and complete green verification.
- `RequestChanges` supports unsatisfied assessments and requires blocking findings with precise locations.
- `NeedsHuman` semantics remain unchanged.
- Fresh Reviewer conversation behavior remains unchanged.
- No test requires an arbitrary inspection count or tool sequence.

### Persistence and resume

- New typed state round-trips through the real accepted ledger value path.
- Ordinary resume works for new typed state across representative lifecycle phases.
- Old string-shaped state fails clearly rather than receiving a hidden compatibility conversion.
- Packet replacement remains semantically correct for state already using the new contract.
- Accepted candidate, verification, review, and publication invariants remain green.

### One-off migration

- Dry-run inventory and mapping cover every exact packet and Planner constraint requirement discovered anywhere in the ledger and reject unknown, missing, extra, duplicate, ambiguous, merged, or reordered transformations.
- Requirement and evidence changes fail equivalence.
- Unapproved field changes fail equivalence.
- Approved identity transformations pass.
- Every affected historical payload deserializes through the new contracts.
- The migrated copy preserves every ledger row, entry ID, sequence, timestamp, status, event distinction, document, and historical state while changing only approved representation.
- No synthetic migration event is appended.
- Atomic replacement retains the original ledger as an immutable backup and the reopened migrated ledger passes the same complete equivalence proof.
- Workspace HEAD, tree, and cleanliness remain unchanged.

Prefer focused tests at the authoritative owner plus existing real lifecycle proofs. Do not create a generalized migration framework or a mocked end-to-end test that claims to prove the live Reviewer result.

## Scope

- This is one coherent Cadence delivery: domain identity, derived presentation/coverage, deliberate state-contract break, exact migration of one retained run, and behavioral litmus.
- Do not modify Tandem.
- Do not repair the Casebridge candidate.
- Do not change the packet's substantive delivery intent.
- Do not add backward compatibility for other old runs or packets.
- Do not add a permanent Cadence migration subsystem or CLI command.
- Do not add proof DTOs, inspection receipts, prompt hashes, model attestations, or persisted obligation catalogs.
- Do not weaken validation merely to let the retained Reviewer output parse.
- Do not strengthen Reviewer grounding through arbitrary mechanical tool-use rules.
- Do not reinterpret Reviewer doctrine as delivery obligations.
- Do not infer that generic Service Bus support is obsolete without tracing retained consumers.
- Preserve unrelated dirty work throughout `/Users/max/Sites/cadence`.
- Leave the migrated run at the resulting accepted `RequestChanges` state; do not continue into candidate repair.

## Verification

Run from `/Users/max/Sites/cadence`:

- Focused tests for packet parsing/validation, Planner decision/state, obligation catalog, prompt composition, report validation, Reviewer validation/policy, persistence, retained workspace, and host resume while iterating.
- `task check`
- `task install`

`task check` is the final repository gate. It packs the current sibling Tandem packages through the existing local feed, restores, checks formatting and analyzers, runs the complete Cadence test suite, builds, and runs architecture checks.

After installation:

- Confirm installed Cadence and Tandem assembly timestamps.
- Inspect/decompile installed Cadence contracts and prompt construction sufficiently to prove the installed tool contains this delivery rather than an earlier build.
- Run `cadence validate /Users/max/.cadence/plans/remove-superseded-case-paths.md`.
- Validate any other operator packet changed by this delivery.
- Run the migration dry run and review its complete equivalence report.
- Inventory, back up, hash, migrate every affected value, validate the copied database, atomically replace the live ledger, reopen it, and re-prove the complete ledger exactly as specified above.
- Resume run `01a0251d6d50735582e93e9e40047b15` normally with no packet replacement and no operator instruction.
- Capture the terminal result, accepted Reviewer decision, latest accepted state, and Reviewer tool trace for assessment of the litmus.

Finally review the Cadence diff against the dirty starting baseline, not merely against `HEAD`. Confirm the delivery changed only the intended Cadence contract/prompt/tests/docs surfaces and preserved all unrelated existing work. Do not commit or push.

## Acceptance

Accept this Threadkeeper delivery only when all of the following are true:

- Constraint identity is explicit in packet and Planner domain contracts.
- The same derived obligation catalog owns prompt rendering and exact coverage expectations.
- Agents are shown every reference they must return.
- Requirement and lifecycle concepts remain correctly separated.
- No production compatibility or migration machinery was introduced.
- Full Cadence verification passes and the installed tool is proven current.
- Every affected value across the complete named ledger was migrated with exact event-for-event semantic equivalence and no curated removal, promotion, or rewriting of hostile claims or historical states.
- The retained workspace and candidate remained byte-for-byte unchanged.
- The fresh Reviewer litmus reached a structurally accepted decision rather than failing ID inference.
- The fresh Reviewer returned repository-grounded `RequestChanges` for the known-incomplete candidate.
- The run was not advanced into repair after that decision.

If complete whole-ledger migration cannot be performed without losing or changing any event, if exact event-for-event semantic equivalence cannot be proven, or if any Human-owned migration decision beyond the settled one-run like-for-like transformation emerges, stop without replacing the live ledger and report the exact blocker.
