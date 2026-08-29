param(
  [string]$ProjectRoot = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))),
  [string]$SpineRuntimePath = 'D:\CotlOrigin\CultoftheLamb\Project\Assembly-CSharp\lib\spine-unity.dll',
  [string[]]$FormIds = @(),
  [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
  $OutputPath = Join-Path $ProjectRoot 'Follower.preview.json'
}
$atlasPath = Join-Path $ProjectRoot 'Follower.atlas.bytes'
$skeletonPath = Join-Path $ProjectRoot 'Follower.skel.bytes'
foreach ($path in @($SpineRuntimePath, $atlasPath, $skeletonPath)) {
  if (-not (Test-Path -LiteralPath $path)) { throw "Missing preview source: $path" }
}

# The game ships Spine 3.8.99 inside spine-unity.dll. Loading the same runtime is
# important: newer Spine readers cannot be assumed to interpret an old binary in
# exactly the same way, and the browser must receive the game's real setup data.
$spineAssembly = [Reflection.Assembly]::LoadFrom($SpineRuntimePath)
if (-not ('FollowerPreviewNoTextureLoader' -as [type])) {
  Add-Type -TypeDefinition @'
using Spine;
public sealed class FollowerPreviewNoTextureLoader : TextureLoader {
  public void Load(AtlasPage page, string path) { page.rendererObject = path; }
  public void Unload(object texture) { }
}
'@ -ReferencedAssemblies $SpineRuntimePath
  [Reflection.Assembly]::LoadFrom($SpineRuntimePath) | Out-Null
}

$reader = [IO.StreamReader]::new($atlasPath, [Text.Encoding]::UTF8)
try {
  # Spine.Atlas is enumerable, so the unary comma prevents PowerShell from
  # expanding it into AtlasRegion objects before SkeletonBinary receives it.
  $atlasHolder = ,[Spine.Atlas]::new($reader, $ProjectRoot, [FollowerPreviewNoTextureLoader]::new())
}
finally {
  $reader.Dispose()
}
$atlas = $atlasHolder[0]
$binary = [Spine.SkeletonBinary]::new([Spine.Atlas[]]$atlasHolder)
$skeletonData = $binary.ReadSkeletonData($skeletonPath)
if ($skeletonData.Version -ne '3.8.99') {
  throw "Unexpected Spine skeleton version: $($skeletonData.Version)"
}

$skinNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
for ($i = 0; $i -lt $skeletonData.Skins.Count; $i++) {
  [void]$skinNames.Add($skeletonData.Skins.Items[$i].Name)
}

function Test-IsNumberedVariantSkin([string]$skinName) {
  $match = [regex]::Match($skinName, '^(.*?)([2-9]|[1-9][0-9]+)$')
  return $match.Success -and $skinNames.Contains($match.Groups[1].Value)
}

function Get-VariantSkins([string]$formId) {
  $result = [Collections.Generic.List[object]]::new()
  if ($skinNames.Contains($formId)) {
    $result.Add([pscustomobject]@{ VariantId = '0'; SkinName = $formId; SortOrder = 1 })
  }

  $prefixPattern = '^' + [regex]::Escape($formId) + '([2-9]|[1-9][0-9]+)$'
  foreach ($skinName in $skinNames) {
    $match = [regex]::Match($skinName, $prefixPattern)
    if (-not $match.Success) { continue }

    $suffix = 0
    if (-not [int]::TryParse($match.Groups[1].Value, [ref]$suffix)) { continue }
    $result.Add([pscustomobject]@{
      VariantId = [string]($suffix - 1)
      SkinName = $skinName
      SortOrder = $suffix
    })
  }

  return @($result | Sort-Object SortOrder)
}

if ($FormIds.Count -eq 0) {
  # Every top-level Spine skin is a potential follower form. A numbered skin is
  # folded into its existing base (Cat2 -> Cat), while single-variant and named
  # special forms remain valid bases. Slash-qualified component skins and the
  # internal composition skin are not standalone follower forms.
  $FormIds = @($skinNames | Where-Object {
    $_ -notmatch '/' -and
    -not $_.StartsWith('_', [StringComparison]::Ordinal) -and
    -not (Test-IsNumberedVariantSkin $_)
  } | Sort-Object)
}

function Round-Floats([single[]]$values, [int]$digits) {
  $result = [Collections.Generic.List[double]]::new($values.Length)
  foreach ($value in $values) {
    $result.Add([Math]::Round([double]$value, $digits, [MidpointRounding]::AwayFromZero))
  }
  return $result.ToArray()
}

function Is-TintableSlot([string]$slotName) {
  return $slotName -in @(
    'HEAD_SKIN_TOP', 'HEAD_SKIN_BTM', 'MARKINGS',
    'ARM_LEFT_SKIN', 'ARM_RIGHT_SKIN',
    'LEG_LEFT_SKIN', 'LEG_RIGHT_SKIN'
  )
}

