# Architecture

## Overview

```
Browser (Blazor WebAssembly, no prerender)                       Server (Pizon.Web, ASP.NET Core)
┌──────────────────────────────────────────────┐                 ┌─────────────────────────────────┐
│ Screens (C#)  ──ANSI──►  Terminal (JS module)│                 │ Auth: cookie + Discord OAuth2   │
│   BbsApp state machine     parser → buffer   │  /api, /auth    │ Minimal APIs (board, scores,    │
│   ScreenId → IScreen       → DOM renderer    │ ◄─────────────► │   page SysOp, SysOp console)    │
│ Services: BbsApi, settings, presence, doors  │  /hubs/node     │ EF Core + SQLite (App_Data)     │
│ Doors (lazy-loaded .wasm) via DoorHost       │ ◄─────────────► │ NodeHub (SignalR presence)      │
└──────────────────────────────────────────────┘                 │ Notifier (ntfy / Discord hook)  │
                                                                 └─────────────────────────────────┘
```

The whole UI is one Razor page (`Home`) hosting the terminal element. Screens are plain C# classes
(`src/Pizon.Web.Client/Screens/`), not Razor components: each implements `IScreen`, writes ANSI to the
terminal and reads keys, then returns the next `ScreenId`. `BbsApp` runs that loop.

## The terminal (`wwwroot/js/terminal/`)

| Module | Role |
|---|---|
| `ansi-parser.js` | Splits text into char / control / CSI / OSC tokens. |
| `cell-buffer.js` | 80×25 cells (char, fg, bg, link); SGR (16, 256, truecolor snapped to 256), cursor moves, erase, OSC 8 links. |
| `palette.js` | The 256 colors (Armageddon BBS chart for 0-15, xterm cube and grays). Must match `Palette256.cs` (checked by a test). |
| `dom-renderer.js` | One element per row, runs of equal attributes as spans; `key:` links become `<button>`s, URLs become `<a>`. Text nodes only, never `innerHTML`. |
| `pacing.js` | Baud-rate pacing (instant, 2400, 9600). |
| `transitions.js` | Cell-based effects: wipe, dissolve, scroll-off, draw/undraw (menu), spray (graffiti banner). |
| `ansimation.js` | Animation files: `{fps, loop, frames}` JSON or ANSI "movies" split on clear-screen. |
| `rip.js` | A RIPscrip subset drawn on a 640×350 canvas, with clickable buttons. |
| `cp437.js` | Decodes `.ans` files (UTF-8, or CP437 with the SAUCE record stripped). |
| `input.js` | Keys, hotspot clicks, "move the mouse to skip" and the hidden input for phone keyboards. |
| `audio.js` | Synthesized dial tone, DTMF, handshake and beep (Web Audio, opt-in). |
| `index.js` | The `Terminal` class (a queue of writes, transitions and animations on `requestAnimationFrame`) and the interop functions .NET calls. |

`prefers-reduced-motion` turns off pacing, transitions and animations (first frame only).

## Server

| Route | Who | Purpose |
|---|---|---|
| `GET /auth/discord?returnUrl=` | anyone | Starts Discord sign-in (scope `identify`). |
| `GET /auth/dev?user=` | Development only | Fake sign-in (`tester` or `sysop`). |
| `POST /auth/logout` | signed in | Clears the cookie. |
| `GET /api/me`, `POST /api/me/handle` | anyone / signed in | Session info; choose or change handle. |
| `GET/POST /api/messages` | anyone / signed in | Approved threaded messages; new posts are queued for review. |
| `POST /api/page-sysop` | signed in | Stores a page and pushes a notification. |
| `GET /api/callers/recent` | signed in | Recent callers for the user list. |
| `GET/POST /api/scores/{door}` | anyone / signed in | Top 10 per door; validated against `doors.json`. |
| `/api/sysop/*` | SysOp only (else 404) | Moderation queue, pages inbox, delete, ban. |
| `GET /plain` | anyone | A plain-HTML version of the content (accessibility, crawlers). |
| `/hubs/node` | anyone | Presence: node numbers and online count. |
| `GET /healthz` | anyone | Health check (Caddy, deploy script). |

Protections: the `X-PIZON-BBS: 1` header on every state change (CSRF), SameSite=Lax HttpOnly
cookies, per-IP rate limits, a honeypot field, a word filter, approve-first moderation and bans.
The SysOp is whoever signs in with the Discord user ID in `SysOp__DiscordUserId` (level 255).

Data (SQLite in `App_Data/`): `Users`, `Messages` (pending/approved), `SysopPages`, `Scores`.
Data-protection keys are stored next to the database so cookies survive restarts and redeploys.

## Presence

`NodeHub` keeps an in-memory map of connection → node number (lowest free number first) and
broadcasts the count, at most once a second. One server instance is assumed. The main menu shows
`N callers online` only when more than one caller is connected; handles are never shown.

## Doors

Doors are separate Razor class libraries built only against `Pizon.Doors.Sdk` and lazy-loaded with
`LazyAssemblyLoader` when entered. See [doors.md](doors.md).

## Local orchestration (Aspire)

`Pizon.AppHost` runs `web` with a SQLite resource (`bbsdb`). Containers are opt-in because they need
a running Docker or Podman: set `Pizon:UseSqliteWeb=true` for the SQLite Web viewer and
`Pizon:UseNtfy=true` for a local ntfy server (AppHost configuration or user-secrets). The
`Pizon.AppHost.Tests` smoke test starts the AppHost and checks `/healthz` and the home page.
