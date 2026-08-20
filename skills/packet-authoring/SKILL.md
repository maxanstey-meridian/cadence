---
name: cadence-packet-authoring
description: Turn agreed delivery intent into a self-contained Cadence packet that can execute from its clean declared Git base.
---

# Cadence Packet Authoring

Turn decisions already reached with the human into one reviewable Cadence packet. Use the conversation context already available; do not make the human restate it through a questionnaire.

The packet is the complete durable handoff to Executor, Planner, and Reviewer. It states desired outcomes, settled decisions, constraints, verification, and bounded repository context. It is not a speculative implementation plan or Cadence runtime configuration.

## Central Invariant

A competent Executor, starting only from the packet and a clean workspace prepared from the declared `base`, can understand the desired result, inspect a bounded set of real code, obtain Planner approval, implement the delivery, and prove it without recovering missing human context.

The packet must not depend on:

- conversation history;
- the author's dirty worktree;
- untracked or ignored files;
- a local plan or specification absent from `base`;
- files available only on another branch;
- unstated product or engineering decisions.

## Before Writing

1. Recover the agreed intent from the conversation:
   - desired result;
   - decisions already made;
   - reasons that materially constrain implementation;
   - explicit constraints and exclusions;
   - observable completion evidence;
   - unresolved questions;
   - repository evidence already gathered.
2. Read the target repository's instructions.
3. Resolve the target repository and declared base branch.
4. Inspect enough checked-in implementation context to identify real owner files and make the outcomes concrete.
5. Discover exact implementation and verification commands from tooling checked in on `base`, such as `Taskfile.yml`, `package.json`, solution files, or documented repository workflows. Never invent a command.
6. Resolve every required external dependency or service integration before authoring:
   - name exact package IDs and versions already present on `base`, or the exact version to add;
   - name the exact supported API, provider type, extension method, configuration options, and required environment variables when those details constrain implementation;
   - verify those facts against checked-in lockfiles or package metadata, locally installed package documentation or assemblies, or authoritative upstream documentation/source;
   - include the verified facts in the packet so Executor and Planner do not have to rediscover them or guess;
   - if exact dependency facts cannot be verified, treat that as a blocking packet-authoring gap rather than delegating open-ended package discovery to Executor.
7. Classify every meaningful unknown:
   - **repository-discoverable**: Executor can resolve it through bounded inspection of named owner files or their direct collaborators;
   - **Planner-owned**: engineering direction, architecture, repository procedure, verification strategy, or scope interpretation that Planner can decide from repository evidence;
   - **human-required**: product, UX, business, security, permission, tenancy, data, migration, legal, or compliance intent;
   - **blocking**: implementation cannot begin faithfully until the human supplies a decision.
8. Run the Executor-fit and run-size checks below before drafting.

Do not encode an assumption as agreed intent. If a human-required or blocking decision changes what must be delivered, stop and ask the smallest necessary question.

## Authoring Standard

Write for a capable senior coding agent, not a literal task runner.

Include decisions whose omission would invite a materially different implementation. Leave repository-discoverable details for Executor to inspect. The packet should constrain outcomes and important boundaries, not dictate every class, helper, test name, or edit sequence.

### Simplicity and Replacement Discipline

Simplicity is a default acceptance boundary, not an optional style preference. Author the smallest delivery that replaces or changes the requested behavior at its existing owner.

- When the human asks to replace or remove behavior, require the old path to be removed. Do not permit an adjacent `v2`, parallel implementation, feature flag, adapter, alias, fallback, or second source of truth unless the human explicitly requested coexistence.
- Do not add backward compatibility, migration, dual-read/write behavior, legacy deserialization, or deprecation scaffolding without a concrete persisted-data, shipped-consumer, or rollout requirement stated by the human.
- Reject provenance theatre: evidence DTOs, receipts, hashes, manifests, audit trails, ledgers, copied assessments, model self-attestation, or other machinery that records claims about facts already owned by production state. Require provenance only when a real external trust boundary or named consumer needs it. Tests and reviewers inspecting production state are not consumers that justify a second proof model.
- Do not turn sequential local state into a generalized workflow, event history, revision protocol, or state machine. Do not add abstractions, seams, helpers, wrappers, ports, or dependencies merely to make a small change look architecturally complete.
- Do not harden against impossible internal states or speculative future failures. Defensive checks must protect a plausible failure at an actual input, persistence, concurrency, process, network, filesystem, security, or publication boundary.
- Do not preserve removed concepts under new names. If correctness still needs part of the old mechanism, state the exact invariant and retain only the smallest state or check that owns it.
- Distinguish behavior being removed from correctness boundaries that remain. Reviewer must be able to reject both a compatibility-preserving non-replacement and an overcorrection that deletes real safety.
- Prefer direct production-path behavior and aggregate verification over prompt wording, mock choreography, validator ceremony, implementation-detail assertions, or redundant proof objects.

