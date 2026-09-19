#!/usr/bin/env bash
# Run BeatBallot.Web locally with secrets injected live from Doppler, so
# there's nothing to set manually and no local secrets file to keep in sync.
# Requires: Doppler CLI, authenticated once via `doppler login`.
#   Install: curl -Ls https://cli.doppler.com/install.sh | sudo sh   (WSL/Linux)
#            winget install doppler.doppler                          (Windows)
#
# Override project/config if needed: DOPPLER_CONFIG=dev_personal ./scripts/run-local.sh

set -euo pipefail

DOPPLER_PROJECT="${DOPPLER_PROJECT:-beatballot}"
DOPPLER_CONFIG="${DOPPLER_CONFIG:-dev}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

if command -v doppler >/dev/null 2>&1; then
  echo "=== Running with secrets from Doppler ($DOPPLER_PROJECT/$DOPPLER_CONFIG) ==="
  exec doppler run --project "$DOPPLER_PROJECT" --config "$DOPPLER_CONFIG" -- \
    dotnet run --project "$REPO_ROOT/BeatBallot.Web"
else
  echo "Doppler CLI not found -- falling back to whatever's already in 'dotnet user-secrets'."
  echo "Install Doppler to stop managing user-secrets by hand: https://docs.doppler.com/docs/install-cli"
  exec dotnet run --project "$REPO_ROOT/BeatBallot.Web"
fi
