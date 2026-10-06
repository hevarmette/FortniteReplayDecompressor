using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Models;

namespace FortniteReplayReader.Models;

public class PlayerData
{
    public PlayerData(FortPlayerState playerState)
    {
        Id = playerState.PlayerId is null ? playerState.PlayerID : (int?)playerState.PlayerId;
        EpicId = playerState.UniqueId ?? playerState.UniqueID;
        BotId = playerState.BotUniqueId;
        IsBot = playerState.bIsABot == true;
        PlayerNameCustomOverride = playerState.PlayerNameCustomOverride?.Text;
        IsGameSessionOwner = playerState.bIsGameSessionOwner;
        PlayerNumber = playerState.WorldPlayerId is not null ? (int?)playerState.WorldPlayerId : null;
        StreamerModeName = playerState.StreamerModeName?.Text;
        IsPartyLeader = playerState.PartyOwnerUniqueId == playerState.UniqueId || playerState.PartyOwnerUniqueId == playerState.UniqueID;
        TeamIndex = playerState.TeamIndex;
        Level = playerState.Level;
        SeasonLevelUIDisplay = playerState.SeasonLevelUIDisplay;
        PlatformUniqueNetId = playerState.PlatformUniqueNetId;
        Platform = playerState.Platform;
        HasFinishedLoading = playerState.bHasFinishedLoading;
        HasStartedPlaying = playerState.bHasStartedPlaying;
        IsUsingAnonymousMode = playerState.bUsingAnonymousMode;
        IsUsingStreamerMode = playerState.bUsingStreamerMode;

        Cosmetics = new Cosmetics()
        {
            CharacterBodyType = playerState.CharacterBodyType,
            HeroType = playerState.HeroType?.Name,
            CharacterGender = playerState.CharacterGender
        };
    }

    public int? Id { get; set; }
    public string? PlayerId => (IsBot == true) ? BotId : EpicId ?? PlatformUniqueNetId;
    public string? EpicId { get; set; }
    public string? PlatformUniqueNetId { get; set; }
    public string? BotId { get; set; }
    public bool IsBot { get; set; }
    public string? PlayerName { get; set; }
    public string? PlayerNameCustomOverride { get; set; }
    public string? StreamerModeName { get; set; }
    public string Platform { get; set; }
    public int? Level { get; set; }
    public uint? SeasonLevelUIDisplay { get; set; }

    public uint? InventoryId { get; set; }

    public int? PlayerNumber { get; set; }
    public int? TeamIndex { get; set; }
    public bool IsPartyLeader { get; set; }
    public bool IsReplayOwner { get; set; }
    public bool? IsGameSessionOwner { get; set; }
    public bool? HasFinishedLoading { get; set; }
    public bool? HasStartedPlaying { get; set; }
    public bool? HasThankedBusDriver { get; set; }
    public bool? IsUsingStreamerMode { get; set; }
    public bool? IsUsingAnonymousMode { get; set; }
    public bool? Disconnected { get; internal set; }

    public uint? RebootCounter { get; set; }
    public int? Placement { get; set; }
    public uint? Kills { get; set; }
    public uint? TeamKills { get; set; }
    public int? DeathCause { get; set; }
    public int? DeathCircumstance { get; set; }
    public IEnumerable<string>? DeathTags { get; set; }
    public FVector? DeathLocation { get; set; }
    public float? DeathTime { get; set; }
    public double? DeathTimeDouble { get; set; }
    public Cosmetics Cosmetics { get; set; }
    public uint? CurrentWeapon { get; internal set; }

    public IList<PlayerMovement> Locations { get; set; } = new List<PlayerMovement>();

    // ---- Damage (populated from BatchedDamageCues in Full mode; see FortniteReplayBuilder.UpdateDamageCues) ----
    // Attribution is BY OWNING PAWN CHANNEL (the cue carries no attacker field), so treat as best-effort
    // until validated against the elimination list. See docs/findings/05-damage.md.

    /// <summary>Total damage this player DEALT to other players (sum of player-hit cue magnitudes attributed to them as attacker).</summary>
    public float DamageDealt { get; internal set; }

    /// <summary>Total damage this player TOOK from player sources (sum of cue magnitudes where they were the HitActor).</summary>
    public float DamageTaken { get; internal set; }

    /// <summary>Count of player-hit damage cues attributed to this player as the attacker.</summary>
    public int DamageDealtEventCount { get; internal set; }

    /// <summary>Count of player-hit damage cues where this player was the victim.</summary>
    public int DamageTakenEventCount { get; internal set; }

    /// <summary>Damage this player's shots dealt to the MAP ENVIRONMENT (terrain, cliffs, walls, water, etc.) rather than to a player. In Reload tournaments all combatants are real players, so this is essentially missed/suppressive fire, not combat damage. Kept separate so player-vs-player totals stay clean.</summary>
    public float DamageToEnvironment { get; internal set; }

