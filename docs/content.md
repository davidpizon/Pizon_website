# Editing content

All site content is in `src/Pizon.Web.Client/wwwroot/content/`. Changing it needs no code changes:
edit, then redeploy (or just refresh while running locally). The JSON files allow `//` comments.
Lines marked **`TODO-SYSOP`** are placeholders waiting for real text.

| File | What it holds |
|---|---|
| `bulletins.json` | SysOp name, mood, location, tagline (status header) and the `[1]` bulletins. |
| `files.json` | The `[3]` file areas: favorite links dressed up as downloads. |
| `halloffame.json` | Extra names for the `[5]` user list (merged with real recent callers). |
| `doors.json` | The `[4]` door list (see [doors.md](doors.md)). |
| `pizon.ans` | The graffiti PIZON banner (the source for the banner baked into `Screens/Art/welcome.dur`). |
| `welcome.rip` | The RIPscrip welcome card shown after login. |
| `anim/` | Optional animations (see below). |

## Pipe codes

Text in the JSON files uses pipe codes, which `PipeCodes.ToAnsi` turns into ANSI:

| Code | Meaning |
|---|---|
| `\|00`–`\|15` | Foreground color 0–15. |
| `\|16`–`\|23` | Background color 0–7. |
| `\|#NNN` / `\|%NNN` | 256-color foreground / background (always three digits, e.g. `\|#196`). |
| `\|[K]label\|]` | A clickable hotspot that acts like pressing key `K`. |
| `\|{https://…}label\|]` | A link that opens in a new tab (http and https only). |
| `\|!` | Reset colors. |
| `\|CL` | Clear the screen. |
| `\|\|` | A literal pipe. |

**Colors 0–15 follow ANSI/xterm order, not the DOS attribute order.** So `|01` is red (maroon) and
`|04` is blue (navy). The bright versions are 8 higher: `|09` bright red, `|12` bright blue. The
house style: `|14` cyan headings, `|11` yellow hotkeys, `|13` magenta accents, `|07` body text,
`|08` dim text. The full 256-color chart is the Armageddon BBS one (see [credits.md](credits.md)).

Visitor text (messages, handles) is always escaped, so visitors can't use pipe codes.

## Bulletins (`bulletins.json`)

Each bulletin has a `number`, `title`, optional `updated` date, `lines` (pipe-code text, up to 76
columns, or 50 when it has an animation) and an optional `animation`. Bulletin #2 uses
`content/anim/sysopcam.json`, which plays in a 24×12 box to the right of the text.

## File areas (`files.json`)

Each area has a `name`, a `description` and `files`. Each file has an 8.3-style `name` (e.g.
`VSCODE.EXE`), a made-up `size` in bytes, a `description` of **45 characters or fewer**, and a `url`
(`null` shows "file not found on this drive").

## Art

- **Banner:** `pizon.ans` is plain ANSI (UTF-8 or CP437; a SAUCE record is ignored). It was generated
  with `node src/tools/make-banner.mjs` and can be redrawn in Moebius or PabloDraw. Keep it within
  80 columns and use 256-color codes for gradients. The welcome screen's copy is baked into
  `Screens/Art/welcome.dur`, so rerun `node src/tools/make-screen-art.mjs` after changing it.
- **RIP card:** `welcome.rip` is generated with `node src/tools/make-welcome-rip.mjs`. Supported
  commands: color, fill style, text size, reset, line, rectangle, bar, circle, oval, polygon, filled
  polygon, text and buttons (`|1U`). Buttons send their hotkey just like the ANSI menu.

## Screen art (`src/Pizon.Web.Client/Screens/Art/*.dur`)

Every BBS screen is drawn from a Durdraw file in `Screens/Art` (outside `wwwroot`, so it is never
published). At build time the screen compiler (`src/Pizon.Screens.Generator`, a Roslyn source generator)
turns each `.dur` into a C# class, the way Razor compiles `.razor` files: `main-menu.dur` becomes
`MainMenuArt`, with one typed property per placeholder (`MainMenuArt.Handle`). The browser only ever
receives compiled code and finished ANSI; it never sees the `.dur` file or the `@` syntax.