These are standing defaults. A packet should state task-specific exceptions only when the human explicitly chose them; it should not invite Executor or Planner to rediscover or negotiate an exception.

Good specificity:

- the behavior that must exist;
- settled ownership and dependency direction;
- compatibility or migration requirements explicitly chosen by the human;
- which existing mechanism must be reused;
- exact public names or contracts the human chose;
- meaningful exclusions;
- exact verification commands;
- external API facts already researched and approved.

Bad specificity:

- speculative file lists presented as mandatory;
- pseudocode for implementation that repository inspection should determine;
- generic "best practice" instructions;
- exhaustive prohibitions responding to one imagined failure mode;
- commands copied from memory rather than repository tooling;
- instructions for Cadence routing, checkpointing, sessions, or review protocol.

## Clean-Base Grounding

Cadence prepares an isolated workspace from `base`. The author's current worktree is not execution context.

Before including any repository reference:

- verify `base` exists as a Git ref;
- verify every named file and directory exists on `base`, not merely in the worktree;
- verify every local document the packet relies on is tracked and available on `base`;
- verify every named implementation and verification command is supported by tooling available on `base`;
- inspect base content when the worktree version differs.

Use Git-aware inspection such as `git cat-file -e <base>:<path>` or `git show <base>:<path>` when appropriate. Do not reference an untracked plan and do not copy a plan dependency into the packet as a pointer. Incorporate the settled decisions needed for execution directly into the packet.

## Executor-Fit Check

Before writing, answer yes to all of these:

1. Can Executor form a credible first approach after reading the packet and inspecting a small set of named owner files?
2. Are repository-discoverable unknowns locally bounded rather than invitations to search the whole repository?
3. Are settled technical decisions present in the packet when they materially constrain a correct implementation?
4. Can Planner review the approach without reconstructing missing human context?
5. Can Reviewer prove each outcome using repository evidence and the declared verification commands?

If not, perform more bounded recon, add the missing settled context, split the work, or ask the human. Do not hand off a discovery prompt disguised as a packet.

## One-Run Fit

A packet must describe one coherent delivery with compatible outcomes, constraints, and verification.

Prefer roughly 2-6 outcomes. Split the work when it spans:

- independently releasable phases;
- unrelated bounded contexts or infrastructure systems;
- separate safety or approval boundaries;
- more work than one Executor can reasonably implement, verify, and repair in one run;
- outcomes that need materially different repository context or verification.

Mechanical sibling outcomes may remain together. Do not preserve a giant packet merely because all work belongs to the same initiative.

## File

Write the packet to:

```text
.cadence/packets/<short-kebab-slug>.md
```

Create the directory when needed. The location is a repository convention, not runtime identity; do not add timestamps, run IDs, or other execution bookkeeping.

Write the file but never run `cadence run` as part of authoring. Only the human initiates a model run.

## Frontmatter

Use this shape:

```yaml
---
title: Implement account registration
repository: /absolute/path/to/repo
base: main
outcomes:
  - id: registration
    description: Users can create an account with valid registration details
  - id: duplicate-email
    description: An existing email address is rejected without creating another account
commands:
  - task generate
acceptance:
  - id: registration-valid
    outcome: registration
    requirement: A focused test proves valid details create an account
  - id: duplicate-email-rejected
    outcome: duplicate-email
    requirement: A focused test proves an existing email creates no additional account
verification:
  - label: check
    command: task check
constraints:
  - Preserve the existing authentication boundary
---
```

Required frontmatter:

- `title`: concise nonblank delivery name.
- `repository`: absolute path to the target Git repository.
- `base`: Git reference used to prepare the isolated workspace.
- `outcomes`: ordered, nonempty list of unique nonblank `id` and nonblank `description` values.
- `acceptance`: ordered, nonempty list of unique nonblank `id`, an `outcome` referencing a declared outcome ID, and a nonblank concrete `requirement`. Every outcome must have at least one criterion.
- `verification`: ordered, nonempty list of entries with a nonblank `label` (a short kebab-case slug used as the tool name suffix, e.g. `unit-tests`, `lint`) and a nonblank `command` string supported by repository tooling.

