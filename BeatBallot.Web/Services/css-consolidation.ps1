# ===== CSS Consolidation Script =====
# This script finds all CSS files in a directory and consolidates them into a single file

param(
    [Parameter(Mandatory=$false)]
    [string]$SourceDirectory = ".",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "consolidated-styles.css",
    
    [Parameter(Mandatory=$false)]
    [switch]$Recursive = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$IncludeSubfolders = $true
)

# Function to write colored output
function Write-ColoredOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

# Validate source directory
if (-not (Test-Path $SourceDirectory)) {
    Write-ColoredOutput "Error: Source directory '$SourceDirectory' does not exist!" "Red"
    exit 1
}

# Get absolute paths
$SourcePath = Resolve-Path $SourceDirectory
$OutputPath = Join-Path (Get-Location) $OutputFile

Write-ColoredOutput "=== CSS Consolidation Script ===" "Cyan"
Write-ColoredOutput "Source Directory: $SourcePath" "Yellow"
Write-ColoredOutput "Output File: $OutputPath" "Yellow"
Write-ColoredOutput "Recursive Search: $Recursive" "Yellow"
Write-ColoredOutput ""

# Find all CSS files
$searchParams = @{
    Path = $SourcePath
    Filter = "*.css"
    File = $true
}

if ($Recursive) {
    $searchParams.Recurse = $true
}

$cssFiles = Get-ChildItem @searchParams | Sort-Object FullName

if ($cssFiles.Count -eq 0) {
    Write-ColoredOutput "No CSS files found in the specified directory!" "Red"
    exit 1
}

Write-ColoredOutput "Found $($cssFiles.Count) CSS file(s):" "Green"
foreach ($file in $cssFiles) {
    $relativePath = $file.FullName.Replace($SourcePath, "").TrimStart('\', '/')
    Write-ColoredOutput "  - $relativePath" "White"
}
Write-ColoredOutput ""

# Initialize output file with header
$header = @"
/*
=============================================================================
                        CONSOLIDATED CSS STYLES
=============================================================================
Generated on: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Source Directory: $SourcePath
Total Files: $($cssFiles.Count)
=============================================================================
*/

"@

$header | Out-File -FilePath $OutputPath -Encoding UTF8

# Counter for progress
$fileCount = 0
$totalFiles = $cssFiles.Count

# Process each CSS file
foreach ($cssFile in $cssFiles) {
    $fileCount++
    $relativePath = $cssFile.FullName.Replace($SourcePath, "").TrimStart('\', '/')
    
    Write-Progress -Activity "Consolidating CSS Files" -Status "Processing: $relativePath" -PercentComplete (($fileCount / $totalFiles) * 100)
    
    try {
        # Read file content
        $content = Get-Content -Path $cssFile.FullName -Raw -Encoding UTF8
        
        # Create section header for this file
        $sectionHeader = @"

/* 
=============================================================================
SOURCE FILE: $relativePath
File Size: $([math]::Round($cssFile.Length / 1KB, 2)) KB
Last Modified: $($cssFile.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
=============================================================================
*/

"@
        
        # Append section header and content to output file
        $sectionHeader | Add-Content -Path $OutputPath -Encoding UTF8
        
        if ([string]::IsNullOrWhiteSpace($content)) {
            "/* This file was empty */" | Add-Content -Path $OutputPath -Encoding UTF8
        } else {
            $content | Add-Content -Path $OutputPath -Encoding UTF8
        }
        
        # Add spacing between files
        "`n`n" | Add-Content -Path $OutputPath -Encoding UTF8
        
        Write-ColoredOutput "  ✓ Processed: $relativePath" "Green"
        
    } catch {
        Write-ColoredOutput "  ✗ Error processing $relativePath : $($_.Exception.Message)" "Red"
    }
}

# Complete the progress bar
Write-Progress -Activity "Consolidating CSS Files" -Completed

# Add footer to output file
$footer = @"

/*
=============================================================================
                            END OF CONSOLIDATED CSS
=============================================================================
Consolidation completed on: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Total files processed: $fileCount
Output file: $OutputPath
=============================================================================
*/
"@

$footer | Add-Content -Path $OutputPath -Encoding UTF8

# Get output file size
$outputFileInfo = Get-Item $OutputPath
$outputSizeKB = [math]::Round($outputFileInfo.Length / 1KB, 2)

# Summary
Write-ColoredOutput ""
Write-ColoredOutput "=== CONSOLIDATION COMPLETE ===" "Green"
Write-ColoredOutput "Files processed: $fileCount" "White"
Write-ColoredOutput "Output file: $OutputFile" "White"
Write-ColoredOutput "Output size: $outputSizeKB KB" "White"
Write-ColoredOutput ""
Write-ColoredOutput "Consolidated CSS saved to: $OutputPath" "Cyan"

# Optional: Open the output file
$openFile = Read-Host "Would you like to open the consolidated file? (y/n)"
if ($openFile -eq 'y' -or $openFile -eq 'Y') {
    Start-Process $OutputPath
}