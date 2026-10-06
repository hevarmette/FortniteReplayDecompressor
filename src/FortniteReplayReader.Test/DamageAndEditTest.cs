using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using Unreal.Core.Models;
using Xunit;

namespace FortniteReplayReader.Test;

/// <summary>
/// Tests for the fork's additions: player-vs-player damage (BatchedDamageCues), edit tracking
/// (BaseBuild.EditingPlayer transitions), and ClientObservedStats. Mirrors the synthetic-export style
/// of <see cref="FortniteReplayBuilderTest"/>.
/// </summary>
public class DamageAndEditTest
{
    private readonly FortniteReplayBuilder builder = new();
    private readonly FortniteReplay replay = new();

    /// <summary>
    /// Wire up a player: a PlayerState on <paramref name="stateChannel"/> whose actor GUID is
    /// <paramref name="stateActorGuid"/>, and a pawn on <paramref name="pawnChannel"/> whose actor GUID
    /// is <paramref name="pawnActorGuid"/>. Returns nothing; use the builder to assert.
    /// </summary>
    private void AddPlayer(uint stateChannel, uint stateActorGuid, string epicId, uint pawnChannel, uint pawnActorGuid)
    {
        builder.AddActorChannel(stateChannel, stateActorGuid);
        builder.UpdatePlayerState(stateChannel, new FortPlayerState
        {
            PlayerID = (int)stateChannel,
            UniqueId = epicId,
            BotUniqueId = "",
            bIsABot = false,
            TeamIndex = 1
        });
        builder.AddActorChannel(pawnChannel, pawnActorGuid);
        builder.UpdatePlayerPawn(pawnChannel, new PlayerPawn { PlayerState = stateActorGuid });
    }

    private PlayerData Player(string epicId) => replay.PlayerData.First(p => p.EpicId == epicId);

    [Fact]
    public void PvpDamage_AttributesDealtToAttackerAndTakenToVictim()
    {
        // attacker: state ch1/guid10, pawn ch2/guid20 ; victim: state ch3/guid30, pawn ch4/guid40
        AddPlayer(1, 10, "attacker", 2, 20);
        AddPlayer(3, 30, "victim", 4, 40);

        // Cue is multicast from the attacker's pawn channel (2); HitActor is the victim's pawn GUID (40).
        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 40, Magnitude = 75f, bIsValid = true });
        builder.Build(replay);

        Assert.Equal(75f, Player("attacker").DamageDealt);
        Assert.Equal(1, Player("attacker").DamageDealtEventCount);
        Assert.Equal(75f, Player("victim").DamageTaken);
        Assert.Equal(1, Player("victim").DamageTakenEventCount);
        Assert.Equal(0f, Player("attacker").DamageToEnvironment);
    }

    [Fact]
    public void DamageToUnknownActor_CountsAsEnvironmentNotPvp()
    {
        AddPlayer(1, 10, "attacker", 2, 20);

        // HitActor 999 is not a known player actor -> environment (terrain/wall/etc.), not PvP.
        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 999, Magnitude = 40f, bIsValid = true });
        builder.Build(replay);

        Assert.Equal(0f, Player("attacker").DamageDealt);
        Assert.Equal(40f, Player("attacker").DamageToEnvironment);
        Assert.Equal(1, Player("attacker").DamageToEnvironmentEventCount);
    }

    [Fact]
    public void PvpDamage_DealtEqualsTaken_AcrossManyCues()
    {
        AddPlayer(1, 10, "a", 2, 20);
        AddPlayer(3, 30, "b", 4, 40);

        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 40, Magnitude = 30f, bIsValid = true });
        builder.UpdateDamageCues(4, new BatchedDamageCues { HitActor = 20, Magnitude = 50f, bIsValid = true });
        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 40, Magnitude = 20f, bIsValid = true });
        builder.Build(replay);

        var dealt = replay.PlayerData.Sum(p => p.DamageDealt);
        var taken = replay.PlayerData.Sum(p => p.DamageTaken);
        Assert.Equal(dealt, taken); // closed-system invariant the real-replay analysis relies on
        Assert.Equal(100f, dealt);
    }

    [Fact]
    public void InvalidOrEmptyCue_IsIgnored()
    {
        AddPlayer(1, 10, "a", 2, 20);
        AddPlayer(3, 30, "b", 4, 40);

        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 40, Magnitude = 10f, bIsValid = false });
        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = null, Magnitude = 10f, bIsValid = true });
        builder.UpdateDamageCues(2, new BatchedDamageCues { HitActor = 40, Magnitude = null, bIsValid = true });
        builder.Build(replay);

        Assert.Equal(0f, Player("a").DamageDealt);
        Assert.Equal(0f, Player("b").DamageTaken);
    }

    [Fact]
    public void Edit_StartThenEnd_CountsOneEditWithDuration()
    {
        // EditingPlayer is a PLAYER-STATE actor GUID (not a pawn). editor state ch1/guid10.
        AddPlayer(1, 10, "editor", 2, 20);

        // advance the match clock so start/end have a measurable gap
        builder.UpdateGameState(new GameState { ReplicatedWorldTimeSecondsDouble = 100.0 });
        // edit starts on build-piece channel 50
        builder.UpdateBuild(50, new WoodWall { EditingPlayer = new ActorGuid { Value = 10 } });

        builder.UpdateGameState(new GameState { ReplicatedWorldTimeSecondsDouble = 100.5 });
        // edit ends (EditingPlayer back to 0)
        builder.UpdateBuild(50, new WoodWall { EditingPlayer = new ActorGuid { Value = 0 } });

        builder.Build(replay);

        Assert.Equal(1, Player("editor").EditCount);
        Assert.Equal(1, Player("editor").EditTimedCount);
        Assert.Equal(0.5, Player("editor").EditTotalDurationSeconds, 3);
    }

    [Fact]
    public void Edit_LongDuration_CountedButExcludedFromTimedAggregate()
    {
        AddPlayer(1, 10, "editor", 2, 20);

        builder.UpdateGameState(new GameState { ReplicatedWorldTimeSecondsDouble = 0.0 });
        builder.UpdateBuild(50, new WoodWall { EditingPlayer = new ActorGuid { Value = 10 } });
        // end far beyond the plausibility cap (piece left flagged)
        builder.UpdateGameState(new GameState { ReplicatedWorldTimeSecondsDouble = FortniteReplayBuilder.EditDurationCapSeconds + 60 });
        builder.UpdateBuild(50, new WoodWall { EditingPlayer = new ActorGuid { Value = 0 } });

        builder.Build(replay);

        Assert.Equal(1, Player("editor").EditCount);          // still counted
        Assert.Equal(0, Player("editor").EditTimedCount);      // but not folded into duration
        Assert.Equal(0.0, Player("editor").EditTotalDurationSeconds, 3);
    }

    [Fact]
    public void ClientObservedStats_RecordedPerPlayer()
    {
        AddPlayer(1, 10, "p", 2, 20);

        // Stat RPC arrives on the pawn channel (2).
        builder.UpdateClientObservedStats(2, new FortClientObservedStat { StatName = "Accuracy", StatValue = 42 });
        builder.Build(replay);

        Assert.True(Player("p").ObservedStats.TryGetValue("Accuracy", out var v));
        Assert.Equal(42, v);
    }
}
