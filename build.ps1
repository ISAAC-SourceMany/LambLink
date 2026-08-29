$ErrorActionPreference = "Stop"
function Assert-NativeSuccess([string]$Step) {
  if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}
Write-Host "Restoring..."
dotnet restore .\LambLink.sln
Assert-NativeSuccess 'Solution restore'
Write-Host "Building..."
dotnet build .\LambLink.sln -c Debug --no-restore
Assert-NativeSuccess 'Solution build'
