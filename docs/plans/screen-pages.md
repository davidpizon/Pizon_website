# Plan: BBS screens as routable, compiled Razor-style pages bound to .DUR/.ANS art

Status: **phases 1-4 built.** Phase 1 (addresses on the existing screens) was built on 2026-11-17. Most
of phases 2 and 3 followed on 2026-11-20: `TextLayout`, the `.dur` screen compiler (`Pizon.Screens.Generator`,
diagnostics PZN001-PZN008), compiled art for every screen and `ScreenView`. Phase 4 (the page model) was
built on 2026-11-22: `BbsHost`, `BbsPage`, `[BbsAccess]`, the `BbsScreen`, `Field`, `List` and `Hotkey`
components, and pages for the main menu, bulletins (list, `/bulletins/{n}`, `/bulletins/top`), the message
board, file areas (`/files`, `/files/{area}`), doors and the user list. Still to do: `.ans` compilation,
forms (phase 5), animated art (phase 6) and the clean-up (phase 7). Two earlier changes from the plan:
hotspots are written `@[K] label@` (the label keeps its place), and 16-color Durdraw files are rejected
(PZN002) for now.

How phase 4 differs from the plan below:
- **No separate `ScreenComposer`.** `ScreenView` from phase 3 already is the view engine, so `BbsScreen` uses it
  directly.
- **The legacy loop still drives.** `BbsApp` runs `PageScreen` for each page screen, and `PageScreen` hands
  the terminal to `BbsHost`'s pump until the address moves to another screen or a hotkey leaves for one
  (`Hotkey Screen="ScreenId.Logout"`). The pump only draws a page whose address belongs to the screen it is
  running. Moving between pages of one screen (`/bulletins` to `/bulletins/2`) stays in the same pump. This
  goes away in phase 7.
- **`BbsHost` doesn't own the terminal or sign-in yet.** `BbsLayout` still creates the terminal and attaches
  it, and `BbsApp` still checks who is calling on a deep link. Before each draw the host re-renders the
  pages, so they show the call as it is now.
- **`[BbsAccess]` is checked by `RouteTable.Guard`**, as in phase 1. The attribute must agree with the route
  (a test checks).
- **Routes.** `/board/{id}`, `/board/{id}/reply` and `/doors/{id}` aren't built. The message board's [N] and [R]
  go to legacy compose screens at `/board/new` and `/board/reply`, which asks for the message number, until
  forms land in phase 5.
- **`List`** turns the page on any key that no hotkey takes, as the old paging did, and runs `OnLastPage` once
  the last page shows. The bulletin #2 cam uses that to play until phase 6 moves it into a `<Slot>`.
- **Imperative moments stay imperative.** Downloads and doors write to the terminal from a key handler, then
  call `BbsScreen.Redraw()`.

How phase 1 differs from the plan below:
- **Layout.** The terminal moved into a new `Layout/BbsLayout` (nested in `MainLayout`), not into `MainLayout`
  itself, so the HTML not-found page stays a plain page.
- **No `BbsHost` yet.** The existing `BbsApp` loop gained the address handling instead (address sync,
  Back/Forward, joining from a link, guards). `BbsHost` arrives with the page model in phase 4.
- **One address per existing screen.** Sub-addresses (`/bulletins/2`, `/board/42`, `/files/{area}`,
  `/doors/{id}`) come when those screens become pages, because today each screen handles them internally.
- **`/sysop` for guests.** A caller who hasn't logged on goes to login first, which is how the SysOp signs in.
  Only a signed-in caller who isn't the SysOp gets "not found".
- **Goodbye.** `/goodbye` hangs up without asking for a key. It changes nothing on the server (it only leaves
  presence), so it can't act for someone.
- **Logout has no address**, so no link can log anyone out.

## Context

Today the BBS is one Razor page (`Pages/Home.razor`, routes `/` and `/sysop`). `BbsApp.RunAsync` loops through
`IScreen` classes (`Screens/*.cs`), and each screen builds ANSI in C# and returns the next `ScreenId`. The address
bar never changes, so a screen can't be bookmarked or shared, and Back leaves the site.

David wants screens managed the way Razor pages are, step for step:

