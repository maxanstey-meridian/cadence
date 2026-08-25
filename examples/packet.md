---
title: Document the Cadence packet contract
repository: ..
base: main
outcomes:
  - id: packet-contract
    description: Cadence users can author a delivery packet from product-facing documentation
acceptance:
  - id: packet-example
    outcome: packet-contract
    requirement: The checked-in example parses and documents every required packet field
commands:
  - label: format
    command: task format
verification:
  - label: check
    command: task check
constraints:
  - id: preserve-packet-order
    requirement: Preserve packet field and authored command order
---

## Known context

Packet field order and authored repository and verification command order are part of the public contract. Command and verification labels must be unique within their respective lists, use only valid Tandem tool-name segment characters (`A-Z`, `a-z`, `0-9`, `_`, or `-`), and keep the corresponding `run_command_<label>` or `run_verification_<label>` tool name at most 64 characters.

## Inspect first

Inspect the packet reader and host boundary tests:

- `README.md`
- `src/Cadence.Host/PacketReader.cs`
- `tests/Cadence.Tests/HostBoundaryTests.cs`
