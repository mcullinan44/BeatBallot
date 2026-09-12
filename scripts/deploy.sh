#!/usr/bin/env bash
# Build the Docker image, push to ACR, redeploy the Azure App Service, and
# sync app settings (Spotify/Stripe/Admin/DB secrets) from Doppler.
# Requires: az CLI (logged in), Docker, Doppler CLI (`doppler login` or DOPPLER_TOKEN set).

set -euo pipefail

ACR_NAME="${ACR_NAME:-beatballotreg}"
RESOURCE_GROUP="${RESOURCE_GROUP:-Dynamix-dev-rg}"
APP_SERVICE_NAME="${APP_SERVICE_NAME:-BeatBallotWeb20250601005053}"
IMAGE_NAME="beatballot"
TAG="${TAG:-latest}"
DOPPLER_PROJECT="${DOPPLER_PROJECT:-beatballot}"
DOPPLER_CONFIG="${DOPPLER_CONFIG:-prd}"

REPO="$ACR_NAME.azurecr.io/$IMAGE_NAME:$TAG"

# Resolve the repo root (one level up from this script)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

echo "=== Logging in to ACR ==="
az acr login --name "$ACR_NAME"

echo "=== Building image: $REPO ==="
docker build -t "$REPO" "$REPO_ROOT"

echo "=== Pushing image ==="
docker push "$REPO"

echo "=== Updating App Service container image ==="
az webapp config container set \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --container-image-name "$REPO" \
  --container-registry-url "https://$ACR_NAME.azurecr.io"

echo "=== Syncing app settings from Doppler ($DOPPLER_PROJECT/$DOPPLER_CONFIG) ==="
SETTINGS=()
while IFS='=' read -r key value; do
  [ -z "$key" ] && continue
  SETTINGS+=("$key=$value")
done < <(doppler secrets download --no-file --format env-no-quotes --project "$DOPPLER_PROJECT" --config "$DOPPLER_CONFIG")

az webapp config appsettings set \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --settings "${SETTINGS[@]}" \
  --output none

echo "=== Restarting App Service ==="
az webapp restart \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP"

echo ""
echo "Deploy complete. Image: $REPO"
echo "App Service: https://$APP_SERVICE_NAME.azurewebsites.net"
