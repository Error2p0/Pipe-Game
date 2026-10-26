# Unity CLI notes

CLI: `unity` 1.0.0-beta.13 at `C:\Users\Marko\AppData\Local\Unity\bin\unity`. Editor 6000.6.5f1, `com.unity.pipeline` 0.9.1-exp.1, port 7800.
Add `--no-banner` to every call. Run from the project root so the project is auto-detected.

## What works

| Task | Command |
| --- | --- |
| Editor connected? | `unity status` |
| List editor commands | `unity list` (or `unity command --query <term> --detail full` for parameters) |
| Recompile + report errors | `unity recompile --project-path "E:\Unity Projects\Pipe Game" --timeout 300` (prints nothing on success) |
| Recompile (async) | `unity command script recompile` then poll `unity command script recompile_status --result-only` until `completed` (the first call may 504 after 30 s; it still runs) |
| Console | `unity command console get --level warning --result-only` (JSON `entries[]`), `console status`, `console clear` |
| EditMode tests | `unity command test run --mode EditMode --timeout 300` (see below) |
| Play mode | `unity command editor.playmode start` / `stop` |
| Screenshot | `unity command capture game_view --save_path <png>` |
| Run C# in editor | `unity command script eval '<code>'` |
| Menu item | `unity command editor invoke_menu --path "Head Loss/Validate Data"` |
| Scenes | `unity command scene create/open/save/add_to_build` |
| Build | `unity command build run ...` then poll `build status` |

## Gotchas

- Removing packages while the Editor is open leaves Plastic SCM / PSD importer TypeLoad errors in the console until the Editor restarts. They are not project errors.
- `package search` takes `--query`, not `--name`, and only searches by exact name; `package list --scope available` lists registry versions.
- `unity test` (top-level) spawns a batch-mode Editor and must not be used while the Editor has the project open (CLAUDE.md §2.6). Use `unity command test run` instead.
