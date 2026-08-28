param(
  [Parameter(Mandatory=$true)][string]$CompanionClientId,
  [Parameter(Mandatory=$true)][string]$ViewerClientId,
  [string]$Profile = "",
  [string]$Region = "ap-northeast-2"
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CompanionClientId) -or [string]::IsNullOrWhiteSpace($ViewerClientId)) {
  throw 'Both staging CHZZK client IDs are required.'
}
if (-not (Get-Command aws -ErrorAction SilentlyContinue)) { throw 'AWS CLI is not installed.' }

$awsCommon = @('--region', $Region)
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $awsCommon += @('--profile', $Profile) }
& aws @awsCommon sts get-caller-identity --output json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'AWS authentication failed.' }

$companionSecret = Read-Host 'Staging Companion CHZZK Client Secret' -AsSecureString
$viewerSecret = Read-Host 'Staging viewer-page CHZZK Client Secret' -AsSecureString
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('cotl-staging-secrets-' + [guid]::NewGuid().ToString('N'))
$companionFile = Join-Path $tempRoot 'companion.json'
$viewerFile = Join-Path $tempRoot 'viewer.json'
$companionPlain = $null
$viewerPlain = $null

function ConvertTo-PlainText([Security.SecureString]$Value) {
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
  try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Set-Secret([string]$Name, [string]$JsonFile) {
  # A first-time describe-secret returns ResourceNotFoundException on stderr.
  # Windows PowerShell can promote that expected native stderr to a terminating
  # NativeCommandError under ErrorActionPreference=Stop, so use a successful
  # filtered list operation and compare exact names instead.
  $namesJson = & aws @awsCommon secretsmanager list-secrets --filters "Key=name,Values=$Name" --query 'SecretList[].Name' --output json
  if ($LASTEXITCODE -ne 0) { throw "Failed to inspect Secrets Manager: $Name" }
  $exists = $false
  foreach ($existingName in ($namesJson | ConvertFrom-Json)) {
    if ([string]::Equals([string]$existingName, $Name, [StringComparison]::Ordinal)) {
      $exists = $true
      break
    }
  }
  $fileUri = 'file://' + $JsonFile.Replace('\', '/')
  if ($exists) {
    & aws @awsCommon secretsmanager put-secret-value --secret-id $Name --secret-string $fileUri --output json | Out-Null
  }
  else {
    & aws @awsCommon secretsmanager create-secret --name $Name --description 'COTL staging CHZZK OAuth credential' --secret-string $fileUri --output json | Out-Null
  }
  if ($LASTEXITCODE -ne 0) { throw "Failed to store Secrets Manager value: $Name" }
}

try {
  New-Item -ItemType Directory -Path $tempRoot | Out-Null
  $companionPlain = ConvertTo-PlainText $companionSecret
  $viewerPlain = ConvertTo-PlainText $viewerSecret
  if ([string]::IsNullOrWhiteSpace($companionPlain) -or [string]::IsNullOrWhiteSpace($viewerPlain)) {
    throw 'Client Secret cannot be empty.'
  }

  & aws @awsCommon ssm put-parameter --name '/cotl/staging/chzzk/companion/client-id' --type String --value $CompanionClientId.Trim() --overwrite --output json | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Failed to store staging Companion Client ID.' }
  & aws @awsCommon ssm put-parameter --name '/cotl/staging/chzzk/mylamb/client-id' --type String --value $ViewerClientId.Trim() --overwrite --output json | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Failed to store staging viewer Client ID.' }

  $utf8NoBom = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($companionFile, (@{clientSecret=$companionPlain} | ConvertTo-Json -Compress), $utf8NoBom)
  [IO.File]::WriteAllText($viewerFile, (@{clientSecret=$viewerPlain} | ConvertTo-Json -Compress), $utf8NoBom)
  Set-Secret '/cotl/staging/chzzk/companion' $companionFile
  Set-Secret '/cotl/staging/chzzk/mylamb' $viewerFile

  Write-Host '[OK] Staging CHZZK credentials stored under /cotl/staging/chzzk/.'
}
finally {
  $companionPlain = $null
  $viewerPlain = $null
  if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
