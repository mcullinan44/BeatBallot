#!/usr/bin/env bash
# One-time setup: creates an Azure Container Registry and wires it to the App Service.
# Run once. Requires: az CLI, logged in via `az login`.

set -euo pipefail

ACR_NAME="${ACR_NAME:-beatballotreg}"
RESOURCE_GROUP="${RESOURCE_GROUP:-Dynamix-dev-rg}"
APP_SERVICE_NAME="${APP_SERVICE_NAME:-BeatBallotWeb20250601005053}"
LOCATION="${LOCATION:-canadacentral}"

echo "=== Creating Azure Container Registry: $ACR_NAME ==="
az acr create \
  --name "$ACR_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Basic \
  --admin-enabled false

echo "=== Enabling system-assigned managed identity on App Service ==="
az webapp identity assign \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP"

PRINCIPAL_ID=$(az webapp identity show \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query principalId \
  --output tsv)

ACR_ID=$(az acr show \
  --name "$ACR_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query id \
  --output tsv)

echo "=== Granting App Service AcrPull on the registry ==="
# Use --assignee-object-id/--assignee-principal-type instead of --assignee: the
# managed identity's service principal can take a few seconds to replicate in
# Azure AD after `az webapp identity assign`, and --assignee triggers a graph
# lookup that fails during that window. The object-id form skips that lookup.
az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --scope "$ACR_ID" \
  --role AcrPull

echo "=== Telling App Service to authenticate to ACR with its managed identity ==="
# AcrPull only grants permission -- App Service still defaults to registry admin
# credentials (disabled above) unless told to use the identity for the pull.
az webapp config set \
  --name "$APP_SERVICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --generic-configurations acrUseManagedIdentityCreds=true

echo ""
echo "Setup complete."
echo "ACR login server: $ACR_NAME.azurecr.io"
echo "Next step: run scripts/deploy.sh to build and push your first image."
