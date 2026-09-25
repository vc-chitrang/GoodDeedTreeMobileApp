# Good Deed Tree – Android Kiosk App Overview

The input side of the Good Deed Tree installation. A visitor writes a short promise on an Android tablet and swipes it up. The promise is sent over the local network to the **Wall app**, a Windows PC with a 4K display, where it flies into a 3D tree as a leaf.

- **Engine:** Unity 6 (6000.3.9f1). UI is built with **UI Toolkit**; motion uses **DOTween**.
- **Target:** Android tablet, portrait only, 1080×1920 reference resolution, minSdk 28.
- **Project folder:** `GoodDeedTreeMobileApp/Assets/Games/GoodDeedTreeMobileApp/`. **Only this folder may be changed**; the rest is the ViitorCloud base project.
- **Scene:** `Scenes/GoodDeedKiosk.unity`. Everything is set up in the scene and UXML; nothing is created at runtime.
- **Repo:** github.com/vc-chitrang/GoodDeedTreeMobileApp (branch `main`).
- **Build:** through the base project's menu (**Build Android Release**), driven by `Game Info So.asset` (application type Kiosk, scene GoodDeedKiosk).

---

## Screen flow

`KioskFlow` is the state machine:

1. **Attract:** idle screen that shows the running promise count from the wall.
2. **Input:** name (≤15 characters) and promise (≤60 characters), or tap a suggestion chip (up to 6 chips, filled from the wall). A leaf previews the text, centred on the leaf blade, in bright yellow with a thin golden outline.
3. **Release:** the visitor swipes up on the leaf (at least 170 px within 1.2 s) and the leaf flies off.
4. **Sending:** POSTs the promise to the wall.
5. **Result:**
   - Accepted: shows the leaf number.
   - Rejected: the wall's filter blocked it.
   - Offline: saved and will retry.
6. **Thank you:** then back to Attract.

Any screen returns to Attract after 60 s of inactivity.

---

## Talking to the wall

| Call | Purpose |
|---|---|
| `GET /api/v1/health` | Connection check (polled every 5 s for the status badge) |
| `GET /api/v1/stats` → `{promiseCount}` | Count on the attract screen |
| `GET /api/v1/suggestions` → `{suggestions:[...]}` | Suggestion chips |
| `POST /api/v1/promises` | `{requestId, name, promise, suggestion, createdAtUtc}` → `{status, leafNumber, queuePosition}` |

- **Server URL:** set in `Config/KioskConfig.asset`, as the wall PC's address on port 8080.
- **Discovery fallback:** if the URL fails, the kiosk broadcasts `GOODDEEDTREE_DISCOVER` on UDP 47777. The wall answers `GOODDEEDTREE_WALL <port>`, and the kiosk switches to that address and remembers it in PlayerPrefs.
- **Idempotent:** every submission has a `requestId` (also sent as the `Idempotency-Key` header), so a retry never creates a second leaf.
- **Offline queue** (`SubmissionQueue`): if the wall can't be reached, the promise is stored and retried with backoff from 5 s up to 120 s.
- **Privacy:** request and response bodies (names and promises) are never logged.
- **Status badge** (bottom-right): "Wall connected · N ms", "Wall offline · retrying", plus "· N pending" when some are queued.
- **Network requirements:**
  - The tablet and the wall PC must be on the **same Wi-Fi**, with no client isolation.
  - The wall PC's firewall must allow TCP 8080 and UDP 47777.
  - Mobile data alone will not work.

---

## Kiosk device behaviour

- **`KioskDevice`:**
  - Portrait lock, full screen, never sleep, 60 FPS.
  - Android **lock task / screen pinning**, so visitors can't leave the app.
- **Android plugin** (`Plugins/Android/GoodDeedKiosk.androidlib`):
  - `KioskBootReceiver`: starts the app after the tablet reboots.
  - `KioskRestarter`: restarts the app.
  - Built with compileSdk 36 and build-tools 36.0.0 to match Unity's bundled SDK.
- **Operator panel** (`OperatorPanel`):
  - Opened by tapping the top-right corner 5 times within 3 s, then entering a PIN. The PIN is stored only as a SHA-256 hash, with a lockout after 5 wrong attempts. **Change the default PIN before deploying.**
  - Shows the server status, pending count and current URL.
  - Actions: check connection, send pending now, restart app, exit kiosk mode.

---

## Key files

| File | Role |
|---|---|
| `Scripts/KioskFlow.cs` | Screen state machine, swipe, idle reset, chips |
| `Scripts/KioskApiClient.cs` | HTTP client and UDP discovery |
| `Scripts/SubmissionQueue.cs` | Offline queue with retry |
| `Scripts/KioskModels.cs` | Request and response data types |
| `Scripts/LeafView.cs` | Leaf preview and fly-off animation |
| `Scripts/ConnectionStatusView.cs` | Bottom-right wall status badge |
| `Scripts/OperatorPanel.cs` | Hidden operator panel with PIN |
| `Scripts/KioskDevice.cs` | Lock task, orientation, sleep, restart and exit |
| `Scripts/KioskConfig.cs` + `Config/KioskConfig.asset` | Server URL, limits, timings, operator settings |
| `UI/Kiosk.uxml`, `UI/Kiosk.uss` | All screens and controls (chips, PIN pad, status label) |
| `Art/` | Leaf, glow and background images; fonts |
| `Game Info So.asset` | Base-project build info (title, bundle ID, version, orientation) |

---

## Open items

- **Android build:** it failed on missing build-tools 35. The plugin now targets 36.0.0; rebuild to confirm.
- **Uncommitted:** the UXML-authored UI, `Game Info So.asset` and the `build.gradle` fix.
- **To confirm:** the bundle ID `com.vc.gooddeedtree` and an app icon (none yet).
- **Not done:** a real-tablet test on the same Wi-Fi as the wall (after the wall PC's firewall rules are added).
- **Not implemented:** no shared-key auth between kiosk and wall; no cloud relay for tablets on mobile data only.
