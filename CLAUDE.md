# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This App Does

BeatBallot is a live DJ session tool. DJs log in with Spotify, create a session that generates a QR code, then attendees scan the QR to vote on upcoming songs or submit song requests. The DJ controls Spotify playback and can see live vote counts and incoming requests in real time.

## Build & Run

```bash
# Run the web app (from repo root)
dotnet run --project BeatBallot.Web

# Build solution
dotnet build BeatBallot.sln

# Publish a Release build
dotnet publish BeatBallot.Web -c Release
```

Dev URLs: `http://localhost:5078` / `https://localhost:49153`

Spotify credentials and the DB connection string live in `appsettings.json`. Override locally using .NET User Secrets (UserSecretsId: `6140487f-abe8-458c-b8be-388844e22690`):

```bash
dotnet user-secrets set "Spotify:ClientId" "<your-id>" --project BeatBallot.Web
```

## Project Layout

| Project | Purpose |
|---|---|
| `BeatBallot.Web` | Blazor Server app — all UI, SignalR hub, API controllers |
| `BeatBallot.Data` | Dapper repositories + SQLite schema initialization |
| `BeatBallot.DataModels` | Shared C# models (Session, SessionTrack, VoteTarget, etc.) |
| `BeatBallot.Services` | Background/shared services (SessionCacheService, SpotifyService stub) |
| `BeatBallot.Entities` | Placeholder — currently unused |

## Architecture

### Authentication
- DJs authenticate via **Spotify OAuth** (authorization code flow, manual PKCE-style).
- Tokens (`sat`, `srt`, `satexp`, `satcr`) are stored in **browser localStorage** via Blazored.LocalStorage.
- `TokenProvider` (`Services/Local/TokenProviderService.cs`) manages token fetch, validation, and refresh.
- The Admin panel (`Admin/`) uses a separate password-based auth (`AdminAuthService`).

### Data Layer
- **SQLite** via Dapper. `DapperContext.InitializeDatabaseAsync()` creates tables on startup if they don't exist.
- Tables: `DeeJay`, `Jam` (session), `JamPlaylist`, `JamTrack`.
- Repositories: `DeeJayRepositorycs.cs`, `SessionRepository`, `SessionPlaylistRepository`, `SessionTrackRepository`.
- The `DefaultConnection` string in `appsettings.json` is unset by default; `DapperContext` uses `SqliteConnection` — verify/configure for local vs. prod environments.

### Real-Time (SignalR)
- `SongRequestHub` (`LiveSession/TakeRequests/Shared/SongRequestHub.cs`) is the central SignalR hub.
- DJs call `StartTakeRequestsSession` to open a session; listeners call `JoinListenerSession` with the session ID from the QR code.
- Hub uses two in-memory static dictionaries (`_connections`, `_activeSessions`) for connection tracking — these reset on app restart.
- `SongRequestCacheService` (`LiveSession/TakeRequests/SongRequestCacheService.cs`) holds in-memory song request state per session.

### Spotify Integration
- `SpotifyService` (`Services/Spotify/SpotifyService.cs`) wraps `SpotifyAPI.Web` v7.1.1.
- Instantiated per Blazor circuit (scoped); logs its GUID on creation for tracing.
- Handles playlist fetch, track lookup, queue management, and playback control (play/pause/resume with device auto-activation fallback).
- `SpotifySearchService` (`Services/Spotify/SpotifySearchService.cs`) is separate for listener-facing search.
- `SpotifyTokenService` handles token exchange with Spotify's `/api/token` endpoint.

### Two Live Session Modes
1. **Voting Experience** — DJ picks a playlist; listeners vote on the next song from that playlist. Components in `LiveSession/VotingExperience/`.
2. **Take Requests** — listeners search Spotify and submit song requests to the DJ's queue. Components in `LiveSession/TakeRequests/`. Includes a tip jar (`TipJarComponent`) backed by Stripe.

### Session Policy
`SessionPolicy` (`Services/Local/SessionPolicy.cs`) controls per-session rules: block explicit content, max requests per listener, blocked artists/tracks/genres, duplicate request behavior.

### Blazor Component Organization
Components are organized by user role and feature area:
- `Auth/` — Spotify login, OAuth callback, auth guard (`AuthRequiredComponent`)
- `Home/DeeJay/` — DJ dashboard and layout
- `Admin/` — Admin panel with its own layout
- `LiveSession/VotingExperience/DeeJay|Listener/` — voting UI for each role
- `LiveSession/TakeRequests/DeeJay|Listener|Shared/` — request-mode UI

Each `.razor` file has a corresponding `.razor.css` scoped stylesheet.

## Docker (Local)

```bash
# Create a .env file with your secrets (copy and fill in):
# SPOTIFY_CLIENT_ID=...
# SPOTIFY_CLIENT_SECRET=...
# ADMIN_PASSWORD=...

docker compose up --build
# App available at http://localhost:5078
```

The container runs on HTTP port 8080 internally (Caddy handles TLS termination in production — see below). The `BaseUrl` env var must match the URL you've registered as a redirect URI in your Spotify app dashboard.

## Deployment (DigitalOcean droplet)

BeatBallot runs on a single DigitalOcean droplet (`docker-compose.prod.yml`): the app container, a self-hosted Postgres container, and Caddy as a reverse proxy handling automatic HTTPS (Let's Encrypt) for `beatballot.com`. A single instance is intentional, not a limitation to fix — all live session state is in-memory (see Postgres Data Layer note above), so the app can't scale past one instance anyway.

**First time only** — provisions a fresh Ubuntu droplet (Docker, firewall, clones the repo):
```bash
bash scripts/provision-droplet.sh
```

**Deploying:**
```bash
DROPLET_HOST=<droplet-ip> bash scripts/deploy-do.sh
# Optional overrides:
GIT_REF=v1.2.0 DROPLET_HOST=<droplet-ip> bash scripts/deploy-do.sh
```

The script pulls secrets live from Doppler's `prd` config, writes them to `.env.prod` on the droplet, checks out `GIT_REF` (default `main`) exactly, and rebuilds/restarts the stack via `docker-compose.prod.yml`.

**Automated deploys**: `.github/workflows/deploy.yml` runs this same deploy on every **published GitHub Release** (tag), not on every push to `main` — a deploy restarts the app container, which would drop any in-progress DJ session, so deploys are a deliberate action tied to cutting a release rather than a side effect of merging. A `workflow_dispatch` trigger is also available (Actions tab → "Deploy to Droplet" → Run workflow) for on-demand redeploys of any ref.
