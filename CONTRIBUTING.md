# Contributing to EMD

## Test-driven development is mandatory

All code changes are developed test-first using the **Red-Green-Refactor**
cycle:

1. **Red** — write a test that describes the desired behavior and run it.
   Confirm it fails for the expected reason, not because of a typo or missing
   fixture.
2. **Green** — write the minimum production code needed to make the test pass.
3. **Refactor** — improve the design while keeping the test suite green.
4. **Commit** — commit the test and the implementation together.

A pull request that changes behavior without a corresponding test will not be
merged. "I will add tests later" is not an accepted development plan.

### Required test patterns

- **Bug fixes** start with a regression test that reproduces the bug.
- **New tool adapters** start with a contract test against the pinned tool
  version (FreeCAD 26.3, KiCad 10.0.8, ...), plus a fixture project.
- **Schema changes** add a valid example and at least one negative fixture that
  the schema must reject.
- **Simulation changes** add deterministic assertions: fixed seed, fixed step,
  no wall-clock dependence, and record/replay where relevant.
- **Cross-domain rules** add one passing case and one failing case per
  diagnostic code (`EMD-*`).
- **Generated artifacts** add a golden artifact or a structural comparison;
  avoid brittle byte-for-byte checks where the tool embeds timestamps.

### Test layers

| Layer | Location (planned) | Purpose |
|---|---|---|
| Unit | `tests/EMD.Core.Tests` | graph, units, validation, build planning |
| Adapter contract | `tests/EMD.Adapters.Tests` | JSON-RPC protocol, capability probing, fixtures |
| Integration | `tests/EMD.Integration.Tests` | headless FreeCAD/KiCad/MechMaker/OMSimulator runs |
| End-to-end | `tests/EMD.EndToEnd.Tests` | the MVP axis: design → validate → co-simulate → export |

Fast unit tests run on every change. Integration tests are pinned to the
versions in the project's `toolchain` block and must also pass in the CI
container.

## Current validation

Until the .NET scaffold lands, validate the project schema:

```bash
python3 -m jsonschema -i examples/project.json schema/emd-project.schema.json
```

## Pull request checklist

- [ ] Tests were written first and fail/pass for the right reasons.
- [ ] `dotnet test` (or the relevant suite) passes locally.
- [ ] Schema examples still validate; new negative fixtures are rejected.
- [ ] No generated artifacts are edited by hand.
- [ ] Tool versions are pinned; no floating `latest` dependencies.
- [ ] Documentation is updated when behavior or public formats change.

## License

By contributing you agree that your contributions are licensed under
`GPL-3.0-only`, the license of this project.
