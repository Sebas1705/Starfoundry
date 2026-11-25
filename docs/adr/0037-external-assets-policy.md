# 0037. External assets policy: which third-party fonts, sounds and art may be bundled, and how they are recorded

- Status: proposed
- Date: 2026-11-25
- Area: repository, client
- Design: [reuse research](../research/reuse-c-assets.md) (the asset section), [audio](../prototypes/audio.md), [ui icons and fonts](../prototypes/ui-icons.md)
- Supersedes in part: the statements "no audio assets" in `docs/prototypes/audio.md` and "no external art" in `docs/prototypes/ui-icons.md` (both pages are updated; the rule that our own art, models and icons are original stands)

## Context
Until now the repository held no third-party file: every sound was synthesised from code, every icon and model was ours, and the interface used the engine's default font. Nobody has listened to the synthesised sounds, and the default font renders badly at 12 to 16 px. The research notes recommended Inter plus a display face and JetBrains Mono (SIL OFL) and Kenney's Sci-fi Sounds and Interface Sounds (CC0). The repository is public and MIT licensed, so anything bundled must be redistributable by anyone under terms we can state and keep.

## Decision
- **Accepted licences** for a bundled file: CC0 (and public domain dedications), SIL Open Font License 1.1, MIT, Apache-2.0, ISC and CC-BY 4.0 (with the credit the licence asks for).
- **Rejected:** GPL, LGPL, AGPL, MPL, any licence with a non-commercial (NC) or no-derivatives (ND) term, CC-BY-SA (share-alike would reach the game), anything with an unclear or missing licence, and paid files that cannot be redistributed in a public repository.
- **Every bundled third-party file has a row in `ASSETS.md`** at the repository root: file, pack, source URL, licence, author, attribution text, date added and size. The licence text of the pack (`OFL.txt`, `License.txt`) is kept next to the files. A test (`AssetsManifestTests`) scans `game/assets/fonts` and `game/assets/audio` for font and audio files and fails when a file has no row, a row has no file, a pack has no licence text, a licence is not on the accepted list, or the credits data is out of date.
- **Credits.** The in-game Credits page (main menu and Settings) lists every pack, its author and licence, the project's own licence and the libraries. Its data is `game/assets/credits.tsv`, generated from `ASSETS.md` (the test rewrites it when `STARFOUNDRY_UPDATE_CREDITS=1`), because markdown files are not exported with the game.
- **Only what is used.** Take the weights and files the game uses, not whole packs, to keep the repository small.
- **Downloads** come from the official source of the asset (the author's repository, the official `google/fonts` repository, the author's site). The licence file is read before anything is copied into the repository.
- **Original work stays original.** AGENTS.md still forbids copying art, names or lore from other games. This policy is about generic fonts and sound libraries, not about another game's content. Icons, models, textures and the look of the interface remain ours.
- **Fallbacks.** The game must run when an asset file is missing: the default font stands in for a font, the synthesised sound for a recorded one. Assets are read as raw bytes from `res://` (no editor import step; `*.import` is not committed) and the export preset includes `assets/fonts/*` and `assets/audio/*`.
- AGENTS.md is not changed by this ADR; this ADR is the policy.

## Consequences
- Benefits: a clear rule that a contributor or an agent can check, a legal paper trail for each file, a better first impression (readable type, recorded one-shots), and a test that stops an unrecorded file from slipping in.
- Costs: about 1.5 MB of fonts (and sounds when added) in the repository; a row to write per file; recorded sounds are common stock sounds other games also use.
- How it is checked: `AssetsManifestTests`, the text-fit audit (`tools/ui-audit.ps1`) after a font change, `--audio-test` and `--audio-ab` for sounds.
- Not decided here: music, textures or 3D models from outside; paid assets; user-made content.

## Alternatives considered
- **A. Allow a list of licences with a row per file (chosen).** Simple, checkable, honest.
- **B. Keep the repository asset-free.** Rejected: the default font and the never-heard synthesised sounds are the weakest part of the first impression.
- **C. Accept any "free" asset.** Rejected: free is not a licence; NC, ND and share-alike terms conflict with a public MIT game.
- **D. Keep third-party files outside the repository and download them at build time.** Rejected: the game must build and run from a fresh clone, and the files are small.
