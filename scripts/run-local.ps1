# Run BeatBallot.Web locally with secrets injected live from Doppler, so
# there's nothing to set manually and no local secrets file to keep in sync.
# Requires: Doppler CLI, authenticated once via `doppler login`.
#   Install: winget install doppler.doppler
#
# Override project/config if needed: .\scripts\run-local.ps1 -DopplerConfig dev_personal

param(
    [string]$DopplerProject = "beatballot",
    [string]$DopplerConfig = "dev"
)

$RepoRoot = Split-Path -Parent $PSScriptRoot

if (Get-Command doppler -ErrorAction SilentlyContinue) {
    Write-Host "=== Running with secrets from Doppler ($DopplerProject/$DopplerConfig) ==="
    doppler run --project $DopplerProject --config $DopplerConfig -- dotnet run --project "$RepoRoot\BeatBallot.Web" --launch-profile https
} else {
    Write-Host "Doppler CLI not found -- falling back to whatever's already in 'dotnet user-secrets'."
    Write-Host "Install Doppler to stop managing user-secrets by hand: https://docs.doppler.com/docs/install-cli"
    dotnet run --project "$RepoRoot\BeatBallot.Web" --launch-profile https
}
