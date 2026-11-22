# Plan: data-bound ANSI screen templates, Durdraw animations and block word-wrap

Status: **proposed, not started.** Nothing here gets built until David says to build.

**Partly superseded by [screen-pages.md](screen-pages.md)**, which turns screens into routable Razor pages:
- art (`.dur` or `.ans`) is now compiled into C# at build time, replacing D2 (scanning in the browser) and
  D5 (the Node converter);
- the browser only ever receives finished ANSI.

The placeholder grammar, `TextLayout` word-wrap, compositor and Durdraw format notes below still apply.

## Goal

Move screen layout out of C# string-building and into art files that artists can edit, without
losing anything PIZON does today:

1. **Templates.** Screens are `.ans` files (drawn in Moebius or PabloDraw) with named `@` placeholders.
2. **Data binding.** C# binds values, lists and input fields to those placeholders by name, and can
   update one field later without redrawing the screen.
3. **Animation.** Animated slots play *while* the screen is live, and fields are never painted over.
   Durdraw (`.dur`) files can be imported, including placeholders drawn inside the frames.
4. **Word wrap.** Every piece of text on every screen wraps neatly inside its block. It never
   spills past the block, never cuts a word without showing a marker, never drops color at a line
   break, and never splits a hotspot.

## What the code does today

- Screens build ANSI in C# (`MainMenuScreen.Render`, `Ui.*`) and send it through `ITerminal`.
- **Wrapping is inconsistent:**
  - `BulletinsScreen.WrapPipeLine` doesn't carry color across a break or split long words, and it
    can split a hotspot `|[K]…|]`.
  - `TextWrap.Wrap` handles plain text only.
  - Many screens truncate with `TextWrap.Fit` instead of wrapping: file descriptions (45 columns),
    door descriptions (68), hall-of-fame notes (48), the header title (60).
  - `TextWrap.Center(…, 80)` cuts the tagline and goodbye lines.
- **Animations block.** `Terminal.playAnimation` is a queue item, so nothing else draws while it
  plays, and a key stops it. A boxed animation is merged into the one `CellBuffer` with `blit`, so
  it overwrites whatever was there.

## Key decisions

| # | Decision | Why |
|---|---|---|
| D1 | Placeholders use `@…@` and their **width is the width of the token** in the art. | Keeps the art WYSIWYG, avoids clashing with pipe codes, and is the classic Synchronet/PCBoard style. |
| D2 | **The terminal (JS) scans for placeholders**, not C#. | Art from editors depends on cursor moves; `cell-buffer.js` is already the ANSI interpreter. |
| D3 | **C# does the text layout and JS clips it to the block.** | Wrapping rules and tests live in one place (C#); JS clipping means nothing can spill even if C# gets it wrong. |
| D4 | The screen is drawn from **three layers: art, then animation slots, then fields.** | Animations and live fields can run together without overwriting each other. |
| D5 | Durdraw files are **converted ahead of time** by a Node tool into an animation format (v2). The browser never reads `.dur`. | Keeps gzip and format quirks out of the client; the result can be checked in tests. |
| D6 | Text content stays in the JSON files; templates hold **only art and layout**. | `/plain` and the content docs keep working, and copy edits don't touch art. |
| D7 | The `.ans` files in `wwwroot/content/screens/` are the source of truth. A live-edit API is out of scope. | It's the same deploy story as the existing content. |

## Template format

### Placeholder grammar

| Token | Meaning |
|---|---|
| `@NAME....@` | Output field. Width = the whole token, `@`s included. Its colors are the art's colors on the first cell. |
| fill `.` / `>` / `=` | Left / right / center alignment (`@ONLINE>>@`). |
| `@?NAME....@` | Input field (a single line, max length = width). |
| `@^.......@` | Continues the block above (same column and width) one more row. A stack of these makes a **block**, a rectangle the artist can see. |
| `@?NAME...@` + `@^…@` rows | A multi-line input block (the message composer). |
| `@ANIM:name..@` + `@^…@` rows | An animation slot rectangle. |
| `@list.FIELD..@` | A field in a list row. The row repeats for each item (see the sidecar). |
| `@@` | A literal `@`. |

