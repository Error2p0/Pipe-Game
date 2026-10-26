# 12 · Save, time and offline progress

[doc] A finished building keeps earning while the game is closed. Offline: steady-state income × time, then roll failure events. Bursts while offline cut that building's income by 75%; in game they cut it to 0. Explosion timers (and every catastrophe timer) pause offline.

## Save format [impl]

- One JSON file per profile: `Application.persistentDataPath/saves/profile-<n>.json`, plus a rolling backup of the last 3 saves (`.bak1`–`.bak3`).
- Top-level: `saveVersion`, `generatorVersion`, `dataVersion` (hash of data files), `savedAtUtc` (ISO 8601), `playTimeSeconds`, `gameState` (`01-architecture.md` §State model).
- Write atomically (write to `.tmp`, then replace). Compress with gzip when > 1 MB.
- Migrations: a `SaveMigrator` with one step per version bump; a test loads a sample save of every past version.
- Autosave every 30 s (setting), on leaving a building, on Commit, on application pause/quit/focus loss.
- Steam Cloud (Phase 3+): sync the save folder via Steam Auto-Cloud configuration.

## Time [impl]

- All gameplay time is real time (UTC). No in-game calendar; durations in data are in seconds.
- On load, `offlineSeconds = now − savedAtUtc`. If negative (clock moved back), treat as 0 and log. Cap at `balancing.offline.maxHours` (default 72 h) [confirm].
- While the game runs but is unfocused or minimised, it keeps simulating normally ("in game" rules apply). Only time while the application was closed counts as offline.

## Offline catch-up [impl]

Goal: fast, deterministic, faithful to the in-game rules with the offline modifications.

1. **Steady state.** For every building, solve its networks at its current settings with crank speed 0 (smart controller runs one optimisation first if owned). Compute per-second net income, electricity and per-segment stress rate `dS/dt`.
2. **Event timeline.** Walk forward through `offlineSeconds` in variable steps, stopping at the next of:
   - a burst: for each segment with `dS/dt > 0`, sample its burst time from the Weibull model given current S (inverse CDF with the failure RNG);
   - a random gas fitting leak (Poisson);
   - a repair completing (standard crew; or maintenance-contract dispatch);
   - a certificate expiring;
   - an event starting or ending (city scheduler, `10-events.md`);
   - a building's state change that changes its steady state (re-solve that building only).
3. **Between stops**, add `income × Δt` analytically, using offline factors: bursts ×0.25, evacuated ×0, gas escalation stops at alarm/evacuation (explosion timer paused), outage cascade timers paused, inspection deadlines paused, new-tenant timers paused.
4. **Room grid offline:** not stepped in full. Leaks and outages are handled with room-level well-mixed approximations (one value per room and layer) so the alarm stage can be reached; full grids resume on return.
5. **Result:** the "While you were away" report (`11-ui-visuals-audio.md`), then normal play resumes. Paused timers resume with their remaining time and a visible countdown.

Performance: 72 h with 100 buildings in < 2 s (`01-architecture.md`). Show a short progress bar if longer than 0.3 s.

A test runs the same scenario (a) in real time with accelerated ticks and (b) through offline catch-up, with the in-game-only differences disabled, and asserts the money matches within 1%.
