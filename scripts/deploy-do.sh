#!/usr/bin/env bash
# Deploy BeatBallot to a DigitalOcean droplet over SSH: push a fresh .env.prod
# (pulled live from Doppler) and rebuild/restart the stack.
#
# Requires: ssh access to the droplet, Doppler CLI (`doppler login`).
# Usage:
#   DROPLET_HOST=1.2.3.4 bash scripts/deploy-do.sh
# Optional overrides:
#   DROPLET_USER=root DROPLET_SSH_KEY=~/.ssh/beatballot_deploy APP_DIR=/opt/beatballot \
#   DOPPLER_PROJECT=beatballot DOPPLER_CONFIG=prd bash scripts/deploy-do.sh

set -euo pipefail

: "${DROPLET_HOST:?Set DROPLET_HOST to the droplet's IP or hostname}"
DROPLET_USER="${DROPLET_USER:-root}"
DROPLET_SSH_KEY="${DROPLET_SSH_KEY:-$HOME/.ssh/beatballot_deploy}"
APP_DIR="${APP_DIR:-/opt/beatballot}"
DOPPLER_PROJECT="${DOPPLER_PROJECT:-beatballot}"
DOPPLER_CONFIG="${DOPPLER_CONFIG:-prd}"

SSH=(ssh -i "$DROPLET_SSH_KEY" "$DROPLET_USER@$DROPLET_HOST")

echo "=== Pulling secrets from Doppler ($DOPPLER_PROJECT/$DOPPLER_CONFIG) ==="
ENV_CONTENT="$(doppler secrets download --no-file --format env-no-quotes --project "$DOPPLER_PROJECT" --config "$DOPPLER_CONFIG")"

echo "=== Writing .env.prod on droplet ==="
echo "$ENV_CONTENT" | "${SSH[@]}" "cat > $APP_DIR/.env.prod"

echo "=== Pulling latest code on droplet ==="
"${SSH[@]}" "cd $APP_DIR && git pull"

echo "=== Rebuilding and restarting the stack ==="
"${SSH[@]}" "cd $APP_DIR && docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build"

echo "=== Pruning old images ==="
"${SSH[@]}" "docker image prune -f"

echo ""
echo "Deploy complete: https://$(echo "$ENV_CONTENT" | grep '^DOMAIN=' | cut -d= -f2)"