$geometryBySignature = @{}
$geometries = [Collections.Generic.List[object]]::new()
$previews = [ordered]@{}
$idle = $skeletonData.FindAnimation('idle')
$outfitSkin = $skeletonData.FindSkin('Clothes/Robes_Lvl1')
if ($null -eq $idle -or $null -eq $outfitSkin) {
  throw 'Follower skeleton is missing idle animation or Clothes/Robes_Lvl1 skin.'
}

foreach ($formId in $FormIds) {
  $variants = [ordered]@{}
  foreach ($variantSkin in @(Get-VariantSkins $formId)) {
    $variantId = $variantSkin.VariantId
    $skinName = $variantSkin.SkinName
    $formSkin = $skeletonData.FindSkin($skinName)
    if ($null -eq $formSkin) { continue }

    # Mirrors FollowerBrain.SetFollowerCostume for a normal follower:
    # character skin first, then Clothes/Robes_Lvl1, setup slots, idle pose.
    $combined = [Spine.Skin]::new("preview/$skinName")
    $combined.AddSkin($formSkin)
    $combined.AddSkin($outfitSkin)
    $skeleton = [Spine.Skeleton]::new($skeletonData)
    $skeleton.SetSkin($combined)
    $skeleton.SetSlotsToSetupPose()
    $idle.Apply($skeleton, 0, 0.32, $true, $null, 1, [Spine.MixBlend]::Replace, [Spine.MixDirection]::In)
    $skeleton.UpdateWorldTransform()

    $layerIds = [Collections.Generic.List[int]]::new()
    $minX = [double]::PositiveInfinity
    $minY = [double]::PositiveInfinity
    $maxX = [double]::NegativeInfinity
    $maxY = [double]::NegativeInfinity

    for ($drawIndex = 0; $drawIndex -lt $skeleton.DrawOrder.Count; $drawIndex++) {
      $slot = $skeleton.DrawOrder.Items[$drawIndex]
      $attachment = $slot.Attachment
      if ($null -eq $attachment -or $attachment.Path -eq 'Other/dummy-clip') { continue }

      $world = $null
      $uvs = $null
      $triangles = $null
      if ($attachment -is [Spine.RegionAttachment]) {
        $world = New-Object single[] 8
        $attachment.ComputeWorldVertices($slot.Bone, $world, 0, 2)
        $uvs = $attachment.UVs
        $triangles = [int[]]@(0, 1, 2, 2, 3, 0)
      }
      elseif ($attachment -is [Spine.MeshAttachment]) {
        $world = New-Object single[] $attachment.WorldVerticesLength
        $attachment.ComputeWorldVertices($slot, $world)
        $uvs = $attachment.UVs
        $triangles = $attachment.Triangles
      }
      else {
        # The normal idle preview contains region and mesh attachments only.
        # Clipping and path attachments have no pixels of their own.
        continue
      }

      for ($v = 0; $v -lt $world.Length; $v += 2) {
        $minX = [Math]::Min($minX, $world[$v])
        $maxX = [Math]::Max($maxX, $world[$v])
        $minY = [Math]::Min($minY, $world[$v + 1])
        $maxY = [Math]::Max($maxY, $world[$v + 1])
      }

      $layer = [ordered]@{
        v = @(Round-Floats $world 3)
        u = @(Round-Floats $uvs 7)
        t = @([int[]]$triangles)
        tint = (Is-TintableSlot $slot.Data.Name)
        a = [Math]::Round([double]($slot.A * $attachment.A), 4)
      }
      $signature = $layer | ConvertTo-Json -Compress -Depth 4
      if (-not $geometryBySignature.ContainsKey($signature)) {
        $geometryBySignature[$signature] = $geometries.Count
        $geometries.Add([pscustomobject]$layer)
      }
      $layerIds.Add([int]$geometryBySignature[$signature])
    }

    if ($layerIds.Count -eq 0) { continue }
    $variants[[string]$variantId] = [pscustomobject][ordered]@{
      skin = $skinName
      bounds = @(
        [Math]::Round($minX, 3), [Math]::Round($minY, 3),
        [Math]::Round($maxX, 3), [Math]::Round($maxY, 3)
      )
      layers = $layerIds.ToArray()
    }
  }
  if ($variants.Count -gt 0) { $previews[$formId] = [pscustomobject]$variants }
}

$page = $atlas.FindRegion('Face/EYE').page
$document = [pscustomobject][ordered]@{
  version = 1
  source = [pscustomobject][ordered]@{
    spineVersion = $skeletonData.Version
    animation = 'idle'
    animationTime = 0.32
    outfit = 'Clothes/Robes_Lvl1'
  }
  atlas = [pscustomobject][ordered]@{ width = $page.width; height = $page.height }
  layers = $geometries.ToArray()
  previews = [pscustomobject]$previews
}

$utf8NoBom = New-Object Text.UTF8Encoding($false)
$json = $document | ConvertTo-Json -Compress -Depth 12
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, $utf8NoBom)
Write-Host "[OK] Follower preview metadata: $OutputPath"
Write-Host "     forms=$($previews.Count), layers=$($geometries.Count), bytes=$((Get-Item -LiteralPath $OutputPath).Length)"
