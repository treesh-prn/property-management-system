[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageDirectory = Join-Path $repositoryRoot ('artifacts/iis/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$projectPath = Join-Path $repositoryRoot 'PropertyManagementSystem/PropertyManagementSystem.csproj'
$configurationTemplate = Join-Path $repositoryRoot 'docs/deployment/appsettings.Iis.example.json'

# A new package directory prevents stale settings from an older publish being reused.
New-Item -ItemType Directory -Path $packageDirectory -ErrorAction Stop | Out-Null
& dotnet publish $projectPath -c Release --no-restore -p:SanitizePublish=true -p:UseAppHost=false -o $packageDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed. No server was changed.' }

$unexpectedSettings = @(Get-ChildItem -LiteralPath $packageDirectory -Filter 'appsettings*.json' -File | Where-Object { $_.Name -ne 'appsettings.json' })
$publishedSettings = Join-Path $packageDirectory 'appsettings.json'
if ($unexpectedSettings.Count -gt 0 -or (Test-Path -LiteralPath (Join-Path $packageDirectory 'App_Data')) -or
    !(Test-Path -LiteralPath $publishedSettings) -or
    ((Get-FileHash -LiteralPath $publishedSettings).Hash -ne (Get-FileHash -LiteralPath $configurationTemplate).Hash)) {
    throw 'The package contains local settings or runtime data. Do not deploy this package.'
}
Write-Output ('IIS package prepared: ' + $packageDirectory)
Write-Output 'Email is Disabled in this package. Configure the server settings, test in Test mode, then explicitly switch to Production.'
Write-Output 'No files were copied to IIS and no emails were sent.'
