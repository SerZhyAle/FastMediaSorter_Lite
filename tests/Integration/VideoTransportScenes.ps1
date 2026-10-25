<# Exercises the video bar and exact frame inspection with generated clips.
   The scene uses a unique registry application name and a no-op persistence scheduler;
   it suppresses lifecycle events and deletes only its own isolated profile on exit. #>
param([string]$WorkDir = (Join-Path $env:TEMP 'fms-video-transport-scenes'), [switch]$FrameOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/lib/Verdict.ps1')
trap { Exit-Verdict 'VideoTransportScenes' 2 $_.Exception.Message }
Write-Subject 'VideoTransportScenes' $(if ($FrameOnly) { 'silent frame playback and decoded forward/backward frame identities' } else { 'the modern video viewer: real LibVLC playback, pointer transport, full-screen round trips, menu exit, speeds, auto-hide and frame playback' })
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
$frames = Join-Path $WorkDir 'frames.mkv'
& ffmpeg -hide_banner -loglevel error -f lavfi -i "nullsrc=s=160x90:r=5,geq=r='N*20':g=0:b=0" -f lavfi -i 'anullsrc=r=44100:cl=mono' -t 2 -c:v ffv1 -c:a pcm_s16le -y $frames
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'VideoTransportScenes' 2 'could not generate the frame-identity clip' }
$variable = Join-Path $WorkDir 'frames-vfr.mkv'
& ffmpeg -hide_banner -loglevel error -f lavfi -i "nullsrc=s=160x90:r=20,geq=r='N*20':g=0:b=0" -f lavfi -i 'anullsrc=r=44100:cl=mono' -vf "setpts='N*4+3*mod(N,2)'" -frames:v 10 -t 2.1 -fps_mode vfr -c:v ffv1 -c:a pcm_s16le -y $variable
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'VideoTransportScenes' 2 'could not generate the variable-frame-rate clip' }
& dotnet build (Join-Path $WorkDir 'VideoTransportScenes.csproj') -v:q
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'VideoTransportScenes' 2 'scene harness could not build' }
& (Join-Path $WorkDir 'bin/Debug/net10.0-windows/VideoTransportScenes.exe') $native $clip $frames $(if ($FrameOnly) { 'frames' } else { 'all' }) (Get-Command ffmpeg).Source $variable
$detail = if ($FrameOnly) { 'decoded frame identities, one-second cadence, backward/forward steps, EOF, silence and sound restoration' } else { 'pointer transport, full-screen round trips, menu exit, speeds, auto-hide and decoded forward/backward frame identities' }
Exit-Verdict 'VideoTransportScenes' $LASTEXITCODE "$detail; evidence: $WorkDir"
