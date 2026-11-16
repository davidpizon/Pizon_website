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
| `pizon.ans` | The graffiti PIZON banner. |
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
  80 columns and use 256-color codes for gradients.
- **RIP card:** `welcome.rip` is generated with `node src/tools/make-welcome-rip.mjs`. Supported
  commands: color, fill style, text size, reset, line, rectangle, bar, circle, oval, polygon, filled
  polygon, text and buttons (`|1U`). Buttons send their hotkey just like the ANSI menu.

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
