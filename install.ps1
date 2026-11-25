<#
.SYNOPSIS
Installs LCP Code on Windows from a GitHub release or an Azure Artifacts feed.

.DESCRIPTION
Installs the lcpcode command line in %LOCALAPPDATA%\Programs\lcpcode and adds it to the user's
PATH; -Desktop also installs the desktop app. Works in Windows PowerShell 5.1 and PowerShell 7.
Each parameter can also come from an environment variable (in brackets below).
A private repository needs a token: GITHUB_TOKEN or GH_TOKEN, or a signed-in `gh`.
An Azure Artifacts feed needs the Azure CLI with the azure-devops extension, signed in with
`az login` or a token in AZURE_DEVOPS_EXT_PAT.

.PARAMETER Version
Release to install, without the "v" [LCPCODE_VERSION]. Default: the latest.

.PARAMETER InstallDir
Folder for lcpcode.exe [LCPCODE_INSTALL_DIR].

.PARAMETER Desktop
Also install the desktop app (per user, silently).

.PARAMETER Repo
GitHub repository [LCPCODE_REPO]. Default: erocafort/lcp-code.

.PARAMETER BaseUrl
Download from this URL or folder instead of GitHub, e.g. a bucket, a mirror or a share
[LCPCODE_BASE_URL]. Its v<version> subfolder (or latest without -Version) is used when it has one.

.PARAMETER AzureFeed
Download the universal package "lcpcode" from this Azure Artifacts feed: "feed" or "project/feed"
[LCPCODE_AZURE_FEED]. Needs -AzureOrg.

.PARAMETER AzureOrg
Azure DevOps organization, e.g. https://dev.azure.com/company [LCPCODE_AZURE_ORG].

.PARAMETER NoModifyPath
Do not add the folder to the user's PATH.

.EXAMPLE
.\install.ps1 -Desktop
#>
[CmdletBinding()]
param(
    [string]$Version = $env:LCPCODE_VERSION,
    [string]$InstallDir = $(if ($env:LCPCODE_INSTALL_DIR) { $env:LCPCODE_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\lcpcode' }),
    [switch]$Desktop,
    [string]$Repo = $(if ($env:LCPCODE_REPO) { $env:LCPCODE_REPO } else { 'erocafort/lcp-code' }),
    [string]$BaseUrl = $env:LCPCODE_BASE_URL,
    [string]$AzureFeed = $env:LCPCODE_AZURE_FEED,
    [string]$AzureOrg = $env:LCPCODE_AZURE_ORG,
    [switch]$NoModifyPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is much faster without the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Fail([string]$msg) { throw "lcpcode install: $msg" }

if ($Version) { $Version = $Version.TrimStart('v') }
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'amd64' }

if (-not $BaseUrl -and -not $AzureFeed) { $BaseUrl = '' } # release: default base URL

$token = if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }
if (-not $BaseUrl -and -not $AzureFeed -and -not $token -and (Get-Command gh -ErrorAction SilentlyContinue)) {
    try { $token = (& gh auth token 2>$null) } catch { $token = $null }
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("lcpcode-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$script:release = $null

# Downloads one release asset to $dest.
function Get-Asset([string]$name, [string]$dest) {
    try {
        if ($BaseUrl) {
            $src = $BaseUrl.TrimEnd('/', '\') + '/' + $name
            if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src $dest } else { Invoke-WebRequest -UseBasicParsing -Uri $src -OutFile $dest }
        } elseif ($token) {
            $h = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }
            if (-not $script:release) {
                $which = if ($Version) { "tags/v$Version" } else { 'latest' }
                $script:release = Invoke-RestMethod -UseBasicParsing -Headers $h -Uri "https://api.github.com/repos/$Repo/releases/$which"
            }
            $asset = $script:release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
            if (-not $asset) { Fail "the release has no $name" }
            $h.Accept = 'application/octet-stream'
            Invoke-WebRequest -UseBasicParsing -Headers $h -Uri $asset.url -OutFile $dest
        } else {
            $path = if ($Version) { "download/v$Version" } else { 'latest/download' }
            Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/$Repo/releases/$path/$name" -OutFile $dest
        }
    } catch {
        if ($_.Exception.Message -like 'lcpcode install:*') { throw }
        Fail "cannot download ${name}: $($_.Exception.Message) (a private repository needs GITHUB_TOKEN or a signed-in gh)"
    }
}

# Checks $file against checksums.txt.
function Test-Asset([string]$name, [string]$file) {
    $line = Get-Content (Join-Path $tmp 'checksums.txt') | Where-Object { $_ -match ('^[0-9a-f]{64} [ *]' + [regex]::Escape($name) + '$') } | Select-Object -First 1
    if (-not $line) { Fail "$name is not in checksums.txt" }
    $want = $line.Substring(0, 64)
    $got = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
    if ($got -ne $want) { Fail "checksum mismatch for $name" }
}

try {
    if ($AzureFeed) {
        if (-not $AzureOrg) { Fail '-AzureFeed needs -AzureOrg (e.g. https://dev.azure.com/company)' }
        if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Fail 'downloading from Azure Artifacts needs the Azure CLI (az)' }
        $pkg = Join-Path $tmp 'pkg'
        $azArgs = @('artifacts', 'universal', 'download', '--organization', $AzureOrg, '--name', 'lcpcode',
            '--version', $(if ($Version) { $Version } else { '*' }), '--path', $pkg, '--only-show-errors')
        $feedParts = $AzureFeed -split '/', 2
        if ($feedParts.Count -eq 2) { $azArgs += '--project', $feedParts[0], '--scope', 'project', '--feed', $feedParts[1] } else { $azArgs += '--feed', $AzureFeed }
        Write-Host "Downloading LCP Code from the feed $AzureFeed..."
        & az @azArgs
        if ($LASTEXITCODE -ne 0) { Fail 'az artifacts universal download failed (sign in with "az login" and add the extension with "az extension add --name azure-devops")' }
        $BaseUrl = $pkg
    }
    $label = if ($Version) { $Version } else { '(latest)' }
    Write-Host "Downloading LCP Code $label for windows/$arch..."
    $sums = Join-Path $tmp 'checksums.txt'
    if ($BaseUrl) {
        # A bucket holds one folder per version plus latest; a plain folder holds one release.
        $root = $BaseUrl.TrimEnd('/', '\')
        $BaseUrl = $root + '/' + $(if ($Version) { "v$Version" } else { 'latest' })
        try { Get-Asset 'checksums.txt' $sums } catch { $BaseUrl = $root; Get-Asset 'checksums.txt' $sums }
    } else {
        Get-Asset 'checksums.txt' $sums
    }
    $archive = "lcpcode_windows_$arch.zip"
    $zip = Join-Path $tmp $archive
    Get-Asset $archive $zip
    Test-Asset $archive $zip
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $tmp 'x') -Force

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $exe = Join-Path $InstallDir 'lcpcode.exe'
    # A running lcpcode.exe cannot be overwritten, but it can be renamed out of the way.
    if (Test-Path -LiteralPath $exe) {
        $old = "$exe.old"
        Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue
        try { Remove-Item -LiteralPath $exe -Force } catch { Rename-Item -LiteralPath $exe -NewName 'lcpcode.exe.old' }
    }
    Copy-Item -LiteralPath (Join-Path $tmp 'x\lcpcode.exe') $exe
    foreach ($doc in 'README.md', 'guia-usuario.md', 'manual-usuario.md') {
        $src = Join-Path $tmp "x\$doc"
        if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src (Join-Path $InstallDir $doc) -Force }
    }
    $installed = (& $exe -version)
    Write-Host "Installed $installed in $exe"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { $_ })
    if ($parts -notcontains $InstallDir) {
        if ($NoModifyPath) {
            Write-Host "Add $InstallDir to PATH to run lcpcode from anywhere"
        } else {
            [Environment]::SetEnvironmentVariable('Path', (($parts + $InstallDir) -join ';'), 'User')
            $env:Path = "$env:Path;$InstallDir"
            Write-Host "Added $InstallDir to your PATH; open a new terminal to use it"
        }
    }

    if ($Desktop) {
        $pattern = if ($arch -eq 'arm64') { 'arm64' } else { 'x64' }
        $sums = Get-Content (Join-Path $tmp 'checksums.txt')
        $name = $sums | ForEach-Object { $_.Substring(66) } | Where-Object { $_ -match "^lcpcode-desktop-.*-win-.*\.exe$" } |
            Sort-Object { if ($_ -match $pattern) { 0 } else { 1 } } | Select-Object -First 1
        if (-not $name) { Fail 'the release has no desktop installer for Windows' }
        $setup = Join-Path $tmp $name
        Get-Asset $name $setup
        Test-Asset $name $setup
        Write-Host "Installing the desktop app ($name)..."
        $p = Start-Process -FilePath $setup -ArgumentList '/S', '/currentuser' -Wait -PassThru
        if ($p.ExitCode -ne 0) { Fail "the desktop installer exited with $($p.ExitCode)" }
        Write-Host 'Installed the desktop app: look for "LCP Code" in the Start menu'
    }

    Write-Host 'Next: run "lcpcode doctor" to check the setup, or open the desktop app.'
} finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
