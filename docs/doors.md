# Door games

A door is a self-contained game in its own Razor class library, `src/Pizon.Doors.<Name>/`. It only
references `Pizon.Doors.Sdk` and is **lazy-loaded**: visitors download its `.wasm` only when they
enter it from the `[4]` menu.

## The contract

```csharp
public interface IDoor
{
    string Id { get; }       // matches doors.json and the score board
    string Title { get; }
    Task RunAsync(IDoorContext context, CancellationToken cancellationToken);
}
```

`IDoorContext` is everything a door can touch:

| Member | Purpose |
|---|---|
| `WriteAsync(ansi)`, `ClearAsync()` | Draw (use `PipeCodes.ToAnsi` and the `Ansi` helpers). |
| `ReadKeyAsync()`, `ReadLineAsync(max)` | Input; keys are named in `DoorKeys` (`Escape`, `Enter`, …). `ReadLineAsync` returns null on Escape. |
| `DelayAsync(delay)` | Pauses (skipped when the visitor presses a key, and in tests). |
| `PlayAnimationAsync(path)`, `LoadTextAsync(path)` | The door's own assets, relative to its `wwwroot`. |
| `Caller` | Handle and `IsAuthenticated` only. |
| `Random`, `Today` | Use these, never `Random.Shared` or `DateTime.Now`, so tests are repeatable. |
| `Scores` | `SubmitAsync(score)` and `TopAsync()` for this door only (guests can't submit). |
| `Storage` | `GetAsync`/`SetAsync`: localStorage namespaced to this door. |

Doors never reference the client app, the session or other doors. Returning from `RunAsync` (or
the visitor pressing Escape) goes back to the BBS; an exception shows `DOOR CRASHED - CARRIER
RESTORED` and the BBS carries on.

## Adding a door

1. Install the template once, then create the project:

   ```bash
   dotnet new install src/templates/pizon-door
   ```

   ```bash
   dotnet new pizon-door -n Pizon.Doors.Foo -o src/Pizon.Doors.Foo --doorId foo
   ```

2. Add it to `src/Pizon.slnx`, and in `src/Pizon.Web.Client/Pizon.Web.Client.csproj` add a
   `ProjectReference` plus `<BlazorWebAssemblyLazyLoad Include="Pizon.Doors.Foo.wasm" />`.
3. Add an entry to `src/Pizon.Web.Client/wwwroot/content/doors.json`:

   ```json
   {
     "id": "foo",
     "title": "Foo Quest",
     "description": "One line for the [4] menu.",
     "assemblies": [ "Pizon.Doors.Foo.wasm" ],
     "entryType": "Pizon.Doors.Foo.FooDoor",
     "enabled": true,
     "requiresAuth": false,
     "scored": true,
     "scoreMode": "max",
     "maxScore": 100000,
     "scoreTitle": "TOP PLAYERS",
     "intro": "anim/intro.json"
   }
   ```

   - `enabled: false` hides a door without a rebuild.
   - `requiresAuth: true` locks it for guests.
   - `scored`, `scoreMode` and `maxScore` are also read by the **server** to accept or reject score
     posts. `scoreMode` is `max` (best score per caller, the default) or `dailyCount` (one point per play per
     day; the board shows today only, like the Oracle).
   - `intro` is an optional animation in the door's `wwwroot`.

4. Add a test project `src/tests/Pizon.Doors.Foo.Tests/` that scripts keys through
   `FakeDoorContext` and checks the output (see `Pizon.Doors.Oracle.Tests`). The coverage gate
   applies to doors too.
5. Run `pwsh src/coverage.ps1`, then deploy.

## The Modem Oracle

The first door (`Pizon.Doors.Oracle`). The caller asks a question, line noise plays, and a fortune
card is drawn. The same question on the same day gives the same answer (seeded from the question
and the date). Fortunes are in `src/Pizon.Doors.Oracle/wwwroot/oracle.json`. Its score board,
"Most Devoted Seekers", counts consultations per caller per day.
