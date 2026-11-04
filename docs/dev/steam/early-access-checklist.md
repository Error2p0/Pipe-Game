# Early Access checklist

Status at the Phase 5 gate. Nothing here has been sent to Steam: every outward step is yours.

## Ready in the repo

- [x] Capsules rendered from the game: `capsules/` (header 920×430, small 462×174, main 1232×706, vertical 748×896, library 600×900, library hero 3840×1240). Drafts with the working title in Unity's default font (D127).
- [x] Screenshots at 1920×1080: `screenshots/` (8). Steam asks for at least 5. Note that they come from a showcase city with $499M, so the wallet looks richer than a real game at that stage.
- [x] Store text draft: `store-text-draft.md`.
- [x] Demo build: menu **Head Loss → Build Windows Demo** writes `Builds/demo/` (D126).
- [x] Steam Cloud path and Steamworks.NET integration from Phase 3.

To remake the capsules: enter play mode, set up a scene (`HeadLoss.Editor.Showcase.BuiltCity`), then call `HeadLoss.Editor.SteamAssets.RenderCapsules()`.

## Only you can do these

- [ ] **Decide the name.** "Head Loss" is a working title. Search Steam and the trademark registers before you commit to it; the capsules, store text and `game.json → displayName` all carry it.
- [ ] **Steamworks partner account and app** (the app fee, tax and bank forms).
- [ ] **Real app ids.** Replace the placeholder `480` in `game.json → steam.appId`. A Steam demo is its own app with its own id: today the demo build writes the same id as the full game, so it needs changing before upload.
- [ ] **Steam Auto-Cloud:** root `WinAppDataLocalLow`, subdirectory `Elevate Studios/Head Loss/saves` (the phase 4 report explains the path).
- [ ] **Price, release date and regional pricing.**
- [ ] **Store page:** paste and edit the store text, upload capsules and screenshots, fill in the content survey. Steam's review of the page and of the build takes some days, and a "coming soon" page has to be live for a while before release; check the current minimums in Steamworks.
- [ ] **A trailer.** Not in the spec's task list, and not made. Steam pages do far better with one. A short capture of a heatmap reacting to a fixed bend is the obvious first shot.
- [ ] **Publisher name.** The project's company name is `Elevate Studios` (Player Settings, save path). Confirm it is the one you want public.

## Decide before launch

- [x] **Which Phase 6 items must be in the launch?** All nine, in full (D132), so launch comes after Phase 6. Store text, capsules and screenshots get remade then.
- [ ] **Achievements:** optional in the spec, none built. Steamworks.NET is there, so they would be a small job. Worth having: first building online, first burst repaired, first gas building, first new city.
- [x] **Demo cut-off:** tiers 1–2 with no gas. Confirmed (D126).
- [ ] **Mobile:** the UI gets its readiness pass in Phase 6 (task 9), but no phone build is planned (D133).

## Not tested

- [ ] A build uploaded to Steam and run through the Steam client. Only the placeholder app id has been used.
- [ ] Steam Overlay and Steam Cloud on a real app id.
- [ ] Other PCs. Everything so far has run on the development machine only, so the system requirements in the store text are placeholders.
- [ ] Steam Deck or Linux (no Linux build has been made).