| Razor | PIZON screens |
|---|---|
| `.razor` is compiled to a C# class at build time | `.dur` and `.ans` art is compiled to a C# class at build time, with a typed slot for each `@HANDLE....@` placeholder |
| A page's C# runs logic and data calls | A screen page (`.razor` with `@page`) runs logic and calls `IBbsApi` and `ContentStore` |
| The view engine merges the data into the template | The screen composer merges the bound values into the compiled art |
| The browser gets finished HTML and never sees Razor | The terminal (JS) gets **finished ANSI only** and never sees `.dur`, `.ans` or `@` syntax |
| Each page has a URL | Each screen has a route; the address bar updates; Back and Forward work; links can be shared where security allows |

This builds on `docs/plans/screen-templates.md`, which covers the `@` placeholder grammar, the `TextLayout` block
word-wrap, the layered compositor, animation slots and the Durdraw format notes. That doc gets updated to point here.
Two of its decisions are superseded:
- **Scanning in the browser (D2):** placeholders are now found at build time by the compiler.
- **The Node converter (D5):** compiling replaces it.

### Decisions settled with David
- **The browser never sees templates.** Art is compiled at build time, and only finished ANSI reaches the terminal.
- **The compiled screens run in the browser's C# (WebAssembly)**, as the app does today. The browser downloads
  compiled DLLs, just as Blazor WebAssembly ships compiled Razor. Data comes through the existing APIs.
- **Art can be `.dur` or `.ans`.** The same placeholders work in both.
- **Deep links go straight to the screen** (no dial-up). Only `/` plays the dial-up intro.

## Design

### 1. The screen compiler (like the Razor source generator)

- **New project `src/Pizon.Screens.Generator`.**
  - A Roslyn incremental source generator targeting `netstandard2.0`, so it works as an analyzer.
  - `Pizon.Web.Client.csproj` references it with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
  - Its tests go in `src/tests/Pizon.Screens.Generator.Tests`.
- **Input files.** Art source lives in `Pizon.Web.Client/Screens/Art/**/*.dur|*.ans`, outside `wwwroot`, so it's
  **never published**. It's passed to the generator as `AdditionalFiles`. `wwwroot/content/pizon.ans` moves here.
- **What the generator does:**
  - **`.dur`:** gunzips it (`System.IO.Compression`) and parses the JSON. `System.Text.Json` is bundled into the
    analyzer package; if that turns out fragile, a small purpose-built JSON reader is used instead. It handles the
    `[x][y]` colorMap layout of v ≤ 7 and the `[y][x]` layout of v ≥ 8, `sizeX`/`columns`, the 16- and 256-color
    tables ported from Durdraw (credit goes in `docs/credits.md`), and frame delays (0 means `1/framerate`).
  - **`.ans`:** a C# ANSI-to-cells interpreter, used only at build time. Strip SAUCE and decode CP437 the way
    `cp437.js` does. A parity test runs the same fixtures through `cell-buffer.js` and compares the cells.
  - **Placeholders:** scans each frame's cell grid using the grammar in `screen-templates.md` (`@NAME...@`, `@?NAME@`,
    `@^…@` blocks, `@list.FIELD@`, `@ANIM:name@`, `@@`, alignment fill, fields that carry forward in animations).
    The placeholder cells are replaced with blanks in the token's colors.
- **What it emits per art file:**

```csharp
// generated: MainMenuArt.g.cs
public sealed partial class MainMenuArt : ScreenArt
{
    public static MainMenuArt Instance { get; } = new();
    public static FieldSlot Handle { get; } = new("HANDLE", Row: 21, Col: 17, Width: 16, Height: 1, Fg: 14, Bg: 0, Align.Left);
    public static FieldSlot Online { get; } = new("ONLINE", ...);
    protected override ReadOnlySpan<byte> Cells => [ /* packed char, fg, bg */ ];
    // animated art also gets Frames, Delays, Ranges and per-frame field positions
}
```

- **Problems are compiler errors.** Scan problems (overlaps, a `@^` with nothing above it, a token past column 80, a
  width that changes between frames, `@?` inside an animation, an unreadable file or unsupported version) are reported
  as diagnostics `PZN001` and up. The build fails on them, which fits warnings-as-errors. Each diagnostic is
  documented in `docs/content.md`.