Names are `[A-Z][A-Z0-9_]*`, matched case-insensitively. When a field is narrower than its name,
use an alias in the sidecar (`"aliases": { "O": "ONLINE" }`).

### Sidecar (`<name>.screen.json`, optional)

```jsonc
{
  "aliases": { "O": "ONLINE" },
  "fields": {
    "BODY":  { "overflow": "page" },               // page | clip | scroll (see "Word wrap")
    "NEWHANDLE": { "required": true, "min": 3, "tab": 1 }
  },
  "lists":  { "messages": { "rows": 10, "overflow": "page" } },
  "slots":  { "cam": { "src": "anim/sysopcam.json", "loop": true, "maxSeconds": 20, "range": "idle" } }
}
```

### Scan result (JS → .NET)

`loadTemplate(ansText)` returns `{ ansi, fields: [{ name, kind, row, col, width, height, fg, bg, align, list? }] }`.
`ansi` is the art with every token replaced by spaces in the token's own colors.
- **Scan errors** (overlapping rectangles, a `@^` with nothing above it, a token running past
  column 80, an unclosed `@`) are returned as `errors[]`.
- C# throws on errors in Development, and the content tests fail on them.

## Word wrap (applies to every screen)

### A new `TextLayout` in `Pizon.Doors.Sdk/Text/`

It replaces `WrapPipeLine` and every use of `Fit` and `Center` on prose. Doors use it too.

- **Measuring.** Width is counted in visible cells. Pipe codes, hotspots and links take no width.
  Tabs become spaces. Characters the 80×25 grid can't show in one cell (emoji, East Asian wide
  characters, combining marks) are normalized when the text is escaped, with `?` as the fallback.
- **Line breaks.** Lines break at spaces or after a hyphen. A word longer than the width is
  hard-split. Spaces at the start of a wrapped line are dropped, but explicit newlines and blank
  lines are kept.
- **Colors carry over.** Each output line starts with the color in effect at that point and ends by
  resetting to the block's base colors. A line break never changes color.
- **Hotspots and links stay whole.** `|[K]label|]` and `|{url}label|]` move to the next line together.
  If one is longer than the width, it's split and the hotspot reopens on each piece, so both halves
  still work.
- **Alignment.** Left, right or center within the block. Every line is padded to exactly the block
  width with the block's background, so text left over from an earlier value is erased.
- **Overflow** (set per block, default `clip` for one-line fields and `page` for multi-line blocks):
  - `clip`: the last visible line ends with `…` (CP437 has no ellipsis, so `...` is used instead).
  - `page`: a `-- more --` prompt *inside the block*. Space or Enter goes forward and Q leaves.
    This replaces `Ui.PageAsync` for bodies.
  - `scroll`: new lines push old ones up within the block (for live text).
- **API:** `TextLayout.Layout(string pipeText, int width, int height, Align align, Overflow overflow)`
  returns a `LaidOutText`, which holds `Pages` (each a list of exactly-`width` lines) and a
  `Truncated` flag.

### Enforcement, so wrapping can't drift

- **By construction.** Text reaches a field only through `TextLayout`, and JS hard-clips every
  field write to its rectangle.
- **Strict terminal mode.** In Development and in tests, the terminal reports (`console.error` plus
  a .NET log entry) any write that wraps automatically at column 80 or paints a field outside its
  rectangle. JS tests already fail on any console error.
- **Worst-case screen tests.** Every screen is rendered with stress data: 16-character handles, a
  500-character message, one 120-character unbroken word, text heavy with pipe codes, and a
  hotspot longer than its block. Each bound block must stay within its width and height.
- **Content tests.** Every bulletin, file, door and hall-of-fame entry in `wwwroot/content` is laid
  out in its real block. The test fails when a `clip` block would truncate, so authors see the
  problem at build time instead of on the site. The hard "45 characters or fewer" rule in
  `content.md` becomes a two-line wrapped block.

