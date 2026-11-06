# Crea l'installer MSI di Zing in artifacts\. Uso: .\build-installer.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$csproj = Get-Content "$root\src\Zing\Zing.csproj"
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$publish = "$root\artifacts\publish\"
$out = "$root\artifacts"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\src\Zing\Zing.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publish -nologo
if ($LASTEXITCODE -ne 0) { throw "publish fallito" }
Remove-Item "$publish*.xml" -ErrorAction SilentlyContinue

dotnet build "$root\installer\Zing.Installer.wixproj" -c Release -p:Version=$version -p:PublishDir=$publish -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "build MSI fallita" }

Get-Item "$out\Zing-$version-x64.msi" | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }

