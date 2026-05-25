<#
.SYNOPSIS
    Creates WebDAV Drive configuration file for Intune/GPO/SCCM deployment.
    Edit the variables in the "CONFIGURATION" section below, then deploy this script via Intune.
    Obtain your license at https://www.userfilesystem.com/download/
#>

# ==============================================================================
# CONFIGURATION — edit these values before deploying
# ==============================================================================

# Integrated Bundle License XML (covers IT Hit User File System + IT Hit WebDAV Client .Net).
$License = '<LICENSE_XML_HERE>'

# One or more drives to mount. Add/remove drive blocks as needed.
# Only set the fields you want to override — remove a line to use the app default.
$Drives = @(
    @{
        WebDAVServerURL        = "https://www.webdavsystem.com/"
        UserFileSystemRootPath = "%USERPROFILE%\WebDAVDrive"
        AutoLockTimeoutMs      = 20000
        ManualLockTimeoutMs    = -1
        SyncIntervalMs         = 10000
        IncomingSyncMode       = "Auto"
        AutoLock               = $true
        SetLockReadOnly        = $true
    }

    # Uncomment and fill in to add a second drive:
    # @{
    #     WebDAVServerURL        = "https://www.webdavsystem.com/personal/"
    #     UserFileSystemRootPath = "%USERPROFILE%\WebDAVPersonal"
    #     IncomingSyncMode       = "Auto"
    #     AutoLock               = $true
    #     SetLockReadOnly        = $true
    # }
)

# ==============================================================================

# Must match "AppID" in appsettings.json.
$AppID = "WebDAVDrive"

$ConfigFile = "$env:ProgramData\$AppID\webdavdrive-config.json"

New-Item -ItemType Directory -Path (Split-Path $ConfigFile) -Force -ErrorAction SilentlyContinue | Out-Null

# Build drives array — only include keys that are present in each hashtable.
$drivesJson = @()
foreach ($d in $Drives) {
    $drive = [ordered]@{}
    foreach ($key in $d.Keys) {
        $drive[$key] = $d[$key]
    }
    $drivesJson += $drive
}

$config = [ordered]@{
    "SettingsVersion" = "2.0"
    "License"         = $License
    "Drives"          = $drivesJson
}

$json = $config | ConvertTo-Json -Depth 10
$json = $json -replace '\\u003c', '<' -replace '\\u003e', '>' -replace '\\u0026', '&' -replace '\\u0027', "'"
$json | Set-Content $ConfigFile -Encoding UTF8

Write-Host "Config saved to: $ConfigFile ($($drivesJson.Count) drive(s))"
