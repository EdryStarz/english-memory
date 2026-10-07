param([Parameter(Mandatory)][string]$PublishDir,[Parameter(Mandatory)][string]$Assets,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$dest=Join-Path $PublishDir 'Licenses'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/THIRD-PARTY-NOTICES.md" -Destination $dest
$sdkRoot=Split-Path (Get-Command $Dotnet).Source
foreach($file in @('LICENSE.txt','ThirdPartyNotices.txt')){if(Test-Path (Join-Path $sdkRoot $file)){Copy-Item -LiteralPath (Join-Path $sdkRoot $file) -Destination (Join-Path $dest "Dotnet-$file")}}
$data=Get-Content -LiteralPath $Assets -Raw | ConvertFrom-Json -AsHashtable
foreach($library in $data.libraries.Keys){foreach($folder in $data.packageFolders.Keys){$path=Join-Path $folder $library.ToLowerInvariant();if(Test-Path -LiteralPath $path){Get-ChildItem -LiteralPath $path -File | Where-Object {$_.Name -match '(?i)license|notice' -and $_.Extension -in @('.txt','.md','')} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dest (($library.Replace('/','-'))+'-'+$_.Name))};break}}}