## Terminal changes (`wwwroot/js/terminal/`)

| Module | Change |
|---|---|
| `template.js` (new) | Scans for placeholders: runs the art through a scratch `CellBuffer`, finds `@` tokens row by row, builds blocks from `@^` stacks and returns fields and errors. Pure, so it's unit tested. |
| `compositor.js` (new) | The three layers: the art `CellBuffer`, the animation slots (each its own buffer and rectangle) and the field layer (cells per field, clipped). `compose()` produces the cells for `DomRenderer`. Only changed cells are marked dirty. |
| `index.js` | Template mode: `showTemplate`, `setField(name, rows[])`, `startSlot(name, animText, options)`, `stopSlot`, `focusField`. Slots tick on every `requestAnimationFrame` **independently of the queue**, so keys go to the screen instead of stopping a slot. One-off full-screen animations (the connect sting, door intros) keep today's queued behavior. Transitions (Draw, Wipe, Spray) run on the composed first frame. |
| `ansimation.js` | Reads animation format v2 (below) alongside v1 JSON and ANSI movies. Adds per-frame delays and named ranges. |
| `cell-buffer.js` | An optional strict flag that reports automatic wrapping. |

Reduced motion: slots show their first frame only. Field updates still happen, since they are
data, not motion.

## .NET changes (`Pizon.Web.Client`)

- **`Terminal/ITerminal.cs`:** `ShowTemplateAsync`, `SetFieldAsync`, `StartSlotAsync`,
  `StopSlotAsync`, and `JsTerminal` wiring for each.
- **`Screens/Templates/ScreenTemplate`:** loads `.ans` and its sidecar through `ContentStore`,
  holds the scan result, and provides:
  - `Bind(name, Func<string>)` for text that's already pipe-escaped where needed;
  - `BindList(list, items, rowBinder)` with paging;
  - `Compose()` (art plus first values, for transitions) and `RefreshAsync(name)`.
- **`FieldWriter`:** runs `TextLayout` with the field's size and alignment, then `SetFieldAsync`.
  Visitor text is always `PipeCodes.Escape`d first.
- **`FormRunner`:** Tab, Shift+Tab and Enter move between `@?` fields in `tab` order. One-line
  fields reuse `LineEditor` at the field's position. Multi-line blocks use a new `BlockEditor`
  that wraps words as you type and replaces the line-by-line `LineEditor.ReadMessageAsync`.
  Validation comes from the sidecar (`required`, `min`), and errors show in an optional
  `@ERROR@` field.
- **`Ui` helpers** stay for doors and for any screen not yet migrated.

## Durdraw import (`src/tools/dur-to-anim.mjs`)

### The `.dur` format

Verified against `durdraw/durdraw_file.py` and the sample files, on 2026-11-16:

- **Container.** Gzipped JSON: `{ "DurMovie": { formatVersion, colorFormat ("16" | "256"), encoding, framerate,
  sizeX/sizeY (v ≤ 7) or columns/lines (v ≥ 8), frames: [{ frameNumber, delay, contents[], colorMap }] } }`.
- **Text.** `contents` is one Unicode string per row, already decoded even when `encoding` is cp437.
- **Colors.** `colorMap` holds `[fg, bg]` pairs:
  - **v ≤ 7 stores it column-first, `[x][y]`.** v ≥ 8 stores it row-first, `[y][x]`. The sample
    `dur-welcome1.dur` (v7) is 80 entries of 24.
  - **256-color files:** fg 1–16 go through Durdraw's `color_256_to_ansi_16` table, and every
    other value is an xterm index. bg is an xterm index.
  - **16-color files:** use Durdraw's `escapeFgMap` and `escapeBgMap` (DOS order, 1-based,
    bright colors at 9–16).
  - **v ≤ 5** (for example `rain.dur`) may use the older palette (`old_16_pal_to_new`).
