---
name: cadence-packet-authoring
description: Turn an agreed implementation conversation into a grounded Cadence delivery packet.
---

# Cadence Packet Authoring

Turn decisions already reached with the human into one reviewable Cadence packet. Use the context already present in the conversation; do not make the human restate it through a questionnaire.

The packet is the durable statement of desired outcomes, constraints, verification, and useful implementation context. It is not an implementation plan or Cadence runtime configuration.

## Before Writing

1. Recover the agreed intent from the conversation:
   - desired result;
   - decisions already made;
   - explicit constraints;
   - unresolved questions;
   - repository evidence already gathered.
2. Read the target repository's instructions.
3. Confirm the target repository and current base branch.
4. Inspect only enough implementation context to make the outcomes concrete.
5. Discover the real repository verification command from checked-in tooling such as `Taskfile.yml`, `package.json`, solution or project configuration, or the repository's documented workflow. Do not invent a command.

If product, UX, security, permission, data, migration, legal, or compliance intent remains unresolved and changes what must be delivered, stop and ask the human. Do not encode an assumption as agreed intent.

## File

Write the packet to:

```text
.cadence/packets/<short-kebab-slug>.md
```

Create the directory when needed. The location is a repository convention, not runtime identity; do not add timestamps, run IDs, or other execution bookkeeping.

Write the file but do not run `cadence run` unless the human explicitly asks.

## Frontmatter

Use this shape:

```yaml
---
title: Implement account registration
repository: ../..
base: main
outcomes:
  - id: registration
    description: Users can create an account with valid registration details
  - id: duplicate-email
    description: An existing email address is rejected without creating another account
verification:
  - task check
constraints:
  - Preserve the existing authentication boundary
---
```

Required frontmatter:

- `title`: concise nonblank delivery name.
- `repository`: absolute path or a path relative to the packet file.
- `base`: Git reference to prepare as the isolated workspace base.
- `outcomes`: ordered, nonempty list of unique nonblank `id` and nonblank `description` values.
- `verification`: ordered, nonempty list of exact nonblank commands supported by repository tooling.

Optional frontmatter:

- `constraints`: ordered exact requirements. Omit it or use `[]` when none apply.

### Outcomes

- Describe independently observable product or engineering results.
- State what must be true, not implementation steps.
- Use short stable IDs and preserve meaningful authored order.
- Do not add passing tests as an outcome; `verification` owns mechanical proof.
- Do not hide preservation requirements or unresolved decisions in descriptions.

### Verification

- Use exact commands supported by checked-in repository tooling.
- Prefer the repository's established aggregate gate when it proves the outcomes.
- Preserve command text, order, and intentional duplicates exactly.
- Quote strings that YAML would otherwise interpret as values, including `true`, `false`, `null`, and numbers.

### Constraints

- Include only explicit requirements that meaningfully restrict an otherwise valid implementation.
- Preserve constraint text and order exactly.
- Do not invent constraints from personal preferences.
- Omit generic advice such as "follow best practices", "keep it clean", or "make minimal changes".

## Body

Use the Markdown body for context that helps Executor, Planner, and Reviewer understand the work:

- relevant facts from the conversation;
- useful starting points found during repository inspection;
- settled scope boundaries and explicit exclusions;
- rationale needed to understand an agreed decision.

Do not duplicate frontmatter, prescribe speculative implementation details, hide required outcomes or constraints in prose, or explain Cadence's runtime protocol.

Example:

```markdown
The registration decisions were agreed in the preceding design discussion.

Inspect the existing authentication composition before choosing the implementation
seam. Email verification is outside this packet.
```

## Final Review

Before presenting the packet, re-read it as an execution handoff and repair mechanical or wording problems that do not change the human's intent.

Confirm that:

1. The packet describes the result agreed in the conversation.
2. Every outcome is independently observable and belongs to the agreed scope.
3. No product or engineering decision was silently invented.
4. Constraints are explicit, compatible, necessary, and locally meaningful.
5. Verification commands exist in checked-in repository tooling and run from the repository root.
6. The body distinguishes known facts from suggestions and contains no speculative design presented as fact.
7. `repository` resolves to the intended target and `base` names the intended current branch.
8. YAML-ambiguous strings are quoted where necessary.
9. There are no unknown fields, duplicate keys, duplicate outcome IDs after trimming, YAML anchors, aliases, tags, or multiple YAML documents.
10. Repetition, generic advice, implementation-plan detail, and runtime instructions have been removed.

Ask two adversarial questions:

- Could a competent but literal executor satisfy this packet while missing what the human intended?
- Does this packet contain a decision the human never made?

If either answer is yes, repair the packet or ask the human for the missing decision before finishing.

## Finish

Report the written path, outcome IDs, verification commands, and any remaining assumptions:

```text
Packet: .cadence/packets/account-registration.md

Outcomes:
- registration
- duplicate-email

Verification:
- task check

Assumptions:
- None
```

If an unresolved human decision blocks a faithful packet, do not write around it. Report the smallest required decision instead.

## Do Not

- Do not invoke another LLM to reinterpret the conversation.
- Do not ask the human to refill information already present in context.
- Do not create an implementation plan or choose unresolved product behavior.
- Do not run Cadence automatically.
- Do not add expected file surfaces, queue metadata, runtime IDs, or execution mechanics.
- Do not reproduce Cadence's parser or validation rules in custom scripts.

Use `examples/packet.md` as the canonical shape.
