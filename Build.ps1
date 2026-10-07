param([string]$Dotnet='dotnet',[string]$Wix='wix',[switch]$Installer)
$ErrorActionPreference='Stop'
$repo=$PSScriptRoot
& $Dotnet publish "$repo/src/EnglishMemory.App/EnglishMemory.App.csproj" -c Release -p:Platform=x64 -o "$repo/release/Windows-x64"
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
& "$repo/PrepareLicenses.ps1" -PublishDir "$repo/release/Windows-x64" -Assets "$repo/src/EnglishMemory.App/obj/project.assets.json" -Dotnet $Dotnet
& $Dotnet run --project "$repo/tests/EnglishMemory.Checks/EnglishMemory.Checks.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Checks failed'}
if($Installer){& $Wix build "$repo/installer/EnglishMemory.wxs" -arch x64 -d "PublishDir=$repo/release/Windows-x64" -out "$repo/EnglishMemory-0.1.4-x64.msi";if($LASTEXITCODE -ne 0){throw 'Installer build failed'}}
