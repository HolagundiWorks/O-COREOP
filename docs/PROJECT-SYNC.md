# O-CoreOp · AORMS sign-in and project sync

**Status (2026-10-19):** sign-in, Online projects, push, queued sync and pull are built and have been driven in the real app against a **scripted fake AORMS** (`BBSDesktop/tests/e2e`, 24 checks). **Not yet run against the real AORMS server.** The older hub flow (Bind, Flush, Publish, Pull JM) is still in the app and is described at the end; it is to be removed (hand-off item 4.5).
**Canon:** the wire contract is `docs/esti/AQC-CLIENT-SPEC.md` and the suite sign-in is `docs/esti/AORMS-SUITE-SIGNIN.md` in the AORMS repository. Library: `BBSDesktop/Aorms.Connect`. Hand-off with what is checked and what is not: [AORMS-CONNECT-HANDOFF.md](AORMS-CONNECT-HANDOFF.md) and [HANDOFF-O-COREOP.md](HANDOFF-O-COREOP.md).

## Sign-in

One AORMS login serves the suite (O-CoreOp, O-Draft). **File > Sign in to AORMS…** asks for email and password and starts this app's own session (one active session per user per app; a second computer signing in replaces it, and the first is offered "Sign in here"). The login is **the licence**: the studio must be on a connected plan (Studio or higher, not expired) or the app says so and carries on in Community mode.

| What | Where |
|---|---|
| Shared sign-in (refresh token, email) | Windows Credential Manager, target `AORMS/Connect`, shared with O-Draft; refreshed with the re-read-before-refresh rule so two apps do not break each other |
| This app's session id | Credential Manager, target `AORMS/Connect/aqc` |
| Never stored | the password, the access token; nothing goes in `firm.db` |
| Server | `https://aorms.in`, or the `AORMS_URL` environment variable |

The status chip in the bottom bar shows `AORMS · <studio>`, then the sync state (`synced`, `3 unsent`, `offline, 3 unsent`, `read-only …`, `taken over on another computer`, `sign in again`). Its tooltip has the account, role, plan and any notice.

## Menu

| File menu item | What it does |
|---|---|
| **Sign in to AORMS…** / **Sign out of AORMS** | as above; signing out clears the shared login |
| **Online projects…** | lists the studio's projects; **Link** ties the open project to one (saved in the project file); **Import CAD take-off** reads the newest take-off published to a bound project into the import preview |
| **Push project online…** | binds the open project to an AORMS project that has no AQC data yet and sends it (the first 500 rows with the binding, the rest in batches under the edit lease) |
| **Pull latest from AORMS** | brings in rows and settings changed elsewhere; asks first when there are unsent edits (the online version wins for the same rows) |

## What syncs, and how

The project stays a `.bbsproj` file. For an online project, **take-off rows and the office registers** (schedule, accounts, stores, org, contracts, link rules …) are sent as rows, and everything else (levels, markups, personas, covers …) as one **settings** block. Estimates and files go as versions (`VersionsApi`, not wired to a menu yet).

- **Queueing.** The app does not hook each edit. After the user has stopped editing for 1.5 s it works out which rows changed since the server was last brought in line (`RowBaseline`, a hash per row), queues only those in a file-backed outbox (`%LocalAppData%\AQCCore\outbox\<aqc id>.json`, `.base.json` beside it), and `ProjectSync` sends them under the edit lease (renewed every 60 s; if someone else holds it the project is read-only and the chip says so). Unsent edits survive a restart.
- **Row ids.** Every take-off row has a `_rid`. A duplicated row gets its own. **Derived** rows (shuttering, link-rule rows: `source` starting `auto`) get a stable id from what they derive from, because those sheets are rebuilt on every calculation.
- **Settings.** Sent when they change, after the rows. Keys that belong to one computer (take-off file path, stored PDF paths, logo path, the sync head) are never sent and never overwritten by a pull.
- **Pull.** The catch-up returns changed rows and the server's settings; the head moves only after they are applied. After a pull the baseline is taken from the project as it is.
- **Offline.** Failures back off (2 s up to 120 s), edits wait in the outbox, nothing is lost.
- **First open on a computer** with no baseline file takes the local file as the starting point and says so; use Pull latest to bring in the online version.

## Not built yet

Home screen (sign in first, "Continue without signing in", online projects, then local), opening an online project from the server onto a new computer, rate books, estimate and IPC versions, the contractor inbox and certify (`InboxApi`), the CAD take-off published through the portal being pulled automatically. See the Backlog in [ROADMAP.md](ROADMAP.md).

## Earlier hub flow (to be removed)

The earlier flow went through the separate AORMS Connect app: `%LocalAppData%\AORMS-Connect\session.json` and `catalog.json` (the **Bind** button reads the catalog; with no Connect app it says "Connect catalog empty"), `Aorms.Bridge`, `firm.db` and a meta outbox, **Flush**, **Publish project status / estimate totals** and **Pull JM** (joint measurements). It still runs, separately from the sign-in above, and the status strip still shows its **Bind**, **Flush** and **Sync** chip. Nothing new depends on it.
