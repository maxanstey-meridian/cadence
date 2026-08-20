---
title: Report YAML locations for TypeScript packet validation errors
repository: ../../tandem
base: main
outcomes:
  - id: validation-source-locations
    description: TypeScript packet schema validation problems report the YAML line and column of the value that failed validation
  - id: stable-validation-paths
    description: Existing TypeScript packet validation paths and error semantics remain unchanged
  - id: regression-coverage
    description: Automated tests cover source locations for top-level and nested validation failures
verification:
  - label: check
    command: task check
constraints:
  - Keep the change within the optional @tandem/packets package and its tests
  - Derive locations from the parsed YAML document rather than reparsing or searching source text
  - Preserve the existing PacketProblem and PacketFileError public API
  - Do not change C# packet behavior or the shared portable fixture contract unless parity requires it
---

Inspect the TypeScript packet parser and its existing Zod issue normalization before
choosing the implementation seam. The current parser validates the YAML node tree before
converting it to JavaScript and applying the caller-owned Zod schema. Reuse that parsed
document to associate each Zod issue path with the corresponding YAML node range.

This is a demo run against Tandem itself. Keep the implementation minimal, add focused
regression tests, and let the complete Tandem repository gate prove package and consumer
behavior.