Optional frontmatter:

- `commands`: ordered exact repository commands needed during implementation. Omit it or use `[]` when none apply.
- `constraints`: ordered exact requirements. Omit it or use `[]` when none apply.

Quote strings containing YAML-significant punctuation, especially `: `, `#`, braces, brackets, or scalar-looking values such as `true`, `false`, `null`, and numbers.

The four contract categories have distinct jobs: outcomes describe delivered capability;
acceptance criteria state independently reviewable behavioral or test proof obligations;
constraints bound every valid implementation; verification entries are exact deterministic
commands Cadence runs. Never move a proof obligation into prose merely because a green aggregate
command may exercise it.

### Outcomes

- Describe independently observable product or engineering results.
- State what must be true, not implementation steps.
- Use short stable IDs and preserve meaningful authored order.
- Do not add passing tests as an outcome; `verification` owns mechanical proof.
- Do not hide preservation requirements or unresolved decisions in descriptions.
- Do not bundle unrelated implementation, migration, UI, cleanup, and release phases into one outcome.

### Commands

- Include only commands the Executor must run to implement the packet, such as migration, contract, or client generation.
- Use the exact command and fixed arguments supported by checked-in repository tooling on `base`.
- Prefer checked-in task or package scripts over raw framework commands when they exist.
- Commands run from the prepared repository root and may modify the isolated workspace.
- Cadence exposes them only to Executor after Planner authorizes mutation.
- Do not include exploratory shells, executable families such as bare `dotnet` or `npm`, destructive host commands, or Git commands.
- Do not duplicate verification commands unless implementation genuinely requires an earlier invocation.
- Generated artifacts must use the declared repository command, never hand editing.

### Verification

- Each entry has a `label` (short kebab-case slug, e.g. `unit-tests`, `lint`, `typecheck`) and a `command` (exact shell command).
- The `label` becomes the executor and reviewer tool name: `run_verification_<label>`. Choose a descriptive label that helps the model understand what the command does.
- Use exact commands supported by checked-in repository tooling on `base`.
- Prefer the repository's established aggregate gate when it materially proves the outcomes.
- Add focused commands only when the aggregate gate does not prove a material behavior.
- Verification commands must be read-only because Cadence reruns them against the captured candidate and rejects candidate mutation.
- Commands run from the prepared repository root.
- Preserve entry order, labels, and command text exactly.

### Constraints

- Include only explicit requirements that meaningfully restrict an otherwise valid implementation.
- Preserve authored constraint meaning and order.
- Make constraints local and testable where possible.
- Do not invent constraints from personal preferences.
- Omit generic advice such as "follow best practices", "keep it clean", or "make minimal changes".
- Do not stack broad negative claims that verification cannot prove.
- Do not duplicate outcomes as constraints or include contradictory absolutes.

## Body

Use only the sections that earn their keep. The body may include:

- `## Known context`: settled facts and decisions needed to implement correctly;
- `## Inspect first`: a small list of checked-in owner files sufficient for the initial approach;
- `## Unknowns and routes`: meaningful repository-discoverable or Planner-owned unknowns and how to resolve them;
- `## Scope boundaries`: explicit exclusions and release or safety boundaries;
- `## Implementation constraints`: settled technical decisions, invariants, and nearby patterns that must be preserved;
- Keep the body to bounded implementation context: architecture, ownership, initial inspection seams, and non-obvious rationale. Put every required behavioral or test scenario in structured `acceptance`; do not hide proof obligations in prose verification notes.

The body should:

- make the packet self-contained;
- distinguish confirmed facts from suggestions;
- name owner files rather than every possible collaborator;
- include settled technical detail when omitting it would force invention;
- leave bounded local facts for Executor to inspect;
- route engineering direction to Planner and human-owned decisions to the human.

The body must not:

- refer to conversation history as execution context;
- depend on another local plan or absent specification;
- prescribe speculative files, APIs, or designs as facts;
- duplicate frontmatter;
- hide required outcomes or constraints in prose;
- explain Cadence's runtime protocol;
- instruct agents to commit, push, merge, reset, checkout, stash, or clean;
- contain run IDs, workspace paths, mutation-authority instructions, review formats, or other runtime bookkeeping.

Example:

