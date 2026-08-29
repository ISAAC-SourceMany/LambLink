using System;
using System.Collections.Generic;

namespace LambLink.Protocol;

public static class GameMessageTypes
{
    // Companion -> Mod
    // Development-only: creates a brand-new recruit. The production raffle flow uses ApplyRecruitIdentity.
    public const string SpawnFollower = "SPAWN_FOLLOWER";
    public const string ApplyRecruitIdentity = "APPLY_RECRUIT_IDENTITY";
    public const string DonationEffect = "DONATION_EFFECT";
    public const string GetAppearanceCatalog = "GET_FOLLOWER_APPEARANCE_CATALOG";
    public const string GetFollowerRoster = "GET_FOLLOWER_ROSTER";
    public const string GetGameStatus = "GET_GAME_STATUS";
    public const string RaffleRequestAck = "RAFFLE_REQUEST_ACK";
    public const string RaffleRoundClosed = "RAFFLE_ROUND_CLOSED";
    public const string SyncChzzkFollowerMarkers = "SYNC_CHZZK_FOLLOWER_MARKERS";
    public const string Ping = "PING";

    // Mod -> Companion
    public const string GameStatus = "GAME_STATUS";
    public const string FollowerSpawnResult = "FOLLOWER_SPAWN_RESULT";
    public const string RecruitIdentityResult = "RECRUIT_IDENTITY_RESULT";
    public const string AppearanceCatalog = "FOLLOWER_APPEARANCE_CATALOG";
    public const string RaffleRequested = "RAFFLE_REQUESTED";
    public const string FollowerRoster = "FOLLOWER_ROSTER";
    public const string FollowerLifecycle = "FOLLOWER_LIFECYCLE";
    public const string DonationEffectResult = "DONATION_EFFECT_RESULT";
    public const string DonationRuntimeState = "DONATION_RUNTIME_STATE";
}

public sealed class GameCommandEnvelope
{
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    // Local diagnostic correlation. Senders leave these as zero; the receiving Mod assigns
    // them before the command enters Unity's main-thread queue.
    public long DiagnosticSequence { get; set; }
    public long DiagnosticReceivedUnixMs { get; set; }
}

public sealed class FollowerAppearanceSelection
{
    public string FormId { get; set; } = string.Empty;
    public string? VariantId { get; set; }
    public string? ColorId { get; set; }
}

public sealed class SpawnFollowerCommand
{
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public string? RaffleId { get; set; }
    public FollowerAppearanceSelection? Appearance { get; set; }

    public SpawnFollowerCommand() { }

    public SpawnFollowerCommand(string viewerId, string nickname, string saveId, string? raffleId = null, FollowerAppearanceSelection? appearance = null)
    {
        ViewerId = viewerId;
        Nickname = nickname;
        SaveId = saveId;
        RaffleId = raffleId;
        Appearance = appearance;
    }
}

