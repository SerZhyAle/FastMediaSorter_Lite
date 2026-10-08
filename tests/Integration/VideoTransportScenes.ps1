<# Exercises the lower video bar with a generated clip and real LibVLC playback.
   The scene suppresses form lifecycle events so user settings are not loaded or saved. #>
param([string]$WorkDir = (Join-Path $env:TEMP 'fms-video-transport-scenes'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/lib/Verdict.ps1')
trap { Exit-Verdict 'VideoTransportScenes' 2 $_.Exception.Message }
Write-Subject 'VideoTransportScenes' 'the lower video bar of the modern viewer: real LibVLC playback of a generated clip, pause, resume and replay by pointer clicks'
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    Exit-Verdict 'VideoTransportScenes' 2 'ffmpeg is needed to generate the test clip'
}
$native = Join-Path $repo 'bin/ModernPublish/libvlc/win-x64'
if (-not (Test-Path (Join-Path $native 'libvlc.dll'))) {
    Exit-Verdict 'VideoTransportScenes' 2 'build the modern viewer first for its native LibVLC payload'
}
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
$project = [System.Security.SecurityElement]::Escape((Join-Path $repo 'src/Modern/FastMediaSorter.Modern.vbproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="VideoTransportScenes.cs"/><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $WorkDir 'VideoTransportScenes.csproj') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VideoTransportScenes.cs') -Destination $WorkDir -Force
$clip = Join-Path $WorkDir 'clip.mp4'
& ffmpeg -hide_banner -loglevel error -f lavfi -i 'color=c=blue:s=320x180:r=25' -t 4 -c:v mpeg4 -y $clip
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'VideoTransportScenes' 2 'could not generate the test clip' }
& dotnet build (Join-Path $WorkDir 'VideoTransportScenes.csproj') -v:q
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'VideoTransportScenes' 2 'scene harness could not build' }
& (Join-Path $WorkDir 'bin/Debug/net10.0-windows/VideoTransportScenes.exe') $native $clip
Exit-Verdict 'VideoTransportScenes' $LASTEXITCODE "pause, resume and replay through pointer clicks; evidence: $WorkDir"