```markdown
## Known context

Registration uses the existing authentication composition. Duplicate email addresses
must not create another account.

## Inspect first

- `src/auth/registration.ts`
- `src/auth/auth.module.ts`

## Scope boundaries

Email verification is outside this delivery.
```

## Hard Rejects

Do not write or present a packet as ready when:

- the desired behavior is not observable;
- the repository or base cannot be resolved;
- a required reference is absent from `base`;
- execution depends on an untracked or external local document;
- a human-required decision is unresolved;
- the work does not fit one coherent run;
- implementation requires a repository workflow that is described in prose but absent from `commands`;
- implementation requires a new or changed external package or service integration whose exact package ID, version, supported API, and configuration have not been verified and stated;
- verification commands are invented, unavailable, or materially insufficient;
- Executor would need broad discovery before it could propose a first approach;
- replacement semantics are ambiguous enough to permit an adjacent implementation while retaining the old path;
- the packet invites compatibility, migration, provenance, ceremony, speculative abstraction, or defensive hardening without an explicit requirement and real owning boundary;
- the packet protects removed machinery more strongly than the behavior the human actually requested;
- Reviewer could not distinguish completion from a plausible but wrong implementation.

Report the smallest blocker instead of writing around it.

## Mechanical Preflight

Before presenting the packet:

1. Parse and schema-validate it using a parser-only Cadence command when the installed CLI provides one. Never use `cadence run` as validation because that creates a run and may invoke models.
2. If no parser-only command exists, do not claim parser validation occurred. Perform all remaining checks and explicitly report that parser-only validation is unavailable.
3. Resolve `repository` relative to the packet and confirm it is the intended Git repository.
4. Confirm `base` resolves to a commit.
5. Verify every path named as execution context exists on `base`.
6. Verify local documents needed for execution are tracked on `base`; otherwise inline the settled decisions or reject the packet.
7. Verify every command against checked-in tooling on `base` without executing destructive or model-backed commands.
8. Check YAML-significant strings, duplicate keys, duplicate outcome IDs after trimming, unknown fields, anchors, aliases, tags, and multiple documents.
9. Check that outcomes are observable, constraints are compatible, and verification can materially prove them.
10. Check that the packet fits one run and that initial inspection is bounded.

Do not reproduce Cadence's parser in a custom script. Missing parser-only validation is a Cadence tooling gap, not permission to start a run or claim success.

## Final Review

Re-read the packet as the only handoff available inside a clean workspace. Ask:

- Could a competent but literal Executor satisfy this packet while missing the human's intent?
- Does the packet contain a product or engineering decision the human never made?
- Does any sentence rely on a file, branch, worktree change, or conversation unavailable from `base`?
- Can Executor inspect the named files and form a first approach without broad archaeology?
- Is this genuinely one run, or an initiative that should be split?
- Can Reviewer prove each positive outcome without circular evidence?
- Does replacement remove the old path rather than add a neighboring version or compatibility route?
- Does every new abstraction, compatibility path, provenance record, or hardening check protect a stated requirement at a real boundary?
- Did the packet preserve only actual correctness invariants rather than the ceremony of the previous implementation?
- Did the author accidentally prescribe an implementation choice that repository inspection should own?

Repair mechanical and wording problems that do not change human intent. Ask the human when the repair would require a new human-owned decision.

## Finish

Report the written path, base, outcome IDs, repository commands, verification commands, grounding checks, and remaining assumptions or validation gaps:

```text
Packet: .cadence/packets/account-registration.md
Base: main

Outcomes:
- registration
- duplicate-email

Commands:
- task generate

Verification:
- check: task check

Grounding:
- repository and base resolved
- all referenced paths exist on base
- verification command exists on base

Assumptions: None
Validation gaps: Cadence has no parser-only validation command
```

Do not offer `cadence run` as ready when a hard reject remains.

## Do Not

- Do not invoke another LLM to reinterpret the conversation.
- Do not ask the human to refill information already present in context.
- Do not choose unresolved product, UX, security, permission, data, migration, legal, or compliance behavior.
- Do not run Cadence automatically.
- Do not add fields outside the Cadence packet contract.
- Do not import Lathe packet fields, queue concepts, campaigns, expected surfaces, or driver instructions.
- Do not reproduce Cadence's parser or validation rules in custom scripts.

Use `examples/packet.md` as the canonical schema shape, not as a substitute for the grounding and preflight workflow above.
