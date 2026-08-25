using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ChzzkOfTheLamb.Protocol;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

public sealed class FollowerService(
    ManualLogSource log,
    GameSaveService saves,
    FollowerAppearanceService appearances)
{
    private bool _spawnInProgress;
    private int? _devSpawnGuardFollowerId;
    private int _devSpawnPrimaryInstanceId;
    private float _devSpawnGuardStartedAt;
    private float _devSpawnGuardUntil;
    private float _devSpawnPrimaryGoneAt;
    private float _devSpawnCompletedAt;
    private float _nextDevSpawnCleanupAt;
    private int? _pendingIdentityUiRefreshFollowerId;
    private FollowerAppearanceSelection? _pendingIdentityUiRefreshAppearance;
    private string _pendingIdentityUiRefreshPreviousName = string.Empty;
    private string _pendingIdentityUiRefreshDisplayName = string.Empty;
    private float _pendingIdentityUiRefreshStartedAt;
    private float _pendingIdentityUiRefreshUntil;
    private float _nextIdentityUiRefreshAt;
    private float _pendingIdentityLiveConfirmedAt;
    private int _identityUiRefreshAttempts;
    private readonly HashSet<string> _legacyNameMigrationCompletedSaves = new(StringComparer.Ordinal);
    private string _chzzkMarkerSaveId = "unknown";
    private readonly Dictionary<int, ChzzkFollowerMarker> _chzzkFollowerMarkers = new();
    private readonly HashSet<int> _loggedDecoratedNameplates = new();
    private float _nameplateRefreshUntil;
    private float _nextNameplateRefreshAt;
    private int _nameplateRefreshPasses;

    public FollowerRosterSnapshot BuildRoster()
    {
        var snapshot = new FollowerRosterSnapshot
        {
            SaveId = saves.GetCurrentSaveId(),
            GeneratedAt = DateTimeOffset.UtcNow
        };

        try
        {
            MigrateLegacyFollowerNames(snapshot.SaveId);
            var list = DataManager.Instance?.Followers;
            if (list != null)
            {
                foreach (var info in list)
                {
                    if (info == null) continue;
                    int id;
                    try { id = info.ID; }
                    catch { continue; }
                    if (id <= 0) continue;
                    snapshot.Followers.Add(new FollowerRosterEntry
                    {
                        FollowerId = id,
                        Name = info.Name ?? string.Empty
                    });
                }
            }

            // Include recruits that are currently in the vanilla indoctrination lifecycle as well.
            // They already belong to this save even though they may not have moved into Followers yet.
            var recruits = DataManager.Instance?.Followers_Recruit;
            if (recruits != null)
            {
                foreach (var item in recruits)
                {
                    if (item == null) continue;
                    var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                    var id = ReadFollowerId(info);
                    if (!id.HasValue || id.Value <= 0) continue;
                    snapshot.Followers.Add(new FollowerRosterEntry
                    {
                        FollowerId = id.Value,
                        Name = ReadStringMember(info, "Name") ?? string.Empty
                    });
                }
            }
            snapshot.Followers = snapshot.Followers
                .GroupBy(x => x.FollowerId)
                .Select(g => g.First())
                .OrderBy(x => x.FollowerId)
                .ToList();
            log.LogDebug($"CHZZK follower roster snapshot: save={snapshot.SaveId}, followers={snapshot.Followers.Count}, ids=[{string.Join(",", snapshot.Followers.Select(x => x.FollowerId))}]");
        }
        catch (Exception ex)
        {
            log.LogWarning($"Could not build follower roster snapshot: {ex.Message}");
        }

        return snapshot;
    }

    public FollowerSpawnResult SpawnFromJson(string payloadJson)
    {
        var command = JsonConvert.DeserializeObject<SpawnFollowerCommand>(payloadJson)
                      ?? throw new InvalidOperationException("Invalid SpawnFollower command.");
        return Spawn(command);
    }

    public FollowerSpawnResult Spawn(SpawnFollowerCommand command)
    {
        var result = new FollowerSpawnResult
        {
            ViewerId = command.ViewerId,
            Nickname = command.Nickname,
            SaveId = saves.GetCurrentSaveId()
        };

        try
        {
            if (_spawnInProgress)
                throw new InvalidOperationException("A CHZZK follower spawn is already being processed.");

            if (_devSpawnGuardFollowerId.HasValue)
                throw new InvalidOperationException($"Previous dev spawn follower {_devSpawnGuardFollowerId.Value} is still finishing its indoctrination lifecycle. Wait a few seconds before spawning another test follower.");

            if (PlayerFarming.Instance == null)
                throw new InvalidOperationException("The cult/base scene is not ready for follower creation.");

            // Critical safety guard: CreateNewRecruit must not be called while any recruit is
            // already waiting for indoctrination. Doing so can create two indoctrination flows
            // and eventually crash FollowerManager.RecruitFollower.
            var pending = GetPendingRecruitCount();
            if (pending > 0)
                throw new InvalidOperationException($"A follower recruit is already waiting for indoctrination ({pending} pending). Finish or clear it before starting another CHZZK recruit.");

            var catalog = appearances.BuildCatalog(includeModded: true, includeSpecial: true);
            if (!appearances.Validate(command.Appearance, catalog))
                throw new InvalidOperationException("Selected follower appearance is not valid in the current game catalog.");

            _spawnInProgress = true;
            var beforeIds = GetRecruitInfoIds();

            var position = ((Component)PlayerFarming.Instance).transform.position;
            if (BiomeBaseManager.Instance != null && BiomeBaseManager.Instance.RecruitSpawnLocation != null)
                position = BiomeBaseManager.Instance.RecruitSpawnLocation.transform.position;

            // Keep the game's normal indoctrination lifecycle. FollowerLocation.Base is clearer
            // and safer than relying on a numeric enum cast.
            var recruit = FollowerManager.CreateNewRecruit(FollowerLocation.Base, position);
            if (recruit == null)
                throw new InvalidOperationException("FollowerManager.CreateNewRecruit returned null.");

            // Prefer the FollowerInfo directly owned by the object returned from CreateNewRecruit.
            // Only fall back to a newly-added ID that did not exist before this exact call.
            var info = FollowerAppearanceService.FindFollowerInfo(recruit, 5)
                       ?? FindNewlyAddedRecruitInfo(beforeIds);

            if (info is null)
                throw new InvalidOperationException("Recruit was created, but its FollowerInfo could not be identified safely. The recruit was not renamed to avoid touching a different pending follower.");

            if (!FollowerAppearanceService.TryWrite(info, new[] { "Name" }, command.Nickname))
                throw new InvalidOperationException("FollowerInfo.Name could not be written on this game build.");

            if (!appearances.TryApply(info, command.Appearance))
                throw new InvalidOperationException("Follower appearance could not be applied.");

            result.FollowerId = ReadFollowerId(info);
            result.Success = result.FollowerId.HasValue;
            if (!result.Success)
                result.Error = "Follower was created and named, but persistent FollowerInfo.ID could not be read.";

            if (result.FollowerId.HasValue)
            {
                _devSpawnGuardFollowerId = result.FollowerId.Value;
                _devSpawnPrimaryInstanceId = recruit is UnityEngine.Object unityObj ? unityObj.GetInstanceID() : 0;
                _devSpawnGuardStartedAt = Time.unscaledTime;
                _devSpawnGuardUntil = Time.unscaledTime + 180f;
                _devSpawnPrimaryGoneAt = 0f;
                _devSpawnCompletedAt = 0f;
                CleanupDevSpawnDuplicates(result.FollowerId.Value);
                log.LogInfo($"[DEV SPAWN GUARD] armed followerId={result.FollowerId.Value}, primaryInstance={_devSpawnPrimaryInstanceId}");
            }

            log.LogInfo($"CHZZK follower recruit created: {command.Nickname} (viewer={command.ViewerId}, followerId={result.FollowerId?.ToString() ?? "pending"})");
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            log.LogError($"CHZZK follower creation failed for {command.Nickname}: {ex}");
            return result;
        }
        finally
        {
            _spawnInProgress = false;
        }
    }

    public void Tick()
    {
        ProcessPendingIdentityUiRefresh();
        ProcessPendingNameplateRefresh();

        if (!_devSpawnGuardFollowerId.HasValue) return;

        // During a dev-spawn lifecycle we scan very frequently. The duplicate recruit is
        // created only after the first recruit finishes its dialogue, and a 250ms polling
        // interval was long enough for that duplicate to enter the indoctrination flow.
        if (Time.unscaledTime < _nextDevSpawnCleanupAt) return;
        _nextDevSpawnCleanupAt = Time.unscaledTime + 0.05f;

        var followerId = _devSpawnGuardFollowerId.Value;
        var now = Time.unscaledTime;

        CleanupDevSpawnDuplicates(followerId);

        var matchingSceneRecruits = FindSceneRecruitsByFollowerId(followerId);
        var primaryStillAlive = matchingSceneRecruits.Any(x => x != null && x.GetInstanceID() == _devSpawnPrimaryInstanceId);

        // The old guard released itself as soon as the original FollowerRecruit disappeared.
        // That is too early: COTL can instantiate a second FollowerRecruit with the same ID
        // a moment AFTER the original dialogue completes. Keep the guard alive through this
        // hand-off window instead of clearing it immediately.
        if (!primaryStillAlive && _devSpawnPrimaryGoneAt <= 0f)
        {
            _devSpawnPrimaryGoneAt = now;
            log.LogInfo($"[DEV SPAWN GUARD] primary recruit left scene followerId={followerId}; waiting for late duplicate window.");
        }

        // Once the persistent follower exists, indoctrination has completed. From this point
        // on, ANY FollowerRecruit carrying the same ID is invalid and must be removed before
        // it can show a second indoctrination/assignment sequence.
        Follower? liveFollower = null;
        try { liveFollower = FollowerManager.FindFollowerByID(followerId); } catch { }

        if (liveFollower != null)
        {
            if (_devSpawnCompletedAt <= 0f)
            {
                _devSpawnCompletedAt = now;
                log.LogInfo($"[DEV SPAWN GUARD] indoctrination completed followerId={followerId}; suppressing late duplicate recruits for 20s.");
            }

            foreach (var stale in FindSceneRecruitsByFollowerId(followerId))
            {
                if (stale == null) continue;
                log.LogWarning($"[DEV SPAWN GUARD] destroying post-indoctrination duplicate followerId={followerId}, instance={stale.GetInstanceID()}");
                UnityEngine.Object.Destroy(stale.gameObject);
            }

            // After indoctrination, this ID must no longer remain in Followers_Recruit.
            RemoveDuplicateRecruitRecords(followerId, keepOne: false);

            // Keep watching for a delayed coroutine/object spawn. Do NOT release the guard
            // immediately when the primary object disappears.
            if (now - _devSpawnCompletedAt >= 20f)
            {
                _devSpawnGuardFollowerId = null;
                log.LogInfo($"[DEV SPAWN GUARD] completed followerId={followerId}; late duplicate window closed.");
                return;
            }
        }

        if (now >= _devSpawnGuardUntil)
        {
            log.LogWarning($"[DEV SPAWN GUARD] timed out followerId={followerId}; guard released.");
            _devSpawnGuardFollowerId = null;
        }
    }

    private void CleanupDevSpawnDuplicates(int followerId)
    {
        var recruits = FindSceneRecruitsByFollowerId(followerId);
        foreach (var candidate in recruits)
        {
            if (candidate == null) continue;
            if (_devSpawnPrimaryInstanceId != 0 && candidate.GetInstanceID() == _devSpawnPrimaryInstanceId) continue;
            log.LogWarning($"[DEV SPAWN GUARD] duplicate scene recruit removed followerId={followerId}, instance={candidate.GetInstanceID()}, primary={_devSpawnPrimaryInstanceId}");
            UnityEngine.Object.Destroy(candidate.gameObject);
        }
        RemoveDuplicateRecruitRecords(followerId, keepOne: true);
    }

    private static List<FollowerRecruit> FindSceneRecruitsByFollowerId(int followerId)
    {
        var result = new List<FollowerRecruit>();
        try
        {
            foreach (var recruit in UnityEngine.Object.FindObjectsOfType<FollowerRecruit>())
            {
                if (recruit == null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(recruit, 5);
                if (info != null && ReadFollowerId(info) == followerId) result.Add(recruit);
            }
        }
        catch { }
        return result;
    }

    private static void RemoveDuplicateRecruitRecords(int followerId, bool keepOne)
    {
        try
        {
            var list = DataManager.Instance?.Followers_Recruit as System.Collections.IList;
            if (list == null) return;
            var seen = false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];
                if (item is null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                if (ReadFollowerId(info) != followerId) continue;
                if (keepOne && !seen)
                {
                    seen = true;
                    continue;
                }
                try { list.RemoveAt(i); } catch { }
            }
        }
        catch { }
    }

    private static int GetPendingRecruitCount()
    {
        var ids = new HashSet<int>();
        var count = 0;

        try
        {
            var list = DataManager.Instance?.Followers_Recruit;
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item is null) continue;
                    var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                    var id = ReadFollowerId(info);
                    if (id.HasValue)
                    {
                        if (ids.Add(id.Value)) count++;
                    }
                    else
                    {
                        count++;
                    }
                }
            }
        }
        catch { }

        try
        {
            var sceneRecruits = UnityEngine.Object.FindObjectsOfType<FollowerRecruit>();
            foreach (var recruit in sceneRecruits)
            {
                if (recruit == null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(recruit, 5);
                var id = info == null ? null : ReadFollowerId(info);
                if (id.HasValue)
                {
                    if (ids.Add(id.Value)) count++;
                }
                else if (count == 0)
                {
                    count++;
                }
            }
        }
        catch { }

        return count;
    }

    private static HashSet<int> GetRecruitInfoIds()
    {
        var result = new HashSet<int>();
        try
        {
            var list = DataManager.Instance?.Followers_Recruit;
            if (list == null) return result;
            foreach (var item in list)
            {
                if (item is null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                var id = ReadFollowerId(info);
                if (id.HasValue) result.Add(id.Value);
            }
        }
        catch { }
        return result;
    }

    private static object? FindNewlyAddedRecruitInfo(HashSet<int> beforeIds)
    {
        try
        {
            var list = DataManager.Instance?.Followers_Recruit;
            if (list == null) return null;

            object? onlyNew = null;
            var newCount = 0;
            foreach (var candidate in list)
            {
                if (candidate is null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(candidate, 4) ?? candidate;
                var id = ReadFollowerId(info);
                if (!id.HasValue || beforeIds.Contains(id.Value)) continue;
                onlyNew = info;
                newCount++;
            }

            // Never guess when more than one candidate appears. Ambiguity is safer as a failed
            // command than renaming/indoctrinating the wrong recruit.
            return newCount == 1 ? onlyNew : null;
        }
        catch { return null; }
    }

    private static int? ReadFollowerId(object info)
    {
        var type = info.GetType();
        var p = type.GetProperty("ID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var value = p?.GetValue(info);
        if (value is int pi) return pi;
        var f = type.GetField("ID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        value = f?.GetValue(info);
        return value is int fi ? fi : null;
    }

    public IReadOnlyList<int> GetPendingRecruitIds()
    {
        // DataManager owns the persistent pending-recruit list. Do not perform a global
        // FindObjectsOfType<FollowerRecruit>() scan from the Mod's recurring Update path: on the
        // current Unity/COTL runtime that scan can prevent the main-thread dispatcher from ever
        // reaching GAME_STATUS and catalog commands.
        var result = new HashSet<int>();
        try
        {
            var list = DataManager.Instance?.Followers_Recruit;
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item is null) continue;
                    var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                    var id = ReadFollowerId(info);
                    if (id.HasValue) result.Add(id.Value);
                }
            }
        }
        catch { }

        return result.OrderBy(x => x).ToList();
    }


    public int? ResolveIndoctrinationRecruitId(IEnumerable<object?> args, out string source)
    {
        source = "none";
        try
        {
            // The UI hook arguments identify the recruit directly and are the authoritative,
            // cheapest source. Resolve them before consulting any fallback collection.
            foreach (var arg in args)
            {
                if (arg is null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(arg, 6) ?? arg;
                var id = ReadFollowerId(info);
                if (id.HasValue)
                {
                    source = $"arg:{arg.GetType().FullName}";
                    return id.Value;
                }
            }

            var pending = new HashSet<int>(GetPendingRecruitIds());
            if (pending.Count == 1)
            {
                source = "single-pending-fallback";
                return pending.First();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning($"Failed to resolve indoctrination recruit ID: {ex.Message}");
        }

        return null;
    }

    public RecruitIdentityResult ApplyIdentityFromJson(string payloadJson)
    {
        var command = JsonConvert.DeserializeObject<ApplyRecruitIdentityCommand>(payloadJson)
                      ?? throw new InvalidOperationException("Invalid ApplyRecruitIdentity command.");
        return ApplyIdentity(command);
    }

    public RecruitIdentityResult ApplyIdentity(ApplyRecruitIdentityCommand command)
    {
        var result = new RecruitIdentityResult
        {
            RecruitFollowerId = command.RecruitFollowerId,
            ViewerId = command.ViewerId,
            Nickname = command.Nickname,
            SaveId = saves.GetCurrentSaveId()
        };

        try
        {
            if (PlayerFarming.Instance == null)
                throw new InvalidOperationException("The cult/base scene is not ready.");

            var target = FindPendingRecruitInfo(command.RecruitFollowerId);
            if (target is null)
                throw new InvalidOperationException($"Pending recruit {command.RecruitFollowerId} was not found. It may already have been indoctrinated or removed.");

            var catalog = appearances.BuildCatalog(includeModded: true, includeSpecial: true);
            if (!appearances.Validate(command.Appearance, catalog))
                throw new InvalidOperationException("Selected follower appearance is not valid in the current game catalog.");

            var previousName = ReadStringMember(target, "Name") ?? string.Empty;
            var displayName = BuildFollowerName(command.Nickname);

            if (!FollowerAppearanceService.TryWrite(target, new[] { "Name" }, displayName))
                throw new InvalidOperationException("FollowerInfo.Name could not be written on this game build.");

            if (!appearances.TryApply(target, command.Appearance))
                throw new InvalidOperationException("Follower appearance could not be applied.");

            // Register the marker only after the persistent name has changed. Registering it
            // first makes a visible UIFollowerName compare the new expected nickname with the
            // recruit's old vanilla name and incorrectly discard the marker as a reused ID.
            RememberChzzkFollower(command.RecruitFollowerId, command.ViewerId, command.Nickname, result.SaveId);

            log.LogInfo($"CHZZK identity data written: recruit={command.RecruitFollowerId}, form={command.Appearance?.FormId ?? "<game-default>"}, color={command.Appearance?.ColorId ?? "<game-default>"}, variant={command.Appearance?.VariantId ?? "<game-default>"}, skinName={ReadFirstMemberValue(target, new[] { "SkinName" }) ?? "<null>"}, skinCharacter={ReadFirstMemberValue(target, new[] { "SkinCharacter" }) ?? "<null>"}, skinColour={ReadFirstMemberValue(target, new[] { "SkinColour", "SkinColor" }) ?? "<null>"}, skinVariation={ReadFirstMemberValue(target, new[] { "SkinVariation", "SkinVariant" }) ?? "<null>"}");

            // The indoctrination screen caches form/colour/variant when it is opened. Updating only
            // FollowerInfo changes the final follower, but the open UI can keep showing the old
            // preview until the streamer manually selects an appearance entry. Refresh those live
            // controllers immediately so the raffle winner becomes visible as soon as the draw ends.
            RefreshOpenIndoctrinationUi(target, previousName, displayName, command.RecruitFollowerId);
            ArmIdentityUiRefresh(command.RecruitFollowerId, command.Appearance, previousName, displayName);

            result.Success = true;
            log.LogInfo($"CHZZK raffle identity applied+UI refreshed: recruit={command.RecruitFollowerId}, viewer={command.ViewerId}, nickname={command.Nickname}, displayName={displayName}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            log.LogError($"CHZZK raffle identity apply failed for recruit {command.RecruitFollowerId}: {ex}");
        }

        return result;
    }


    private void ArmIdentityUiRefresh(int followerId, FollowerAppearanceSelection? appearance, string previousName, string displayName)
    {
        _pendingIdentityUiRefreshFollowerId = followerId;
        _pendingIdentityUiRefreshAppearance = appearance;
        _pendingIdentityUiRefreshPreviousName = previousName;
        _pendingIdentityUiRefreshDisplayName = displayName;
        _pendingIdentityUiRefreshStartedAt = Time.unscaledTime;
        _pendingIdentityUiRefreshUntil = Time.unscaledTime + 180.0f;
        _nextIdentityUiRefreshAt = Time.unscaledTime + 0.10f;
        _pendingIdentityLiveConfirmedAt = 0f;
        _identityUiRefreshAttempts = 0;
        log.LogInfo($"[IDENTITY-COMMIT] armed recruit={followerId}, displayName='{displayName}', maxDuration=180s; enforcing through recruit-to-live transition");
    }

    private void ProcessPendingIdentityUiRefresh()
    {
        if (!_pendingIdentityUiRefreshFollowerId.HasValue) return;
        var now = Time.unscaledTime;
        if (now > _pendingIdentityUiRefreshUntil)
        {
            log.LogWarning($"[IDENTITY-COMMIT] timed out recruit={_pendingIdentityUiRefreshFollowerId.Value}, attempts={_identityUiRefreshAttempts}; live follower was not stable for 3s");
            _pendingIdentityUiRefreshFollowerId = null;
            return;
        }
        if (now < _nextIdentityUiRefreshAt) return;
        var initialUiWindow = now - _pendingIdentityUiRefreshStartedAt < 5.0f;
        _nextIdentityUiRefreshAt = now + (initialUiWindow ? 0.20f : 0.75f);

        var followerId = _pendingIdentityUiRefreshFollowerId.Value;
        var recruitTarget = FindPendingRecruitInfo(followerId);
        object? liveTarget = null;
        try
        {
            liveTarget = DataManager.Instance?.Followers?.FirstOrDefault(x => x != null && x.ID == followerId);
        }
        catch { }
        if (recruitTarget == null && liveTarget == null) return;

        try
        {
            // COTL's final indoctrination confirmation can copy the original look/name into a new
            // live FollowerInfo after the raffle result was applied to the recruit object. Enforce
            // the winner identity on both sides of that hand-off until the live object is stable.
            var targets = new List<object>();
            if (recruitTarget != null) targets.Add(recruitTarget);
            if (liveTarget != null && !ReferenceEquals(liveTarget, recruitTarget)) targets.Add(liveTarget);

            foreach (var target in targets)
            {
                appearances.TryApply(target, _pendingIdentityUiRefreshAppearance);
                if (!FollowerAppearanceService.TryWrite(target, new[] { "Name" }, _pendingIdentityUiRefreshDisplayName))
                    log.LogWarning($"[IDENTITY-COMMIT] name write failed recruit={followerId}, targetType={target.GetType().FullName}");
            }

            // Rebuild the open preview only during the short visual convergence window. After
            // that, low-frequency data writes continue without repeated global UI/controller work.
            if (initialUiWindow && recruitTarget != null)
            {
                RefreshOpenIndoctrinationUi(recruitTarget, _pendingIdentityUiRefreshPreviousName, _pendingIdentityUiRefreshDisplayName, followerId);
                RefreshRecruitPreviewObject(followerId, recruitTarget);
                FollowerAppearanceService.TryWrite(recruitTarget, new[] { "Name" }, _pendingIdentityUiRefreshDisplayName);
            }

            _identityUiRefreshAttempts++;

            if (liveTarget != null)
            {
                var liveName = NormalizeLegacyChzzkStoredName(ReadStringMember(liveTarget, "Name") ?? string.Empty);
                if (string.Equals(liveName, _pendingIdentityUiRefreshDisplayName, StringComparison.Ordinal))
                {
                    if (_pendingIdentityLiveConfirmedAt <= 0f)
                    {
                        _pendingIdentityLiveConfirmedAt = now;
                        log.LogInfo($"[IDENTITY-COMMIT] live follower acquired recruit={followerId}, name='{liveName}'; confirming stability for 3s");
                    }
                    else if (now - _pendingIdentityLiveConfirmedAt >= 3.0f)
                    {
                        log.LogInfo($"[IDENTITY-COMMIT] complete recruit={followerId}, name='{liveName}', attempts={_identityUiRefreshAttempts}, liveStableSeconds={(now - _pendingIdentityLiveConfirmedAt):0.0}");
                        _pendingIdentityUiRefreshFollowerId = null;
                    }
                }
                else
                {
                    _pendingIdentityLiveConfirmedAt = 0f;
                    log.LogWarning($"[IDENTITY-COMMIT] live name diverged recruit={followerId}, actual='{liveName}', expected='{_pendingIdentityUiRefreshDisplayName}'; repair will retry");
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning($"Indoctrination live preview retry failed: recruit={followerId}, attempt={_identityUiRefreshAttempts + 1}, error={ex.GetBaseException().Message}");
        }
    }

    private void RefreshRecruitPreviewObject(int followerId, object followerInfo)
    {
        foreach (var recruit in FindSceneRecruitsByFollowerId(followerId))
        {
            if (recruit == null) continue;
            try
            {
                // The recruit object owns the character currently visible on the indoctrination
                // pedestal. Current COTL builds rebuild that graphic from FollowerInfo during
                // CharacterSetupCallback; invoking it mirrors the vanilla refresh path without
                // completing/finalising the recruit.
                var method = AccessTools.Method(recruit.GetType(), "CharacterSetupCallback", Type.EmptyTypes);
                if (method != null)
                {
                    method.Invoke(recruit, null);
                    log.LogDebug($"Indoctrination recruit preview callback invoked: recruit={followerId}, instance={recruit.GetInstanceID()}");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"Indoctrination recruit preview callback failed: recruit={followerId}, error={ex.GetBaseException().Message}");
            }
        }
    }

    private void MigrateLegacyFollowerNames(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId) || string.Equals(saveId, "unknown", StringComparison.OrdinalIgnoreCase)) return;
        if (_legacyNameMigrationCompletedSaves.Contains(saveId)) return;

        var migrated = 0;
        try
        {
            var followers = DataManager.Instance?.Followers;
            if (followers != null)
            {
                foreach (var info in followers)
                {
                    if (info == null) continue;
                    var original = info.Name ?? string.Empty;
                    var cleaned = NormalizeLegacyChzzkStoredName(original);
                    if (string.Equals(original, cleaned, StringComparison.Ordinal)) continue;
                    RememberChzzkFollower(info.ID, string.Empty, cleaned, saveId);
                    info.Name = cleaned;
                    migrated++;
                    log.LogInfo($"CHZZK legacy follower name normalized: save={saveId}, followerId={info.ID}, old='{original}', new='{cleaned}'");
                }
            }

            var recruits = DataManager.Instance?.Followers_Recruit;
            if (recruits != null)
            {
                foreach (var item in recruits)
                {
                    if (item == null) continue;
                    var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                    var original = ReadStringMember(info, "Name") ?? string.Empty;
                    var cleaned = NormalizeLegacyChzzkStoredName(original);
                    if (string.Equals(original, cleaned, StringComparison.Ordinal)) continue;
                    var legacyId = ReadFollowerId(info);
                    if (legacyId.HasValue) RememberChzzkFollower(legacyId.Value, string.Empty, cleaned, saveId);
                    if (FollowerAppearanceService.TryWrite(info, new[] { "Name" }, cleaned))
                    {
                        migrated++;
                        log.LogInfo($"CHZZK legacy recruit name normalized: save={saveId}, followerId={ReadFollowerId(info)?.ToString() ?? "?"}, old='{original}', new='{cleaned}'");
                    }
                }
            }

            _legacyNameMigrationCompletedSaves.Add(saveId);
            if (migrated > 0)
                log.LogInfo($"CHZZK legacy name migration complete: save={saveId}, migrated={migrated}. Clean names are now in the loaded save state and will be persisted by the next vanilla save/autosave.");
            else
                log.LogDebug($"CHZZK legacy name migration complete: save={saveId}, migrated=0.");
        }
        catch (Exception ex)
        {
            // Do not mark complete on failure; the next roster poll will retry.
            log.LogWarning($"CHZZK legacy name migration failed for save={saveId}: {ex.GetBaseException().Message}");
        }
    }

    private static string NormalizeLegacyChzzkStoredName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        const string legacyPrefix = "<color=#00C471>Chzzk</color> ";
        if (normalized.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(legacyPrefix.Length).Trim();
        return normalized;
    }

    private static string BuildFollowerName(string nickname)
    {
        // Never persist TMP rich-text markup in FollowerInfo.Name. COTL reuses follower names in
        // many UI surfaces, and markup plus dynamic Korean glyph generation can unnecessarily
        // expand TMP atlases. The actual save data therefore contains only the viewer nickname.
        return string.IsNullOrWhiteSpace(nickname) ? "CHZZK Viewer" : nickname.Trim();
    }

    private void RefreshOpenIndoctrinationUi(object followerInfo, string previousName, string displayName, int followerId)
    {
        try
        {
            var refreshed = new List<string>();

            // Form must be refreshed first because changing a form can rebuild the colour/variant lists.
            RefreshAppearanceController(
                "Lamb.UI.UIAppearanceMenuController_Form", followerInfo, followerId,
                "_cachedForm", new[] { "SkinCharacter" },
                new[] { "ApplyCachedSettings", "UpdateSelection" }, refreshed);

            RefreshAppearanceController(
                "Lamb.UI.UIAppearanceMenuController_Colour", followerInfo, followerId,
                "_cachedColour", new[] { "SkinColour", "SkinColor", "Colour", "Color" },
                new[] { "ApplyCachedSettings", "UpdateColourSelection" }, refreshed);

            RefreshAppearanceController(
                "Lamb.UI.UIAppearanceMenuController_Variant", followerInfo, followerId,
                "_cachedVariant", new[] { "SkinVariation", "SkinVariant", "Variant" },
                new[] { "ApplyCachedSettings", "UpdateVariantSelection" }, refreshed);

            RefreshVisibleNameFields(previousName, displayName);
            log.LogInfo($"Indoctrination UI refresh complete: recruit={followerId}, controllers=[{string.Join(",", refreshed)}], name='{displayName}', platformBadge=none (village nameplate only)");
        }
        catch (Exception ex)
        {
            // Data was already applied successfully. UI refresh is best-effort and must never make
            // the follower assignment fail or block the vanilla indoctrination flow.
            log.LogWarning($"Indoctrination UI refresh failed for recruit {followerId}: {ex}");
        }
    }

    private void RefreshAppearanceController(
        string typeName,
        object followerInfo,
        int followerId,
        string cacheFieldName,
        string[] valueNames,
        string[] methods,
        List<string> refreshed)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            log.LogWarning($"Indoctrination UI refresh: type not found: {typeName}");
            return;
        }

        UnityEngine.Object[] instances;
        try { instances = UnityEngine.Object.FindObjectsOfType(type); }
        catch (Exception ex)
        {
            log.LogWarning($"Indoctrination UI refresh: cannot enumerate {typeName}: {ex.Message}");
            return;
        }

        foreach (var instance in instances)
        {
            if (instance == null) continue;
            var controller = (object)instance;

            var followerField = AccessTools.Field(type, "_follower");
            var controllerFollower = followerField?.GetValue(controller);
            if (controllerFollower != null)
            {
                var info = FollowerAppearanceService.FindFollowerInfo(controllerFollower, 4) ?? controllerFollower;
                var id = ReadFollowerId(info);
                if (id.HasValue && id.Value != followerId) continue;
            }

            // Re-run the controller's vanilla show-start lifecycle against the already-mutated
            // FollowerInfo. This repopulates the visible item lists and selection state immediately,
            // which is more reliable than changing only the private cached value.
            try
            {
                var component = instance as Component;
                if (component == null || component.gameObject.activeInHierarchy)
                {
                    var onShowStarted = AccessTools.Method(type, "OnShowStarted", Type.EmptyTypes);
                    onShowStarted?.Invoke(controller, null);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"Indoctrination UI refresh: {type.Name}.OnShowStarted failed: {ex.GetBaseException().Message}");
            }

            var cacheField = AccessTools.Field(type, cacheFieldName);
            if (cacheField != null)
            {
                var value = ReadFirstMemberValue(followerInfo, valueNames);
                if (value != null)
                {
                    try
                    {
                        var converted = ConvertForMember(value, cacheField.FieldType);
                        cacheField.SetValue(controller, converted);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning($"Indoctrination UI refresh: {type.Name}.{cacheFieldName} set failed: {ex.Message}");
                    }
                }
            }

            foreach (var methodName in methods)
            {
                try
                {
                    var method = AccessTools.Method(type, methodName, Type.EmptyTypes);
                    method?.Invoke(controller, null);
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Indoctrination UI refresh: {type.Name}.{methodName} failed: {ex.GetBaseException().Message}");
                }
            }

            refreshed.Add(type.Name);
        }
    }

    public void SyncChzzkFollowerMarkersFromJson(string payloadJson)
    {
        var sync = JsonConvert.DeserializeObject<ChzzkFollowerMarkerSync>(payloadJson)
                   ?? new ChzzkFollowerMarkerSync();
        SyncChzzkFollowerMarkers(sync);
    }

    public void SyncChzzkFollowerMarkers(ChzzkFollowerMarkerSync sync)
    {
        if (sync == null) return;
        var saveId = string.IsNullOrWhiteSpace(sync.SaveId) ? "unknown" : sync.SaveId;
        _chzzkMarkerSaveId = saveId;
        _chzzkFollowerMarkers.Clear();
        _loggedDecoratedNameplates.Clear();

        foreach (var marker in sync.Followers ?? new List<ChzzkFollowerMarker>())
        {
            if (marker == null || marker.FollowerId <= 0) continue;
            marker.Nickname = NormalizeLegacyChzzkStoredName(marker.Nickname);
            _chzzkFollowerMarkers[marker.FollowerId] = marker;
        }

        log.LogInfo($"CHZZK follower markers synced: save={saveId}, count={_chzzkFollowerMarkers.Count}, ids=[{string.Join(",", _chzzkFollowerMarkers.Keys.OrderBy(x => x))}]");
        ArmVisibleNameplateRefresh("marker-sync");
    }

    private void RememberChzzkFollower(int followerId, string viewerId, string nickname, string saveId)
    {
        if (followerId <= 0) return;
        if (!string.Equals(_chzzkMarkerSaveId, saveId, StringComparison.Ordinal))
        {
            _chzzkMarkerSaveId = saveId;
            _chzzkFollowerMarkers.Clear();
            _loggedDecoratedNameplates.Clear();
        }

        _chzzkFollowerMarkers[followerId] = new ChzzkFollowerMarker
        {
            FollowerId = followerId,
            ViewerId = viewerId ?? string.Empty,
            Nickname = NormalizeLegacyChzzkStoredName(nickname)
        };
        _loggedDecoratedNameplates.Remove(followerId);
        ArmVisibleNameplateRefresh($"identity-applied:{followerId}");
    }

    public void RefreshFollowerNameplate(object uiFollowerName)
    {
        if (uiFollowerName == null) return;

        var uiType = uiFollowerName.GetType();
        var nameTextField = AccessTools.Field(uiType, "nameText");
        var nameText = nameTextField?.GetValue(uiFollowerName);
        if (nameText == null) return;

        var saveId = saves.GetCurrentSaveId();
        if (!string.Equals(saveId, _chzzkMarkerSaveId, StringComparison.Ordinal))
        {
            SetChzzkBadgeActive(nameText, false);
            return;
        }

        var followerField = AccessTools.Field(uiType, "follower");
        var follower = followerField?.GetValue(uiFollowerName);
        if (follower == null)
        {
            SetChzzkBadgeActive(nameText, false);
            return;
        }

        var info = FollowerAppearanceService.FindFollowerInfo(follower, 5) ?? follower;
        var followerId = ReadFollowerId(info);
        if (!followerId.HasValue || !_chzzkFollowerMarkers.TryGetValue(followerId.Value, out var marker))
        {
            SetChzzkBadgeActive(nameText, false);
            return;
        }

        var plainName = NormalizeLegacyChzzkStoredName(ReadStringMember(info, "Name") ?? marker.Nickname);
        var expectedName = NormalizeLegacyChzzkStoredName(marker.Nickname);
        if (!string.IsNullOrWhiteSpace(expectedName) &&
            !string.Equals(plainName, expectedName, StringComparison.Ordinal))
        {
            // A follower ID can be reused, and an unsaved raffle legitimately rolls back when the
            // game exits. Never trust an ID-only marker enough to rename game data or decorate a
            // different follower. Companion reconciliation will delete the stale mapping.
            _chzzkFollowerMarkers.Remove(followerId.Value);
            _loggedDecoratedNameplates.Remove(followerId.Value);
            SetChzzkBadgeActive(nameText, false);
            log.LogWarning($"CHZZK nameplate marker dropped: followerId={followerId.Value}, expectedName='{expectedName}', actualName='{plainName}' (ID appears reused or raffle result was not saved)");
            return;
        }

        try
        {
            // Render the platform marker in the same TMP text object as the visible name.
            // The previous child-object approach depended on layout width, anchors, masks, and
            // sibling ordering; COTL could report a valid badge while still clipping it. The
            // persistent FollowerInfo.Name remains plain, and every vanilla SetText call is
            // postfixed so this presentation-only prefix is restored deterministically.
            ApplyChzzkInlineName(nameText, plainName);

            if (_loggedDecoratedNameplates.Add(followerId.Value))
                log.LogInfo($"[NAMEPLATE][INLINE-APPLIED] followerId={followerId.Value}, vanillaName='{plainName}', renderedPrefix='Chzzk', color=#00C471, saveNameUntouched=true");
        }
        catch (Exception ex)
        {
            log.LogWarning($"[NAMEPLATE][INLINE-FAILED] followerId={followerId.Value}, name='{plainName}', error={ex.GetBaseException().Message}");
        }
    }

    private const string ChzzkBadgeObjectName = "CHZZK_PlatformBadge";
    private const string ChzzkInlinePrefix = "<color=#00C471>Chzzk</color> ";

    private static void ApplyChzzkInlineName(object nameText, string plainName)
    {
        SetChzzkBadgeActive(nameText, false); // Disable an RC5-RC26 child badge if it exists.
        SetTextProperty(nameText, "richText", true);
        var rendered = ChzzkInlinePrefix + EscapeTmpRichText(plainName);
        SetTextProperty(nameText, "text", rendered);

        var actual = ReadStringProperty(nameText, "text");
        if (!string.Equals(actual, rendered, StringComparison.Ordinal))
            throw new InvalidOperationException($"TMP text write did not persist (actual='{actual ?? "<null>"}')");
    }

    private static string EscapeTmpRichText(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static void SetChzzkBadgeActive(object nameTextOrBadge, bool active)
    {
        if (nameTextOrBadge is not UnityEngine.Component component) return;
        if (string.Equals(component.gameObject.name, ChzzkBadgeObjectName, StringComparison.Ordinal))
        {
            component.gameObject.SetActive(active);
            return;
        }

        var sourceRect = component.transform as UnityEngine.RectTransform;
        var child = sourceRect?.Find(ChzzkBadgeObjectName);
        if (child != null) child.gameObject.SetActive(active);
    }

    private static void SetTextProperty(object target, string propertyName, object value)
    {
        try
        {
            var property = AccessTools.Property(target.GetType(), propertyName);
            if (property?.CanWrite == true) property.SetValue(target, value, null);
        }
        catch { }
    }

    private static string? ReadStringProperty(object target, string propertyName)
    {
        try
        {
            var property = AccessTools.Property(target.GetType(), propertyName);
            if (property?.CanRead != true) return null;
            return property.GetValue(target, null)?.ToString();
        }
        catch { return null; }
    }

    private void ArmVisibleNameplateRefresh(string reason)
    {
        _nameplateRefreshUntil = Time.unscaledTime + 2.0f;
        _nextNameplateRefreshAt = Time.unscaledTime + 0.05f;
        _nameplateRefreshPasses = 0;
        RegenerateVisibleFollowerNameplates();
        log.LogInfo($"[NAMEPLATE][REFRESH] armed reason={reason}, duration=2s");
    }

    private void ProcessPendingNameplateRefresh()
    {
        if (_nameplateRefreshUntil <= 0f) return;
        var now = Time.unscaledTime;
        if (now > _nameplateRefreshUntil)
        {
            log.LogInfo($"[NAMEPLATE][REFRESH] finished passes={_nameplateRefreshPasses}");
            _nameplateRefreshUntil = 0f;
            return;
        }
        if (now < _nextNameplateRefreshAt) return;

        _nextNameplateRefreshAt = now + 0.20f;
        _nameplateRefreshPasses++;
        RegenerateVisibleFollowerNameplates();
    }

    private void RegenerateVisibleFollowerNameplates()
    {
        var type = AccessTools.TypeByName("UIFollowerName");
        if (type == null) return;
        UnityEngine.Object[] instances;
        try { instances = UnityEngine.Object.FindObjectsOfType(type); }
        catch { return; }

        var setText = AccessTools.Method(type, "SetText", Type.EmptyTypes);
        foreach (var instance in instances)
        {
            if (instance == null) continue;
            try
            {
                // SetText is Harmony-postfixed by FollowerNameplatePatch, so this single call
                // restores vanilla text first and then decorates it once. Never call Regenerate
                // or RegenerateLabels from CHZZK code.
                setText?.Invoke(instance, null);
            }
            catch { }
        }
    }

    private void RefreshVisibleNameFields(string previousName, string displayName)
    {
        // The open indoctrination menu owns a TMP input field for the follower name. Update only
        // fields whose current text still matches the recruit's pre-raffle name, avoiding unrelated UI.
        foreach (var typeName in new[] { "TMPro.TMP_InputField", "UnityEngine.UI.InputField" })
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) continue;
            UnityEngine.Object[] fields;
            try { fields = UnityEngine.Object.FindObjectsOfType(type); }
            catch { continue; }

            var textProperty = AccessTools.Property(type, "text");
            if (textProperty == null || !textProperty.CanRead || !textProperty.CanWrite) continue;

            foreach (var field in fields)
            {
                if (field == null) continue;
                try
                {
                    var current = textProperty.GetValue(field, null) as string;
                    if (!string.Equals(current, previousName, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(previousName))
                        continue;
                    textProperty.SetValue(field, displayName, null);
                }
                catch { }
            }
        }
    }

    private static object? ReadFirstMemberValue(object target, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var type = target.GetType();
            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanRead)
            {
                try { return prop.GetValue(target, null); } catch { }
            }
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try { return field.GetValue(target); } catch { }
            }
        }
        return null;
    }

    private static string? ReadStringMember(object target, string name)
        => ReadFirstMemberValue(target, new[] { name })?.ToString();

    private static object? ConvertForMember(object value, Type targetType)
    {
        var effective = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effective.IsInstanceOfType(value)) return value;
        if (effective.IsEnum)
        {
            if (value is string text) return Enum.Parse(effective, text, true);
            return Enum.ToObject(effective, Convert.ToInt32(value));
        }
        return Convert.ChangeType(value, effective);
    }

    private static object? FindPendingRecruitInfo(int followerId)
    {
        try
        {
            var list = DataManager.Instance?.Followers_Recruit;
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item is null) continue;
                    var info = FollowerAppearanceService.FindFollowerInfo(item, 4) ?? item;
                    if (ReadFollowerId(info) == followerId) return info;
                }
            }
        }
        catch { }

        try
        {
            foreach (var recruit in UnityEngine.Object.FindObjectsOfType<FollowerRecruit>())
            {
                if (recruit == null) continue;
                var info = FollowerAppearanceService.FindFollowerInfo(recruit, 5);
                if (info != null && ReadFollowerId(info) == followerId) return info;
            }
        }
        catch { }

        return null;
    }

}
