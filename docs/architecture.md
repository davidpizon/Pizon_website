# Architecture

## Overview

```
Browser (Blazor WebAssembly, no prerender)                       Server (Pizon.Web, ASP.NET Core)
┌──────────────────────────────────────────────┐                 ┌─────────────────────────────────┐
│ Screens (C#)  ──ANSI──►  Terminal (JS module)│                 │ Auth: cookie + Discord OAuth2   │
│   pages + BbsHost pump     parser → buffer   │  /api, /auth    │ Minimal APIs (board, scores,    │
│   BbsApp (legacy screens)  → DOM renderer    │ ◄─────────────► │   page SysOp, SysOp console)    │
│ Services: BbsApi, settings, presence, doors  │  /hubs/node     │ EF Core + SQLite (App_Data)     │
│ Doors (lazy-loaded .wasm) via DoorHost       │ ◄─────────────► │ NodeHub (SignalR presence)      │
└──────────────────────────────────────────────┘                 │ Notifier (ntfy / Discord hook)  │
                                                                 └─────────────────────────────────┘
```

The terminal lives in `Layout/BbsLayout`, which every screen address shares, so it survives moving between
screens. Screens come in two kinds while [plans/screen-pages.md](plans/screen-pages.md) is under way:

- **Screen pages** (`Screens/Pages/*.razor`): the main menu, bulletins, Top Players, the message board, file
  areas, doors and the user list. Each is an `@page` that inherits `BbsPage` and declares
  `[BbsAccess(...)]`. It binds data to its compiled art with components that render no HTML
  (`Screens/Components`):
  - `<BbsScreen Art="…">` holds the screen, with its `Enter`/`Leave` transitions, `Prompt` and `OnKey`;
  - `<Field Slot="…" Value="…">` binds one value;
  - `<List Slot="…" Items="…" More="…">` pages lines through a block;
  - `<Hotkey Key="…" Href="…">` goes to an address, or `Screen="…"` leaves for a screen with no address.

  `Screens/Hosting/BbsHost` (one per tab) draws the page for the current address and pumps keys to it.
  Hotkeys come first, then a list's next page, then `OnKey`. When a bound value changes while the page is on
  screen, Razor's normal re-render sends just that slot to the terminal. The menu's callers-online row works
  this way. `BbsScreen` also renders a visually hidden mirror of its text and real links for its hotkeys,
  for screen readers.
- **Legacy screens** (`Screens/*.cs`): the entrance, login, handle, compose, page SysOp, settings, goodbye,
  RIP card and SysOp console. Each implements `IScreen`, writes ANSI and reads keys, then returns the next
  `ScreenId`. `BbsApp` runs them in a loop. For a screen that is now a page, it runs `PageScreen`, which
  hands the terminal to `BbsHost` until the address moves to another screen or a hotkey leaves for one.

Each screen's layout is a Durdraw drawing in `Screens/Art`, compiled into a C# class at build time by
`Pizon.Screens.Generator` (see [content.md](content.md#screen-art-srcpizonwebclientscreensartdur)). A screen
binds its data to the art's typed slots through `Screens/Rendering/ScreenView`, which lays each value out in
its slot with `TextLayout` and produces finished ANSI: the whole screen with `Render()`, one slot with
`RenderField()` for live updates (such as the callers-online row), and `PageAsync()` for lists longer than
their box. Streamed moments (the modem handshake, login prompts, download progress, `NO CARRIER`) are still
written directly.

## Screen addresses

Each screen has its own address, so it can be bookmarked and shared, and Back and Forward work.
`Screens/RouteTable.cs` lists them. A page screen's `@page` serves its own address and any below it (a
*nested* route: `/bulletins/2` still belongs to the bulletins screen, and its pages tell them apart).
`Pages/Home.razor` has an `@page` for each legacy screen. A test checks that all three agree.

| Address | Screen | Who can open it from a link |
|---|---|---|
| `/` | Dial-up, then the banner and login | anyone |
| `/login` | Login (`resume=1` after Discord, `error=failed`, `returnUrl=/…`) | anyone |
| `/menu`, `/board`, `/doors`, `/users`, `/rip`, `/settings`, `/goodbye` | The matching screen | anyone |
| `/bulletins`, `/bulletins/{n}`, `/bulletins/top` | The bulletin list, one bulletin, Top Players | anyone |
| `/files`, `/files/{area}` | The file areas, one area (`/files/tools`) | anyone |
| `/board/new`, `/board/reply` | Write or reply on the message board | members |
| `/page`, `/handle` | Page the SysOp, change handle | members |
| `/sysop` | SysOp console | the SysOp |

- **Following the address.** When a screen ends, `BbsApp` puts the next screen's address in the address bar.
  Leaving an entrance screen (dial-up, login, new-user handle) replaces the history entry, so Back from the
  menu doesn't redial. When the address changes by itself (Back, Forward, a typed address), `BbsApp` stops
  the current screen and opens the one named.
- **Links skip the dial-up.** Any address but `/` draws its screen straight away, after asking the server
  who is calling. An unknown bulletin or file area shows "file not found".
- **Access from a link.** A caller who hasn't logged on yet and opens a member or SysOp screen goes to
  `/login` first, then on to that screen. A signed-in caller who isn't the SysOp gets "file not found" for
  `/sysop`. Member screens still show ACCESS DENIED to guests themselves. The server enforces all of this
  for its APIs regardless.
- **Links never change anything.** Logout, Discord sign-in, the dial-up steps and the banner have no
  address of their own, so no link can start them.
- **`returnUrl` is only ever a known screen address** on this site. `RouteTable.TryResolve` rejects
  anything else, such as `//host` or `/\host`.

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
