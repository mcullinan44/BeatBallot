#!/usr/bin/env bash
# One-time setup for a fresh Ubuntu 24.04 DigitalOcean droplet: installs
# Docker, opens the firewall, and clones the repo.
#
# Run this ON THE DROPLET as root (or a sudo user), e.g.:
#   ssh root@<droplet-ip> 'bash -s' < scripts/provision-droplet.sh

set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/mcullinan44/BeatBallot.git}"
APP_DIR="${APP_DIR:-/opt/beatballot}"

echo "=== Installing Docker ==="
if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com | sh
fi

echo "=== Configuring firewall (ssh, http, https) ==="
if command -v ufw >/dev/null 2>&1; then
  ufw allow OpenSSH
  ufw allow 80/tcp
  ufw allow 443/tcp
  ufw --force enable
fi

echo "=== Cloning repo into $APP_DIR ==="
if [ -d "$APP_DIR/.git" ]; then
  echo "Repo already present, pulling latest instead."
  git -C "$APP_DIR" pull
else
  git clone "$REPO_URL" "$APP_DIR"
fi

echo ""
echo "Done. Next steps:"
echo "  1. Copy .env.prod.example to $APP_DIR/.env.prod and fill in real values"
echo "     (or run scripts/deploy-do.sh from your machine, which can push it from Doppler)."
echo "  2. From $APP_DIR: docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build"