The drawings were made by `node src/tools/make-screen-art.mjs`, which paints each screen with the real
terminal emulator. To restyle a screen, either edit its rows in that script and rerun it, or open the
`.dur` in [Durdraw](https://github.com/durdraw/durdraw) and save it (256 colors, one frame). Keep each
placeholder's size and position, or move it and rebuild: the screen code finds slots by name.

Placeholders are typed straight into the art, one row at a time:

| Token | Meaning |
|---|---|
| `@NAME....@` | A field the whole token wide. Its value uses the color of the cell after the first `@`. Fill with `.` to align left, `>` right, `=` centered. |
| `@?NAME...@` | A field the caller types into. |
| `@^.......@` | Makes the slot directly above one row taller (same column and width), for blocks of text and lists. |
| `@ANIM:NAME@` | A box an animation plays in (the SysOp cam on bulletin #2). |
| `@[K] label@` | A hotspot: clicking the label sends key `K` (a key, or a name such as `Enter`). The label stays where it is drawn; only the two `@` cells turn blank. |
| `@CURSOR@` | Where the cursor rests after drawing; prompts and typed text appear there. |

Names are capitals, digits and `_`, starting with a letter. An `@` followed by anything other than a
capital, `?`, `^` or `[` is an ordinary character. Values bound into a field are laid out by `TextLayout`:
one-line fields are cut with `...`, taller ones word-wrap within their box, and colors and links carry
across wrapped lines, so text never spills into the art.

### Screen compiler errors

Problems in the art fail the build, like a mistake in a Razor file:

| Code | Meaning |
|---|---|
| `PZN001` | The file can't be read as Durdraw art (not gzip/JSON, no frames, no contents, no size). |
| `PZN002` | Unsupported format: save with 256 colors (16-color files aren't read yet). |
| `PZN003` | A malformed placeholder: no closing `@` on the row, a bad name or fill, or a hotspot without a key. |
| `PZN004` | A `@^@` continuation with no slot of the same column and width directly above it. |
| `PZN005` | Two placeholders with the same name in one file. |
| `PZN006` | The art is larger than the 80x25 terminal. |
| `PZN007` | The art has more than one frame (animated art isn't supported yet). |
| `PZN008` | The name clashes with a member of the art class (for example `NAME`, `TEXT`, `INSTANCE`). |

## Terminal glow

The soft phosphor glow around each character is set in `src/Pizon.Web/appsettings.json`:

```json
"Terminal": { "GlowBlur": 0.1 }
```

- The value is the blur radius as a fraction of the terminal font size. 0 turns the glow off, and
  0.25 was the original, stronger look.
- Values outside 0–1 are clamped, and a non-number falls back to 0.1.
- On the server, set it without a rebuild by adding `Terminal__GlowBlur=0.15` to `~/pizon/.env` and
  running `docker compose up -d`.
- Locally, use `dotnet user-secrets` or the `Terminal__GlowBlur` environment variable.
- `App.razor` writes the value onto `<html>` as `--term-glow`, and `app.css` reads it.

## Animations (`anim/`)

Every spot works without its file; add one to turn it on.

| File | Where it plays |
|---|---|
| `anim/sting.json` | Full screen right after `CONNECT 14400`, before the banner. |
| `anim/sysopcam.json` | The SysOp cam box in bulletin #2 (loops, max 20 s). |
| `_content/Pizon.Doors.<Name>/anim/…` | A door's intro (see [doors.md](doors.md)). |

Two formats are accepted:

1. JSON: `{ "fps": 10, "loop": false, "frames": ["<ansi>", "<ansi>", ...] }` (fps 1–30, up to 240 frames).
2. An ANSI "movie": one `.ans` file whose frames each start with a clear-screen (`ESC[2J`).

From a GIF: use a GIF-to-ANSI converter in **256-color xterm** mode (truecolor also works but is
snapped to the nearest palette color). If it exports a shell script, convert it with

```bash
node src/tools/sh-to-anim.mjs input.sh > src/Pizon.Web.Client/wwwroot/content/anim/sting.json
```

Keep clips short (about 60 frames or fewer) and only use GIFs you own or may use.
Visitors with reduced motion see the first frame only, and any key stops an animation.
