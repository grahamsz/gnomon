# Load Gnomon's workspace-local development toolchain into this PowerShell session.
$repoRoot = $PSScriptRoot
$toolRoot = Join-Path $repoRoot ".tools"

$env:DOTNET_ROOT = Join-Path $toolRoot "dotnet"
$env:JAVA_HOME = Join-Path $toolRoot "jdk17"
$env:GRADLE_HOME = Join-Path $toolRoot "gradle"
$env:GRADLE_USER_HOME = Join-Path $toolRoot "gradle-home"
$env:ANDROID_HOME = Join-Path $toolRoot "android-sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:ANDROID_USER_HOME = Join-Path $toolRoot "android-user"
$env:KOTLIN_DAEMON_RUN_FILES_PATH = Join-Path $toolRoot "kotlin-daemon"
$env:VIRTUAL_ENV = Join-Path $repoRoot ".venv"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$localPaths = @(
    $env:DOTNET_ROOT
    (Join-Path $env:JAVA_HOME "bin")
    (Join-Path $env:GRADLE_HOME "bin")
    (Join-Path $env:ANDROID_HOME "platform-tools")
    (Join-Path $env:ANDROID_HOME "cmdline-tools\latest\bin")
    (Join-Path $toolRoot "node")
    (Join-Path $env:VIRTUAL_ENV "Scripts")
)

foreach ($directory in @(
    $env:GRADLE_USER_HOME,
    $env:ANDROID_USER_HOME,
    $env:KOTLIN_DAEMON_RUN_FILES_PATH
)) {
    New-Item -ItemType Directory -Force $directory | Out-Null
}

$env:PATH = ($localPaths + $env:PATH) -join [IO.Path]::PathSeparator

Write-Host "Gnomon development environment loaded."
