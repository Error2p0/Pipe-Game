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
| PlayMode tests | `unity command test run --mode playmode --async_tests [--filter <name>]`, then poll `unity command test status --result-only` until `status` is `completed` (results also in `Temp/pipeline_test_status.json`). A synchronous PlayMode run is refused (the domain reload drops the request). Wait ~8 s after a recompile or the run does not start. |
| Keep play mode ticking while the Editor is unfocused | `unity command editor.playmode set_run_in_background --enable true` and `unity command editor set_auto_tick --enable true --interval_ms 16` (otherwise play mode stops after a frame or two and screenshots are stale) |
| Play mode | `unity command editor.playmode start` / `stop` |
| Screenshot | `unity command capture game_view --save_path <name>.png` saves under `Assets/` whatever path is given; move it out (and delete its `.meta`) right away |
| Run C# in editor | `unity command script eval '<code>'` |
| Menu item | `unity command editor invoke_menu --path "Head Loss/Validate Data"` |
| Scenes | `unity command scene create/open/save/add_to_build` |
| Build | `unity command build run ...` then poll `build status` |

## Gotchas

- Removing packages while the Editor is open leaves Plastic SCM / PSD importer TypeLoad errors in the console until the Editor restarts. They are not project errors.
- `package search` takes `--query`, not `--name`, and only searches by exact name; `package list --scope available` lists registry versions.
- `unity test` (top-level) spawns a batch-mode Editor and must not be used while the Editor has the project open (CLAUDE.md §2.6). Use `unity command test run` instead.
- `script eval` runs statements and returns `result`; in play mode it is the way to drive the game (for example `FindAnyObjectByType<GameRoot>().StartGame(GameContext.NewGame(r.Data), false)` starts a game that never writes the save).
- `InputTestFixture`: change one key of a device per frame. Events queued in the same frame are built from the device's last processed state, so `Release(z); Release(ctrl);` leaves `z` held.
- The Phase 2 game opens on the main menu. Its saves live in `%USERPROFILE%/AppData/LocalLow/Elevate Studios/Pipe Game/saves/`.
- While the Editor reloads, commands fail with "Pipeline server isn't reachable" (with or without the `PIPELINE_UNREACHABLE` code); retry after a few seconds.
- In Git Bash, long `python - <<'EOF'` heredocs sometimes fail to parse; write the script to a file first.
- `script eval` prints numbers in the Editor's culture (decimal commas).
- If play mode runs but `GameRoot.Data` is null, a script recompile happened during play (check `Logs/Editor.log` for "Requested script compilation"): stop play mode, let it settle, start again. A changing define (Steamworks.NET's `STEAMWORKS_NET`) caused it once; it is now set in ProjectSettings.
