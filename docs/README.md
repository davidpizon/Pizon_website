# PIZON BBS — pizon.com

David Pizon's personal website, presented as a 1990s bulletin board system: dial in, watch the
graffiti banner spray onto the screen, sign in with Discord (or connect as GUEST), and browse the
bulletins, message board, file areas and door games, in 256-color ANSI. Log off to `NO CARRIER`.

Built with Blazor WebAssembly and ASP.NET Core on .NET 11.

## Run it locally

```bash
dotnet run --project src/Pizon.AppHost
```

## Test it

```bash
pwsh src/coverage.ps1
```

## Documentation

- [AGENTS.md](AGENTS.md) — layout, build rules and conventions (start here)
- [architecture.md](architecture.md) · [content.md](content.md) · [doors.md](doors.md) ·
  [deploy.md](deploy.md) · [credits.md](credits.md)
