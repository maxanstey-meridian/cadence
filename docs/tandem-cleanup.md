# Tandem slop cleanup: Cadence adaptation (W2-8)

Branch `slop/tandem-cleanup`, for the owner to review before merging.

## How Cadence references Tandem

`TandemVersion` in `Directory.Build.props` pins the published Tandem 0.3.0 from nuget.org, the first release with the cleaned-up surface.

## Resume: product decision

**Before.** `cadence resume <run-id> [--packet p | --instruction "..."]` read the run's latest accepted `CadenceState`, rebuilt a resume state (fresh model sessions, stale mutation authority closed, optional operator instruction or replacement packet) and called `SqliteLedgerStore.ReopenRunAsync`. That set the *same* ledger run back to `Running` and continued in the same run directory under the same run ID. An instruction resume also wrote the instruction into the reopened run before loading configuration.

**What users actually get from it.** They continue a stopped, failed or crashed delivery in its retained workspace, with its progress, and can redirect it with an instruction or a corrected packet. This is a real product outcome, not a durability artefact. The only durability part was keeping one run ID by reopening the run. Tandem has removed that (D1: "durability is a tombstone; runs are in memory").

**Chosen: (a) fresh run seeded from the journal.** This keeps the user outcome and adds no machinery. Removing the feature (b) would drop real recovery value.

- `resume <prior-id>` reads the prior run's latest accepted `CadenceState` (read-only), validates it, loads configuration, and only then creates a **new run** with a new ID under `runs/<new-id>/`.
- The new run's first journal entry is step `resume` (outcome `resume.accepted`, "Resumed from run '<prior-id>'."). It carries the seeded state as its accepted value, so the new run can be published or resumed even if it stops at once. It also keeps the operator instruction. This replaces the old `resume.operator-instruction` entry.
- The workspace is not moved. The new run works in `runs/<origin-id>/workspace`, which is where the state's `WorkspacePath` points. The old "workspace must be this run's directory" check is now "workspace must be `<home>/runs/<some-run>/workspace`" (`Program.IsRunWorkspace`), so chains of resumes work. Workspaces outside the home are still rejected.
- The prior run is never modified. It keeps its status (`Ready`, `Failed`, `Interrupted`, or `Running` if its process died) as history.

**User-visible changes**

1. Resume prints `Resuming run <prior> as run <new>.`, and the terminal header shows the new ID. `publish` and a later `resume` take the **new** ID. `publish <prior-id>` still publishes whatever the prior run accepted.
2. `runs/` gains one directory per resume. It holds only `ledger.sqlite3`. The workspace stays in the original run's directory.
3. A packet, workspace or configuration problem now fails before anything is written. The old behaviour persisted an instruction into the reopened run before a configuration failure. Now the prior run is untouched and you re-run the command.
4. Agents' ledger tools (`read_ledger`/`search_ledger`) see only the new run's journal. Facts carried forward live in the seeded state ("facts in state"). Earlier raw tool and command output in the prior run's journal is no longer browsable by agents in the resumed run.
5. `resume --help` now reads "Continue a previous run's delivery in a new run".

**Tests (red first).** In `HostBoundaryTests`:
- `Resume_starts_a_fresh_run_seeded_from_a_prior_run_in_every_status` (x6 statuses): the prior status and state are unchanged; the new run has the `resume` seed with the instruction, retains the packet commands and workspace, and ends `Failed`.
- `Resume_packet_override_…` reads the new run.
- `Cross_repository_packet_rejection_starts_no_new_run`.
- `Resume_rejects_a_ledger_bound_to_another_workspace` also asserts that no new run is created.
- `Resume_accepts_workspaces_of_any_run_in_the_home_and_nothing_else`.

In `OperatorInstructionResumeTests`:
- `Configuration_failure_starts_no_run_and_leaves_the_prior_run_untouched` replaces "instruction accepted before configuration failure".
- The cancellation test now covers `RecordResumeAsync`.
- `Instruction_resume_reopens_every_persisted_status…` is deleted; the host test above covers every status end to end.

## Other adaptations

- `AgentMessageOutcome` → `OperationOutcome`; `StructuredOutputProblem` → `ValidationProblem`.
- `PacketFile.Read` → `await PacketFile.ReadAsync`, so `PacketReader.Read` becomes `ReadAsync` (and `validate` becomes async).
- `TerminalPipelineRunOptions.Persistence` is removed: the ledger observer is passed as `PipelineRunOptions`' observer.
- The resume `--packet` path reads accepted values with `SqliteLedgerStore.ReadAcceptedAsync` instead of the removed generic stream read. Its error messages no longer quote a journal sequence number.
- `OpenAIClientOptions.RetryPolicy = new ClientRetryPolicy(0)`: Tandem's `StreamRetryChatClient` is now the only retry layer. Previously a persistent 503 could cost up to 16 requests per model call. There is no Cadence test for this, because `ConfiguredChatClients` has no transport seam; Tandem's A7 tests measure it.
- Tests: `Tool*` types live in `Tandem`; `ToolEffect` and `ToolInvocationStatus` are enums, not strings; `PipelineInspection` lost its Mermaid/DOT constructor arguments.

## Compatibility notes

- **Ledger files:** Tandem's ledger schema is now version 2 and refuses version-1 files. Runs created by the published Cadence cannot be resumed or published by this build. There is no migration; finish or publish them with the old build first.
- **Packets:** Tandem packets now use YAML 1.2 core scalars, so values like `yes` or `1_000` change meaning. The checked-in example and test packets are unaffected.
- **Plain terminal output format:** this changed (Tandem T-lane). Cadence doesn't parse it.

## Owner checklist

1. Review this branch, especially the resume decision above.
2. When Tandem is published, switch to the published version (see above) and run `task check`.
3. The test suite needs `TAVILY_API_KEY` set to any non-blank value; without it, 20 tests fail on `main` too. This was already the case before this branch.