- **Typed slots mean binding mistakes don't compile.** `MainMenuArt.Handle` either exists or the build fails, just
  as a mistyped Razor parameter would. A conventions test covers the reverse: every placeholder in the art must be
  bound by its page.

### 2. The page model

```razor
@* Screens/Pages/MainMenu.razor *@
@page "/menu"
@attribute [BbsAccess(Level = SecurityLevels.Guest)]
@inherits BbsPage

<PageTitle>Main menu - PIZON BBS</PageTitle>
<BbsScreen Art="MainMenuArt.Instance" Transition="Transition.Draw">
    <Field Slot="MainMenuArt.Handle" Value="@Session.Handle" />
    <Field Slot="MainMenuArt.Online" Value="@Presence.Count" />     @* live: re-renders when it changes *@
    <Hotkey Key="1" Href="/bulletins" />
    <Hotkey Key="D" Href="/login" When="@(!Session.IsMember)" />
    <Hotkey Key="*" Href="/sysop" When="@Session.IsSysop" />
    <Slot Art="SysopCamArt.Instance" At="MainMenuArt.Cam" Range="@(Presence.Count > 1 ? "busy" : "idle")" />
</BbsScreen>
```

- **Forms.** `BbsForm` wraps `EditContext` with `DataAnnotationsValidator`, and `InputField` (with `@bind-Value`)
  lets you type at the field's position. Multi-line blocks use `BlockEditor`, which wraps words as you type.
  Errors are bound to an `ERROR` slot.
- **Lists.** `<List Items="…" Rows="MessageListArt.Rows">` repeats the art's row template, with paging.
- **The components output nothing to the HTML page**, apart from the accessible mirror (§7). They register with a
  cascading `BbsScreen`.
- **The view-engine step happens in `ScreenComposer`.** It takes the compiled cells and, for each field, the value
  laid out by `TextLayout` (escaped, word-wrapped, padded and clipped to the block). It produces **finished ANSI**
  for the first draw, and a small positioned ANSI write when one field changes. A field update is triggered by Razor's
  own re-rendering (`OnParametersSet` sees the value change), so `StateHasChanged` updates the terminal the way it
  updates a web page.
- **Animations are compiled too.** `ScreenComposer` sends the slot's frames as finished ANSI frames (today's v1
  `{fps, frames}` shape, extended with per-frame delays). For fields inside animations it sends the laid-out text
  plus each frame's position and colors. `compositor.js` stamps them per frame, so the browser still only handles
  finished ANSI.

### 3. A persistent terminal host

- **The terminal moves into the layout.** The `.crt`, `.term` and `.rip` markup moves from `Pages/Home.razor` into
  `Layout/MainLayout.razor`. The layout persists across route changes, so the terminal does too.
- **New scoped service, `Screens/Hosting/BbsHost`.** It owns:
  - the `JsTerminal` (`Terminal/JsTerminal.cs`);
  - `BbsSession`, now a scoped service;
  - the key pump: keys go to the active input, then to `Hotkey`s, then to the page's `OnKey`;
  - per-page cancellation;
  - transitions between pages (Undraw the old page, then draw the new page's composed ANSI with the page's
    `Transition`).
- **Sign-in is checked once per tab.** On first load the host awaits `GetMeAsync` and `Presence.ConnectAsync` before
  any guarded page draws. These calls move out of `Screens/LoginScreens.cs:32` and `:97`.

### 4. Routes

| Route | Screen | Access |
|---|---|---|
| `/` | dial-up → handshake → welcome → login | public |
| `/login?returnUrl=…` | login / Discord / guest | public |
| `/menu` | main menu | public |
| `/bulletins`, `/bulletins/{n:int}`, `/bulletins/top` | list, one bulletin, top players | public |
| `/board`, `/board/{id:int}` | message list, one message | public |
| `/board/new`, `/board/{id:int}/reply` | compose | member |
| `/files`, `/files/{area}` | file areas | public |
| `/doors`, `/doors/{id}` | door list, door | public, or member if `requiresAuth` |
| `/users` | user list and hall of fame | public |
| `/page`, `/handle`, `/settings` | page SysOp, change handle, settings | member, member, public |
| `/goodbye` | logoff (asks to confirm with a key) | public |
| `/sysop`, `/sysop/queue`, `/sysop/pages` | SysOp console | SysOp (anyone else sees "not found") |