- **Timing.** `delay` is in seconds, and `0` means `1 / framerate`.
- **Unsupported.** Gzipped Python pickles (the oldest `.dur` files) are not supported. The tool
  says to open and re-save the file in the current Durdraw.

### Converter steps

1. Read the file with `zlib.gunzipSync` (Node built-ins only), detect the version and normalize the
   colors to row-first.
2. Map the colors to PIZON's palette indices. Indexes 0–15 are ANSI order, which is Durdraw's
   output order.
3. Scan each frame for placeholders with the same grammar as `template.js`. Share one pure module,
   `src/tools/lib/placeholders.mjs`, imported by both the tool and the tests; the client copy stays
   in `template.js`, and a test checks the two agree on a shared set of examples.
4. **Fields carry forward.** A field found in frame *n* applies to later frames until a frame
   contains a placeholder with that name again. A placeholder whose width changes between frames
   is an error.
5. Apply the optional `<name>.anim.json` sidecar: `loop`, `maxSeconds`, and `ranges`
   (`{ "idle": [0, 23], "busy": [24, 47] }`).
6. Write animation format v2 to `wwwroot/content/anim/<name>.json`.

### Animation format v2

```jsonc
{
  "version": 2, "cols": 24, "rows": 12, "loop": true, "ranges": { "idle": [0, 23] },
  "frames": [
    { "delay": 100,                                     // ms
      "rows": ["text…", null, …],                       // null = same as the previous frame
      "attrs": ["fg,bg,len;fg,bg,len", null, …],        // run-length colors per row
      "fields": [{ "name": "HANDLE", "row": 3, "col": 2, "width": 16, "fg": 15, "bg": 0, "align": "left" }] }
  ]
}
```

- Limits stay as today: 240 frames, a size no larger than the slot (or 80×25), and a delay of
  33–5000 ms.
- `.dur` sources live in `src/Pizon.Web.Client/art-src/`, which is not served. A JS test converts
  each one again in memory and fails if the committed v2 JSON is stale.

### Fields in animation frames at runtime

- C# sets the value once (`SetFieldAsync("cam.HANDLE", …)`), already laid out for the field's width.
- On each frame, JS stamps it at that frame's position in that frame's colors. Animated
  placeholders therefore move and change color with the art.
- A `range` can be chosen from data. For example, `StartSlotAsync("cam", range: online > 1 ? "busy" : "idle")`.
- **Input fields are not allowed inside animations.** The tool rejects `@?` in frames.

**Credits.** Durdraw is BSD-3-Clause (© Sam Foster). The color tables are ported from it, so add
an entry to `docs/credits.md`.

## Screen migration

Each screen gets `wwwroot/content/screens/<name>.ans`, an optional sidecar, and a slimmer C# class
that binds data and handles keys. The blocks in **bold** are the ones that wrap.

| Screen | Template | Blocks and fields |
|---|---|---|
| Main menu | `mainmenu.ans` | `HANDLE`, `LEVEL`, `ONLINE>` (live); menu items are art plus hotspots; member-only items are fields |
| Welcome | `welcome.ans` | **`TAGLINE`** (centered, wraps to 2 lines), banner stays `pizon.ans` |
| Bulletins list | `bulletins.ans` | list `bulletins` (`NUM`, **`TITLE`**, `UPDATED`) |
| Bulletin view | `bulletin.ans` / `bulletin-cam.ans` | **`BODY`** (page), `ANIM:cam` slot plays while you read |
| Top players | `topplayers.ans` | list per door (`RANK`, `HANDLE`, `SCORE`) |
| Message board | `board.ans`, `message.ans`, `compose.ans` | list `messages`; **`BODY`** (page); **`?BODY`** block editor 70×7 |
| File areas | `files.ans`, `filearea.ans` | list `areas` (**`DESC`**); list `files` (`NAME`, `SIZE>`, **`DESC`** 2 rows, clip) |
| Doors | `doors.ans` | list `doors` (`TITLE`, **`DESC`** 2 rows) |
| User list | `users.ans` | list `users` (`HANDLE`, `CLEARANCE`, **`NOTE`** 2 rows) |
| Page SysOp | `page.ans` | **`?MESSAGE`** block 62×4, `ERROR` |
| Login / handle | `login.ans`, `handle.ans` | `?NEWHANDLE`, `ERROR`, **`HINT`** |
| Settings | `settings.ans` | `BAUD`, `SOUND`, `GLOW` values |
| Goodbye | `goodbye.ans` | **`THANKS`**, **`SESSION`** (centered, wrap) |
| SysOp console | `sysop.ans`, `sysop-msg.ans` | queue list; **`BODY`** |
| Door loading (`DoorHost`) | `doorload.ans` | **`TITLE`**, **`DESC`** (centered, wrap) |

