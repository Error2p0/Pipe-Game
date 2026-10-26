# 11 · UI, heatmaps, visuals and audio

[doc] The look is flat and minimal, like a clean blueprint: the heatmaps are the visual spectacle, everything else stays quiet.

## Heatmap modes [doc]

Hotkeys 1–7, available per fluid where relevant.

| Mode | Shows | Main use |
| --- | --- | --- |
| Velocity | Flow speed in pipes and rooms | Spot bottlenecks and noise risks |
| Pressure | Static pressure | Find weak outlets and burst risks |
| Flow rate | Volume per time per segment | Balance branches |
| Temperature | Fluid and room temperature | Heating, cooling, insulation |
| Density | Fluid density | Gas layering, hot vs. cold air |
| Concentration | Gas share in room air | Leaks and ventilation |
| Stress | Accumulated pipe stress | Predict bursts |

Animated particles flow along pipes in every mode, speed matching velocity, so the network feels alive even in the plain view.

[impl]
- `0` (or `` ` ``) = plain view (fluid colours). Modes that do not apply to the selected network are greyed out in the selector.
- Pipes are coloured by the value along them; rooms by the room grid (velocity, temperature, density, concentration). Pressure and flow colour pipes only; room cells stay neutral.
- Room layer toggle for two-layer fields: `upper` / `lower` / max (default max), hotkey `H`.
- Concentration mode has a species picker (natural gas, propane, hydrogen, CO₂, O₂ deficit, O₂ excess).
- Scale: auto-range to the current building with sensible fixed anchors (velocity 0–6 m/s for air, 0–3 m/s water; stress 0–1 with ≥1 in the warning colour). Legend always visible with units and min/max; a lock button freezes the range.
- City view has its own heatmap: **utilisation per main** (percent of capacity), plus a per-utility filter.

## Visual style [doc]

- Flat 2D, blueprint-like: light walls, soft room fills, pipes as clean coloured lines with one colour per fluid.
- Colour-blind-safe palettes for heatmaps (for example viridis), with a legend and numeric hover readouts.
- City view: simple building blocks that light up when online, gentle ambient motion.
- Calm audio: soft airflow and water sounds that rise with throughput, gentle UI clicks.

[impl] Palette (light theme default, dark theme available):
- Background paper `#F4F1EA`, grid lines `#E3DED2`, walls `#3E4A57`, room fills by type at 8% tint, text `#1F2730`.
- Fluid line colours from `06-fluids.md`, plus line patterns per fluid so colour is never the only cue.
- Heatmap ramps: viridis (default), cividis, magma; diverging (blue–white–red, colour-blind safe variant) for temperature relative to setpoint. Selectable in settings.
- Warning colour: one muted amber `#C98A2E`; danger: muted red `#B5483A`. Used sparingly.
- Ambient motion: slow drifting clouds' shadows over the city, online buildings breathe a soft glow (4 s cycle), particles in pipes.

## Building controls [doc]

- Drag to draw pipe runs; the tool auto-places fittings and shows their loss live.
- Hover any segment for flow, pressure, temperature and stress.
- Undo/redo and a free "simulate before you buy" preview, so experimenting costs nothing.
- Floor and layer switcher always visible.

The UI should scale cleanly to a phone screen later; avoid tiny click targets and hover-only information that has no tap equivalent.

[impl]
- Drawing: pick a network (fluid), size, material, fitting quality; left-drag from an existing node/opening; orthogonal by default, 45° mode from tier 2 (`Shift`). Right click or `Esc` cancels. Each placed corner shows its K and the pressure lost there as a small label while drawing. Snapping to openings, outlets and existing nodes. Tees are created by drawing from the middle of a segment.
- Selection: click a segment or equipment to select; box select with drag on empty space. Selection panel shows readouts and actions (upgrade, change size/material, insulate, delete, add valve).
- Readout (hover or tap-select): Q (m³/h or L/s), v (m/s), p start/end (Pa, bar or mbar by fluid), Δp split (friction / fittings / elevation), Re, temperature, stress bar.
- Undo/redo: `Ctrl+Z` / `Ctrl+Y` (and buttons). Unlimited in design mode; history cleared on leaving the building.
- Design mode is the default when editing; the top of the building view shows "Design · not committed · cost $X · projected income $Y/s" with Commit and Discard buttons.

## Screens [impl]

1. **Boot / main menu:** Continue, New city, Settings, Quit. Shows the city name and play time.
2. **City view:** map; top bar (wallet, debt, net income/s with breakdown, Reputation, prestige progress); event banner; tools (route main/cable, utilisation heatmap, filters); building hover tooltip (difficulty hint, `03-city.md`); roof icons (burst, leak, alarm with countdown, evacuated, tripped panel, inspector, certificate expiring).
3. **Building view:** requirement sheet panel (collapsible), outlet list with live delivery and zone colours, toolbox (networks, pipes, equipment, controls, safety), heatmap selector and legend, floor switcher (tabs + `PageUp`/`PageDown`), layer switcher, violations list, design bar (Commit / Discard), building status line and energy rating. `Esc` returns to the city.
4. **Shop / unlocks:** items by category and tier; locked tiers show the unlock condition.
5. **Prestige:** milestone progress (share online and income target), Reputation preview, prestige tree, Found new city.
6. **While you were away:** offline report (earned, electricity, fines, bursts, repairs done, paused timers first, events that happened).
7. **Settings:** audio sliders (master, ambience, UI), heatmap palette, theme, UI scale (80–150%), key rebinding, autosave interval, reduce motion.

## Hotkeys [impl]

| Key | Action |
| --- | --- |
| 1–7 | Heatmap modes (velocity, pressure, flow, temperature, density, concentration, stress) |
| 0 / ` | Plain view |
| H | Room layer: upper / lower / max |
| L | Cycle layer (floor / ceiling / wall cavity) |
| PageUp / PageDown, Tab-strip | Floor up / down |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Enter | Commit design |
| Esc | Cancel tool → deselect → back to city |
| C (hold/tap) | Crank the hand-crank fan |
| R | Rotate equipment before placing |
| Delete | Remove selection |
| Shift (while drawing) | 45° mode (tier 2+) |
| WASD / right-drag / wheel | Pan / pan / zoom |

## Accessibility and mobile readiness [impl]

- Minimum hit target 44 × 44 px at 1080p reference; UI scale setting.
- Every hover tooltip is also available by tap/select (selection panel).
- Never use colour alone: icons, patterns, labels.
- Reduce motion setting turns off particles' speed variation, glow breathing and ripple animations.
- Input through the Input System with action maps (Mouse+Keyboard now; Touch map stubbed for later).

## Procedural audio [impl]

No audio files. Synthesised in `OnAudioFilterRead` on one audio source per layer:
- **Airflow:** pink noise through a low-pass filter whose cutoff and gain follow total air throughput in the open building (log scale), very soft.
- **Water:** filtered brown noise with slow random amplitude modulation, gain by water throughput.
- **Fans/pumps:** a quiet hum at a frequency proportional to speed (40–120 Hz) with a soft harmonic, summed and limited.
- **UI clicks:** short sine blips (800–1200 Hz, 30 ms, exponential decay). Placing pipe: a soft "tock". Commit: a gentle two-note chime.
- **Alarm (gas):** a soft, slow two-tone chime every 4 s, never harsh. Explosion: a muffled low thump (filtered noise burst, 0.6 s).
- City view: soft ambience bed (wind noise) plus quiet chimes when a building comes online.
- Master limiter; everything defaults to low volume.
