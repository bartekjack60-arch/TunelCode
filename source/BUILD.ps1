$ErrorActionPreference='Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
 $vendor = Join-Path $PSScriptRoot '../vendor/usb'
 foreach ($package in (Get-Content -LiteralPath (Join-Path $vendor 'packages.json') -Raw | ConvertFrom-Json)) {
  $payload = Join-Path $vendor $package.fileName
  if (!(Test-Path -LiteralPath $payload) -or (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $package.sha256) {
   throw "Brak lub uszkodzenie pakietu USB: $($package.fileName). Przywróć vendor/usb z pełnej paczki źródeł."
  }
 }
 & dotnet build AutoTunel.csproj -c Release -o 'dist/AutoTunel'
 if ($LASTEXITCODE -ne 0) { throw 'Kompilacja AutoTunel nie powiodła się.' }
 & dotnet run --project tests/Tests.csproj -c Release
 if ($LASTEXITCODE -ne 0) { throw 'Testy nie powiodły się.' }
 & dotnet run --project tests/Tests.csproj -c Release -- --verify-bundle (Join-Path $PSScriptRoot 'dist/AutoTunel/AutoTunel.exe')
 if ($LASTEXITCODE -ne 0) { throw 'Weryfikacja wbudowanych pakietów USB nie powiodła się.' }
} finally { Pop-Location }
