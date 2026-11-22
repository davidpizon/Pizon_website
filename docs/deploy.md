# Deploying pizon.com

Production is one Docker host: a **Hostinger KVM VPS** running three containers from
`src/deploy/docker-compose.yml`:

- `web` — the BBS on port 8080, with SQLite on the `bbs-data` volume.
- `caddy` — HTTPS (Let's Encrypt, automatic), `www` → apex redirect, compression.
- `backup` — a nightly SQLite backup into `~/pizon/backups`, kept 14 days.

Hostinger's shared or web hosting can't run ASP.NET Core, so a VPS is required. Aspire is not used
in production.

## One-time setup

### 1. Hostinger (you do this in hPanel)

1. Buy a **KVM 1** VPS with **Ubuntu 24.04 LTS** and add your SSH public key during setup.
2. In **DNS / Nameservers** for pizon.com, add **A** records for `@` and `www` pointing to the VPS IP.
   Remove any parking or default A/CNAME records for those names.

### 2. Server

```bash
scp src/deploy/setup-server.sh root@<vps-ip>:/root/
```

```bash
ssh root@<vps-ip> bash /root/setup-server.sh
```

The script installs Docker, creates the `bbs` user (same SSH key), opens only ports 22/80/443,
turns off SSH password login and enables automatic security updates. Deploy as `bbs@<vps-ip>` from now on.

### 3. Discord application

1. Discord Developer Portal → **New Application** → **OAuth2**.
2. Add redirect URIs `https://pizon.com/signin-discord` and, for local testing,
   `https://localhost:7067/signin-discord`.
3. Copy the **Client ID** and **Client Secret**.
4. Find your own Discord user ID (Settings → Advanced → Developer Mode, then right-click your name →
   Copy User ID). That account becomes the SysOp.

### 4. Notifications

Install the **ntfy** app on your phone and subscribe to a long random topic name, e.g.
`https://ntfy.sh/pizon-<random>`. (A Discord webhook URL also works.)

### 5. Secrets on the server

```bash
ssh bbs@<vps-ip>
```

Create `~/pizon/.env` from `src/deploy/.env.example` and fill in `Discord__ClientId`,
`Discord__ClientSecret`, `SysOp__DiscordUserId` and `Notify__WebhookUrl`. It is never committed.
Non-secret settings can go there too, such as `Terminal__GlowBlur` (see [content.md](content.md#terminal-glow)).

Locally, the same values go in user-secrets:

```bash
dotnet user-secrets --project src/Pizon.Web set "Discord:ClientId" "<id>"
```

## Deploying

Needs Docker Desktop or Podman Desktop on your PC (the script uses whichever it finds) and SSH access.

```powershell
$env:PIZON_SERVER = "bbs@<vps-ip>"
pwsh src/deploy/deploy.ps1
```

`deploy.ps1` runs `src/coverage.ps1` first and stops if anything fails. It then builds the image
`pizon-bbs:<timestamp>`, streams it to the server over SSH (no registry), copies the compose files,
runs `docker compose up -d` and waits for `https://pizon.com/healthz`. The first deploy can take a minute
while Caddy gets the certificate.

### Rollback

```bash
pwsh src/deploy/deploy.ps1 -Rollback
```

Switches `web` back to the previous image tag (the last two are kept on the server).

## Backups

The `backup` container copies the database nightly with SQLite's online `.backup` into
`~/pizon/backups/bbs-<date>.db` and deletes copies older than 14 days. To copy the newest one to
your PC (into `src/deploy/backups/`, which is git-ignored):

```bash
pwsh src/deploy/pull-backup.ps1
```

To restore: stop `web`, copy the backup over `bbs.db` in the `bbs-data` volume, start `web`.

## Logs and health

```bash
ssh bbs@<vps-ip> "cd ~/pizon && docker compose logs --tail 200 web"
```

Logs rotate at 10 MB × 3 files per container. `GET /healthz` returns 200 when the app is up.

## After the first deploy, check

- `https://pizon.com` loads with a valid certificate and `https://www.pizon.com` redirects to it.
- `https://pizon.com/healthz` returns `Healthy`.
- Discord sign-in works, your account gets the SysOp console (`*` from the menu, or `/sysop`).
- A Page SysOp reaches your phone.
- The next morning, a file appears in `~/pizon/backups`.
