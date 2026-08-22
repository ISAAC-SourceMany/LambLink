$ErrorActionPreference = "Stop"
Write-Host "Restoring..."
dotnet restore .\ChzzkOfTheLamb.sln
Write-Host "Building..."
dotnet build .\ChzzkOfTheLamb.sln -c Debug --no-restore
