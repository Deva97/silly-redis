## Plan: Better project file structure for silly-redis

TL;DR - reorganize into standard .NET layered directories, improve naming clarity, and add docs while preserving current behavior.

**Steps**
1. Analyze current structure: `/src/sillyredis` for app and `/src/ServerTest` for tests.
2. Propose new structure with explicit root folders: `src/`, `tests/`, `docs/`, `build/`.
3. Include optional `src/sillyredis.Core`, `src/sillyredis.Server`, `src/sillyredis.Cli` for extensibility.
4. Move test project into `tests/ServerTest` and add a `tests/Integration` folder if needed.
5. Add README per project and unify `Directory.Build.props` at repo root.

**Verification**
1. Build solution from root after path updates.
2. Run tests and ensure existing behavior unchanged.
3. Document `dotnet sln` updated references.

**Decisions**
- Keep single solution or split into multiple solutions as needed.
- Boundaries: only filesystem layout and metadata; no code logic changes.

**Further Considerations**
1. Add `src/sillyredis.Tests` or `tests/sillyredis.UnitTests` for unit separation.
2. Consider adding `tools/` for scripts and `docker/` for container configs.