Unchanged: the connect and handshake screens (pure animation and modem text), the RIP card, and the
inside of doors (they use `TextLayout` directly through the SDK).

## Phases

Each phase ends with `pwsh src/coverage.ps1` passing (warnings are errors, coverage at least 80%)
and the docs updated.

1. **`TextLayout` and wrap fixes on the current screens** (no templates yet). The wrapping
   requirement is met right away.
   - Replace `WrapPipeLine`, and every prose use of `Fit` and `Center`.
   - Add the worst-case and content tests.
2. **Template scan and output fields.** Add `template.js`, the interop, and `ScreenTemplate` and
   `FieldWriter`. Migrate the main menu, including the live `ONLINE` field.
3. **Blocks, lists and overflow.** Add `@^` blocks, list rows, and the clip, page and scroll
   policies. Migrate bulletins, files, doors, the user list, message reading and top players.
4. **Forms.** Add `FormRunner` and `BlockEditor`. Migrate login and handle, page SysOp, compose and
   settings.
5. **Layers and background slots.** Add `compositor.js` and slots that keep playing while you read.
   Bulletin #2's cam moves into an `@ANIM` slot.
6. **Durdraw.** Add `dur-to-anim.mjs`, animation format v2, fields in frames and ranges. Convert one
   sample `.dur` (with permission, or one drawn for PIZON) end to end.
7. **Clean-up.** Migrate the remaining screens, remove the dead `Ui` builders, and update
   `architecture.md`, `content.md` (templates, placeholders, the Durdraw workflow) and `credits.md`.

## Tests

| Area | Tests |
|---|---|
| `TextLayout` (xUnit, SDK tests) | breaks, hyphens, long words, colors carried over, hotspots and links reopened, alignment, padding, clip/page/scroll, wide characters normalized, a property test (no line wider than `width` for random input) |
| `template.js` (node) | every token form, editor-style cursor moves (`ESC[nC`), blocks, errors, colors taken from the art |
| `compositor.js` (node) | layer order, field clipping, a slot frame never covers a field, dirty cells, reduced motion |
| `dur-to-anim.mjs` (node) | v5, v7 and v8 fixtures (small hand-made `.dur` files), the column-first quirk, both color modes, delay 0, fields carried forward, width-change error, rejected `@?`, stale-output check |
| Screens (xUnit, `FakeTerminal`) | every screen with stress data; field values passed to `SetFieldAsync` fit their rectangles; key routing unchanged |
| Conventions | every template in `content/screens` scans with no errors, and every bound name in C# exists in its template (and the reverse) |

## Risks and open questions

- **Placeholder width versus name length.** Very narrow fields need aliases, which is a small
  burden on artists. An alternative would be a sidecar-only width override.
- **Animation cost.** A full-screen slot at 30 fps re-composes about 2,000 cells per frame. Only
  dirty cells get re-rendered. Measure this on a phone in phase 5.
- **Durdraw format drift.** Format v8 is new, so pin the tool to the versions tested and fail
  clearly on anything else.
- **Durdraw sample art** in its repository has its own authors. Don't ship it; draw PIZON's own.
- **Choosing a range from data** could spread to state machines. Keep it to "the screen picks one
  named range."
