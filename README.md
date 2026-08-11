# Cadence

Cadence is a single-run coding pipeline built as an external Tandem consumer.

```text
Packet
  -> isolated workspace
  -> Executor <-> Planner
  -> candidate capture
  -> deterministic verification
  -> Reviewer
  -> accepted SHA ready for Human review
```

Cadence does not implement campaigns, generated repair packets, queues, daemons,
cross-run convergence, or automatic merge.

## Prepare Tandem

Until Tandem packages are published, pack the local repository into Cadence's
ignored package feed:

```sh
./scripts/pack-tandem.sh
```

Override `TANDEM_REPOSITORY` or `TANDEM_VERSION` when required. Cadence references
`Tandem`, `Tandem.Advanced`, and `Tandem.Generators` as packages; it has no Tandem
source-project reference.

## Configure

Cadence reads `$CADENCE_HOME/config.json`, defaulting to `~/.cadence/config.json`:

```json
{
  "gitTimeoutSeconds": 120,
  "reviewerDoctrineFile": "reviewer-doctrine.md",
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
      "checkpointAtPercent": 80
    },
    "planner": {
      "provider": "local",
      "model": "planner-model",
      "contextWindowTokens": 200000,
      "maxOutputTokens": 32000,
      "checkpointAtPercent": 80
    },
    "reviewer": {
      "provider": "local",
      "model": "reviewer-model",
      "contextWindowTokens": 200000,
      "maxOutputTokens": 32000,
      "checkpointAtPercent": 80
    }
  }
}
```

`reviewerDoctrineFile` is required. Relative paths resolve against the directory
containing `config.json`. Cadence loads the file once during run setup, rejects a
missing or blank file, and binds every review and publication candidate to the
SHA-256 of the exact loaded bytes. The doctrine body is sent to Reviewer but is not
stored in `CadenceState`.

## Packet

```markdown
---
title: Implement example
repository: /absolute/path/to/repository
base: main
outcomes:
  - id: example
    description: Deliver the requested behavior
verification:
  - dotnet test
constraints:
  - Preserve the public API
---

Inspect the relevant implementation and tests before choosing the change surface.
```

At least one outcome and one verification command are required.
Packets whose outcomes already hold may produce an allow-empty candidate commit;
they still pass complete verification and Reviewer inspection.

## Run

```sh
dotnet run --project src/Cadence.Host -- run packet.md
```

Use `--publish` to publish after Reviewer acceptance, or publish later with the
printed run ID:

```sh
dotnet run --project src/Cadence.Host -- publish <run-id>
```

Publication pushes exactly the Reviewer-accepted candidate SHA to an isolated
`cadence/...` branch. It does not modify the source working tree or merge.

## Role Tools

| Role | Repository tools | Lifecycle tools | Verification |
| --- | --- | --- | --- |
| Executor | Read-only inspection; workspace mutation only while Planner-authorized | `ask_planner`, `update_outcomes`, `write_checkpoint`, `submit_report` | Receives fixed `run_verification_N` commands for packet verification; ordinary mutation tools remain conditional |
| Planner | Read-only repository inspection | Typed Planner decision | Reads only; no mutation and no packet command execution |
| Reviewer | Read-only files plus `git_changed_files` and `git_diff` | Typed review decision | Receives the same fixed `run_verification_N` commands and must run all of them |

Packet verification commands are mapped to fixed `run_verification_N` tools; agents
do not receive arbitrary shell access. The deterministic verification stage remains
authoritative. Reviewer `Accept` requires all reruns green. `RequestChanges` may
report a red rerun with exact command/result evidence because Tandem does not expose
failed invocation details to Cadence's output-acceptance policy.

## Verify

```sh
dotnet test Cadence.slnx
dotnet tool run csharpier check .
```

See `PLAN.md` for the lifecycle contract and behavioral proof list.