- **The server renders the shell for every route**, because they're `@page` components already discovered by
  `MapRazorComponents<App>()` in `Pizon.Web/Program.cs`. Caddy needs no change.
- **Unknown routes** still show `Pages.NotFound`.
- **Route parameters are validated** against the content. An unknown bulletin, area or door shows the not-found screen.

### 5. Security for deep links

- **`[BbsAccess(Level, SysopOnly)]`** is checked by the host before drawing:
  - a guest who needs to be a member goes to `/login?returnUrl=<path>`;
  - a member without enough access gets `Ui.AccessDenied`;
  - anyone but the SysOp on a SysOp route sees not-found.
- **The server stays the authority.** These checks are display only.
- **Opening a link never changes anything.** Posting, paging, logging out and changing the handle all need a
  keypress or submit. This affects `LogoutScreen` and `GoodbyeScreen`.
- **`returnUrl` is checked on both sides.** The server uses `AuthSetup.SafeReturnUrl` (`Pizon.Web/Auth/AuthSetup.cs:41`).
  The client also requires a local path that matches a known route. `BbsApp.StartScreen`'s `?resume=login`,
  `?login=failed` and `/sysop` handling becomes `/login?returnUrl=…&error=failed`.

### 6. Navigation behavior

- **Address bar.** `Hotkey Href` and hotspot clicks call `NavigationManager.NavigateTo`, so the address bar updates.
- **Back and Forward.** The host cancels the page's token, so pending reads, animations and doors end cleanly, then
  draws the target page.
- **`replace: true` for the entrance steps**, so Back from `/menu` doesn't replay the modem.
- **Back inside a door** leaves it through the door's normal exit.
- **The compose draft** is kept in session memory for the tab.

### 7. Accessibility and titles

- **Every page sets `<PageTitle>`**, for tabs, bookmarks and shared links.
- **`BbsScreen` renders a visually hidden semantic mirror** of the bound fields and hotkeys, with real links to the
  routes. `/plain` stays.

### 8. Migrating a screen at a time

- **Every old screen gets a URL in phase 1.** `LegacyScreenPage` is an `@page` per legacy route that runs the
  existing `IScreen.RunAsync`, then maps the `ScreenId` it returns to a route with `RouteTable` and calls `NavigateTo`.
- **Screens are rewritten one at a time** as compiled-art pages, and each legacy route is deleted as its page lands.

## Phases

Each phase ends with `pwsh src/coverage.ps1` green (warnings are errors, coverage ≥ 80%) and docs updated.

1. **Host and routes on the legacy screens.**
   - Add `MainLayout` hosting, `BbsHost`, `BbsSession` as a service, `RouteTable`, `LegacyScreenPage`,
     `[BbsAccess]`, `returnUrl` and the no-action-on-load rule.
   - Reduce `Home` to `/`.
   - At the end, URLs, Back and deep links work.
2. **`TextLayout` and the word-wrap fixes** (phase 1 of `screen-templates.md`).
3. **Screen compiler.**
   - `Pizon.Screens.Generator`: `.dur` and `.ans` readers, placeholder scan, generated art classes and `PZN`
     diagnostics.
   - Parity fixtures shared with `cell-buffer.js`.
4. **Page model.**
   - Add `ScreenArt`, `FieldSlot`, `ScreenComposer`, and the `BbsScreen`, `Field`, `Hotkey` and `List` components.
   - Migrate the main menu first (including the live `ONLINE` field), then bulletins, top players, board read,
     files, doors and users.
5. **Forms.** Add `BbsForm`, `InputField` and `BlockEditor`. Migrate login and handle, page SysOp, compose,
   settings and the SysOp console.
6. **Animation.** Add compiled animated art, `compositor.js` background slots and `Range`. The bulletin #2 cam
   moves to `<Slot>`.
