#!/usr/bin/env bash
# Deploy BeatBallot to a DigitalOcean droplet over SSH: push a fresh .env.prod
# (pulled live from Doppler) and rebuild/restart the stack.
#
# Requires: ssh access to the droplet, Doppler CLI (`doppler login`, or
# DOPPLER_TOKEN set for a non-interactive service token in CI).
# Usage:
#   DROPLET_HOST=1.2.3.4 bash scripts/deploy-do.sh
# Optional overrides:
#   DROPLET_USER=root DROPLET_SSH_KEY=~/.ssh/beatballot_deploy APP_DIR=/opt/beatballot \
#   DOPPLER_PROJECT=beatballot DOPPLER_CONFIG=prd GIT_REF=v1.2.0 bash scripts/deploy-do.sh
#
# GIT_REF (default: main) is checked out exactly — a tag (e.g. v1.2.0) pins
# the deploy to that precise commit; a branch name fast-forwards to its tip.

set -euo pipefail

: "${DROPLET_HOST:?Set DROPLET_HOST to the droplet IP or hostname}"
DROPLET_USER="${DROPLET_USER:-root}"
DROPLET_SSH_KEY="${DROPLET_SSH_KEY:-$HOME/.ssh/beatballot_deploy}"
APP_DIR="${APP_DIR:-/opt/beatballot}"
DOPPLER_PROJECT="${DOPPLER_PROJECT:-beatballot}"
DOPPLER_CONFIG="${DOPPLER_CONFIG:-prd}"
GIT_REF="${GIT_REF:-main}"

SSH=(ssh -i "$DROPLET_SSH_KEY" "$DROPLET_USER@$DROPLET_HOST")

echo "=== Pulling secrets from Doppler ($DOPPLER_PROJECT/$DOPPLER_CONFIG) ==="
ENV_CONTENT="$(doppler secrets download --no-file --format env-no-quotes --project "$DOPPLER_PROJECT" --config "$DOPPLER_CONFIG")"

echo "=== Writing .env.prod on droplet ==="
echo "$ENV_CONTENT" | "${SSH[@]}" "cat > $APP_DIR/.env.prod"

echo "=== Checking out $GIT_REF on droplet ==="
"${SSH[@]}" "cd $APP_DIR && git fetch --all --tags --prune && git checkout '$GIT_REF' && if git symbolic-ref -q HEAD >/dev/null; then git merge --ff-only 'origin/$GIT_REF'; fi"

echo "=== Rebuilding and restarting the stack ==="
"${SSH[@]}" "cd $APP_DIR && docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build"

echo "=== Pruning old images ==="
"${SSH[@]}" "docker image prune -f"

echo ""
echo "Deploy complete ($GIT_REF): https://$(echo "$ENV_CONTENT" | grep '^DOMAIN=' | cut -d= -f2)"