    /// <summary>Count of damage cues this player dealt to the map environment.</summary>
    public int DamageToEnvironmentEventCount { get; internal set; }

    /// <summary>Individual player-vs-player damage cues this player DEALT, kept for validation/analysis.</summary>
    public IList<DamageEvent> DamageEvents { get; set; } = new List<DamageEvent>();

    // ---- Edits (populated from build-piece EditingPlayer transitions in Debug mode; Task 6) ----

    /// <summary>Number of completed edit interactions attributed to this player (EditingPlayer set then cleared on a build piece).</summary>
    public int EditCount { get; internal set; }

    /// <summary>Edit interactions where a start was seen but no end before the piece/replay ended.</summary>
    public int EditOpenCount { get; internal set; }

    /// <summary>Sum of durations (seconds) of completed edits — only meaningful if start/end pairing is reliable (see findings).</summary>
    public double EditTotalDurationSeconds { get; internal set; }

    /// <summary>Number of edits whose duration was within the plausibility cap and folded into EditTotalDurationSeconds.</summary>
    public int EditTimedCount { get; internal set; }

    /// <summary>Individual completed edit interactions for this player.</summary>
    public IList<EditEvent> EditEvents { get; set; } = new List<EditEvent>();

    // ---- ClientObservedStats (Debug mode; name/value pairs replicated per pawn) ----

    /// <summary>Raw ClientObservedStats seen for this player: StatName -> last StatValue.</summary>
    public IDictionary<string, int> ObservedStats { get; set; } = new Dictionary<string, int>();
}

/// <summary>A completed edit interaction (EditingPlayer set -> cleared on one build piece).</summary>
public class EditEvent
{
    public uint BuildChannel { get; set; }
    public double? StartTime { get; set; }
    public double? EndTime { get; set; }
    public double? DurationSeconds => (StartTime.HasValue && EndTime.HasValue) ? EndTime - StartTime : null;
}

/// <summary>
/// A single player-vs-player damage cue (BatchedDamageCues). Attacker is inferred from the owning pawn
/// channel (the cue has no explicit attacker field); victim is the resolved HitActor.
/// </summary>
public class DamageEvent
{
    public string? VictimEpicId { get; set; }
    public float Magnitude { get; set; }
    public bool IsFatal { get; set; }
    public bool IsCritical { get; set; }
    public bool IsShield { get; set; }
    public bool AttackerResolved { get; set; }
    public bool VictimResolved { get; set; }
    public float? Time { get; set; }
    public double? TimeDouble { get; set; }
    public FVector? Location { get; set; }
}

public class Cosmetics
{
    public int? CharacterGender { get; set; }
    public int? CharacterBodyType { get; set; }
    public string? Parts { get; set; }
    public IEnumerable<string> VariantRequiredCharacterParts { get; set; }
    public string? HeroType { get; set; }
    public string? BannerIconId { get; set; }
    public string? BannerColorId { get; set; }
    public IEnumerable<string> ItemWraps { get; set; }
    public string SkyDiveContrail { get; set; }
    public string Glider { get; set; }
    public string Pickaxe { get; set; }
    public bool? IsDefaultCharacter { get; set; }
    public string Character { get; set; }
    public string Backpack { get; set; }
    public string LoadingScreen { get; set; }
    public IEnumerable<string> Dances { get; set; }
    public string MusicPack { get; set; }
    public string PetSkin { get; set; }
}

public class PlayerMovement
{
    public FRepMovement? ReplicatedMovement { get; set; }
    public float? ReplicatedWorldTimeSeconds { get; set; }

    public double? ReplicatedWorldTimeSecondsDouble { get; set; }

    public float? LastUpdateTime { get; set; }

    public bool? bIsCrouched { get; set; }
    public bool? bIsSprinting { get; set; }
    public bool? bIsJumping { get; set; }
    public bool? bIsSlopeSliding { get; set; }

    public bool? bIsZiplining { get; set; }
    public bool? bIsTargeting { get; set; }

    public bool? bIsDBNO { get; set; }

    public bool? bIsHonking { get; set; }
    public bool? bIsInAnyStorm { get; set; }

    public bool? bIsWaitingForEmoteInteraction { get; set; }
    public bool? bIsPlayingEmote { get; set; }

    public bool? bIsSkydiving { get; set; }
    public bool? bIsSkydivingFromLaunchPad { get; set; }
    public bool? bIsSkydivingFromBus { get; set; }
    public bool? bIsParachuteOpen { get; set; }
    public bool? bIsParachuteForcedOpen { get; set; }
    public bool? bIsInWaterVolume { get; set; }
}