7. **Clean-up.**
   - Delete `BbsApp`'s loop, `ScreenId` routing, the legacy pages and the unused `Ui` builders.
   - Update `docs/architecture.md`, `docs/content.md` (authoring a screen, the Durdraw workflow, PZN codes),
     `docs/credits.md` and the `docs/AGENTS.md` folder layout.

## Critical files

- **Change:**
  - `Pizon.Web.Client/Pages/Home.razor(.cs)`, `Layout/MainLayout.razor(.cs)`, `Pizon.Web.Client.csproj`
    (analyzer reference, `AdditionalFiles`)
  - `Screens/BbsApp.cs`, `Screens/BbsSession.cs`, `Screens/LoginScreens.cs`, `Screens/SectionScreens.cs`
  - `Terminal/ITerminal.cs`, `Terminal/JsTerminal.cs`, `wwwroot/js/terminal/index.js`
  - `Program.cs` (client dependency injection)
  - `src/Pizon.slnx`
- **New:**
  - `src/Pizon.Screens.Generator/`
  - `Screens/Art/`
  - `Screens/Hosting/` (`BbsHost`, `RouteTable`, `BbsAccessAttribute`, `LegacyScreenPage`)
  - `Screens/Rendering/` (`ScreenArt`, `FieldSlot`, `ScreenComposer`)
  - `Screens/Components/` (each with a `.razor.cs`)
  - `Screens/Pages/*.razor`
  - `wwwroot/js/terminal/compositor.js`
- **Reuse:**
  - `Pizon.Doors.Sdk/Text/PipeCodes` (`Escape`, `ToAnsi`, `VisibleLength`), `Ansi.MoveTo`, `Palette256`
  - `Terminal/LineEditor.cs`, `Ui.AccessDenied`, `AuthSetup.SafeReturnUrl`, `ContentStore`, `IBbsApi`,
    `IPresence.Changed`
  - `transitions.js` and `ansimation.js` (frame playback)

## Risks

- **Download size.** Compiled art ships inside the client DLL. Static screens are about 6 KB each, but a 100-frame
  full-screen animation is about 600 KB. If animations grow, move them into a lazy-loaded art assembly, the same way
  doors are loaded with `LazyAssemblyLoader`.
- **Packaging the generator.** Bundling `System.Text.Json` into an analyzer is the fragile part. The fallback is a
  small JSON reader inside the generator.
- **Two ANSI interpreters.** The build-time C# one and the runtime JS one could drift apart. Parity tests on shared
  fixtures keep them aligned.

## Verification

- **Generator tests:**
  - small hand-made `.dur` fixtures (v5, v7 and v8, both color modes, delay 0, the column-first quirk) and `.ans`
    fixtures produce the expected generated classes (snapshot tests);
  - each `PZN` diagnostic fires on a bad fixture;
  - the parity fixtures produce the same cells as `cell-buffer.js` (node test reads the same files).
- **xUnit with `FakeTerminal`:**
  - every route renders;
  - guards: a guest at `/board/new` → `/login?returnUrl=/board/new`; a member at `/sysop` → not found;
    `/bulletins/99` → not found;
  - no route calls a state-changing API on load;
  - `RouteTable` round-trips every `ScreenId`;
  - changing one `Field` sends exactly one positioned write;
  - **terminal output never contains `@` placeholder syntax**;
  - `BbsForm` errors land in `ERROR`.
- **Conventions:** every page has `[BbsAccess]`, `<PageTitle>` and a `.razor.cs` summary; every placeholder is bound.
- **Publish check:** after `dotnet publish`, `wwwroot` contains no `.dur` or `.ans` files from `Screens/Art`.
- **End to end:** run `dotnet run --project src/Pizon.AppHost` and use the browser pane:
  - dial in at `/`, go to the menu, bulletins and #2, and check the address bar shows `/bulletins/2`;
  - Back → `/bulletins`;
  - reload `/bulletins/2` → it draws right away with no dial-up;
  - as a guest, `/board/new` → login; then `/auth/dev?user=tester&returnUrl=/board/new` → compose;
  - `/sysop` as a guest → not found; with `?user=sysop` → the console;
  - check the network tab shows no `.dur` or `.ans` requests, check the console has no errors, and take screenshots.
