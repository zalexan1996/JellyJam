param([switch]$ToPhone)

$ErrorActionPreference = 'Stop'

# Release ARM64 APK for physical phones (uses the AndroidArm64 publish profile)
.\artifacts\toolchain\dotnet\dotnet.exe publish src/JellyJam.MAUI -c Release -r android-arm64 -p:PublishProfile=AndroidArm64 `
  -p:AndroidSdkDirectory="$PWD/artifacts/toolchain/android-sdk" -p:JavaSdkDirectory="$PWD/artifacts/toolchain/jdk"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Copy the signed APK into artifacts
New-Item -ItemType Directory -Force artifacts/android | Out-Null
Get-ChildItem src/JellyJam.MAUI/bin/Release -Recurse -Filter *-Signed.apk |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
  Copy-Item -Destination artifacts/android/JellyJam-arm64.apk -Force