/// <summary>
/// Production flow: apply the raffle winner's identity to the recruit that the game already created.
/// No additional follower is spawned.
/// </summary>
public sealed class ApplyRecruitIdentityCommand
{
    public int RecruitFollowerId { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public string? RaffleId { get; set; }
    public int Generation { get; set; } = 1;
    public string FollowerName { get; set; } = string.Empty;
    public FollowerAppearanceSelection? Appearance { get; set; }
}

public sealed class RecruitIdentityResult
{
    public bool Success { get; set; }
    public int RecruitFollowerId { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public int Generation { get; set; } = 1;
    public string FollowerName { get; set; } = string.Empty;
    public FollowerAppearanceSelection? AppliedAppearance { get; set; }
    public string? RaffleId { get; set; }
    public string? Error { get; set; }
}

public sealed class FollowerSpawnResult
{
    public bool Success { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public int? FollowerId { get; set; }
    public string? Error { get; set; }
}

public sealed class DonationEffectCommand
{
    public string RequestId { get; set; } = string.Empty;
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Effect { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string? Message { get; set; }

    public DonationEffectCommand() { }
    public DonationEffectCommand(string viewerId, string nickname, long amount, string effect, string? message, string? eventName = null, string? requestId = null)
    {
        RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId!;
        ViewerId = viewerId;
        Nickname = nickname;
        Amount = amount;
        Effect = effect;
        EventName = eventName ?? string.Empty;
        Message = message;
    }
}

public sealed class DonationEffectResult
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Retryable { get; set; }
    public string Effect { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Mod -> Companion gameplay gate state for donation delivery and timed effects.
/// The Mod is authoritative because only it can observe Unity scene and gameplay state.
/// </summary>
public sealed class DonationRuntimeStateEvent
{
    public bool IsReady { get; set; }
    public bool TimersPaused { get; set; } = true;
    public string Reason { get; set; } = "STARTING";
    public string Area { get; set; } = "UNKNOWN";
    public string Evidence { get; set; } = string.Empty;
    public int PendingDonations { get; set; }
    public long Revision { get; set; }
}

public sealed class GameStatusEvent
{
    public bool InGame { get; set; }
    public string SaveId { get; set; } = "unknown";
    public string ModVersion { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string Area { get; set; } = "UNKNOWN";
    // True only when the persistent Unity main-thread runtime pump has ticked recently.
    // Companion uses this distinction to avoid reporting a socket-only connection as a
    // ready game integration; a cache fallback may repeat the most recent proven state.
    public bool RuntimePumpActive { get; set; }
    public long RuntimeUpdateCount { get; set; }
    // Backup copy of the donation gate. The dedicated DONATION_RUNTIME_STATE event is
    // sent immediately on transitions; these fields make reconnect/status recovery safe.
    public bool DonationReady { get; set; }
    public bool DonationTimersPaused { get; set; } = true;
    public string DonationPauseReason { get; set; } = "STARTING";
    public int PendingDonationCount { get; set; }
    public long DonationStateRevision { get; set; }
}

public sealed class AppearanceCatalogRequest
{
    public bool IncludeModded { get; set; }
    public bool IncludeSpecial { get; set; }
}

public sealed class FollowerFormDescriptor
{
    public string FormId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; } = true;
    public bool IsSpecial { get; set; }
    public bool IsModded { get; set; }
    public List<string> VariantIds { get; set; } = new();
    public List<string> ColorIds { get; set; } = new();

    // Runtime-exported preview resources. These are generated from the streamer's own
    // installed game at runtime and are never bundled with/distributed inside the mod.
    // The local/AWS backend can decode the base64 PNGs into cache/object storage and
    // replace them with public/signed URLs for the viewer web UI.
    public string? FormPreviewPngBase64 { get; set; }
    public Dictionary<string, string> VariantPreviewPngBase64 { get; set; } = new();
    public Dictionary<string, string> ColorHexById { get; set; } = new();
}

public sealed class FollowerAppearanceCatalog
{
    public string SaveId { get; set; } = "unknown";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<FollowerFormDescriptor> Forms { get; set; } = new();
}


public sealed class FollowerRosterEntry
{
    public int FollowerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDead { get; set; }
    public string? DeathReason { get; set; }
    public FollowerAppearanceSelection? Appearance { get; set; }
}

public sealed class FollowerLifecycleEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string SaveId { get; set; } = "unknown";
    public int FollowerId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FollowerRosterSnapshot
{
    public string SaveId { get; set; } = "unknown";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<FollowerRosterEntry> Followers { get; set; } = new();
}


public sealed class ChzzkFollowerMarker
{
    public int FollowerId { get; set; }
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public sealed class ChzzkFollowerMarkerSync
{
    public string SaveId { get; set; } = "unknown";
    public List<ChzzkFollowerMarker> Followers { get; set; } = new();
}

public sealed class RaffleRequestedEvent
{
    public string Reason { get; set; } = "recruit_available";
    public int RecruitFollowerId { get; set; }
    public string SaveId { get; set; } = "unknown";
}

public sealed class RaffleRequestAck
{
    public int RecruitFollowerId { get; set; }
    public bool Accepted { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Companion -> Mod lifecycle notification. This releases the Mod-side duplicate guard
/// after a raffle is cancelled or finishes without a participant, so reopening the same
/// pending recruit's indoctrination menu can start a new round.
/// </summary>
public sealed class RaffleRoundClosed
{
    public int RecruitFollowerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool AllowRetry { get; set; }
}
