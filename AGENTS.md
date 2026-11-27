# AGENTS.md

Contract for agents working in this repository.

## Rules
- Everything committed is English: docs, comments, commits, PRs, branches.
- Commits use the owner's git identity only. No `Co-Authored-By`, no "Generated with" footer.
- Commit subjects: short, imperative, conventional commits (`docs:`, `feat:`, `chore:`).
- Branches: `feature/`, `fix/`, `chore/`, `docs/`.
- Specs are design proposals. Mark anything taken from an external game as *reference*, anything we invented as *proposal*, and anything unknown as *TBD*. Never present a guess as fact.
- Do not copy names, lore, art, text or exact numbers from other games.
- Bundled third-party assets (fonts, sounds, textures) must follow [ADR 0037](docs/adr/0037-external-assets-policy.md): permissive licences only, every file listed in `ASSETS.md`.
- A decision that closes off alternatives gets an ADR in `docs/adr/` (format in its README). Accepted ADRs are never rewritten; supersede them.
- Update the specs in the same change as the decision that alters them, and keep `docs/specs/12-open-questions.md` current.

## Checks
```
git diff --check
dotnet test --filter "Tier!=Slow"   # fast loop; the full dotnet test below also runs the slow tier
dotnet test
dotnet build game -c Debug
```
Needs the .NET 8 SDK and, to run the game, Godot 4.7.2 (Mono). The simulation library must never reference Godot.

Releases: the version is the `VERSION` file; `tools/release/build-windows.ps1` builds the exe, zip and installer, and `.github/workflows/release.yml` only drafts a release. Agents never tag, publish or sign a release: see [docs/release.md](docs/release.md) and [ADR 0048](docs/adr/0048-windows-release-pipeline.md).
