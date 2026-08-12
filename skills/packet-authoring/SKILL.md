---
name: cadence-packet-authoring
description: Author a valid Cadence delivery packet with observable outcomes and repository-native verification.
---

# Cadence Packet Authoring

Create one Markdown file whose YAML frontmatter describes the delivery contract and whose body gives bounded implementation context.

Required frontmatter:

- `title`: concise nonblank delivery name.
- `repository`: absolute path or a path relative to the packet file.
- `base`: Git reference to prepare as the isolated workspace base.
- `outcomes`: ordered, nonempty list of unique nonblank `id` and nonblank `description` values.
- `verification`: ordered, nonempty list of exact nonblank commands supported by repository tooling.

Optional frontmatter:

- `constraints`: ordered exact requirements. Omit it or use `[]` when none apply.

Write outcomes as independently observable product or engineering results. Preserve command and constraint text exactly as Cadence should execute or apply it. Quote commands and constraints that YAML would otherwise interpret as values, including `true`, `false`, `null`, and numbers. Use the Markdown body for useful starting context, inspected seams, and scope boundaries, not for hidden requirements that should be outcomes or constraints.

Do not add unknown frontmatter fields, duplicate keys, duplicate outcome IDs after trimming, YAML anchors, aliases, tags, or multiple YAML documents. Confirm the repository directory exists; Cadence checks the Git base later while preparing the workspace.

Use `examples/packet.md` as the canonical shape.
