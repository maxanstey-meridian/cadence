---
title: Document the Cadence packet contract
repository: ..
base: main
outcomes:
  - id: packet-contract
    description: Cadence users can author a delivery packet from product-facing documentation
verification:
  - task check
constraints:
  - Preserve packet field and verification command order
---

## Known context

Packet field order and authored verification command order are part of the public contract.

## Inspect first

Inspect the packet reader and host boundary tests:

- `README.md`
- `src/Cadence.Host/PacketReader.cs`
- `tests/Cadence.Tests/HostBoundaryTests.cs`
