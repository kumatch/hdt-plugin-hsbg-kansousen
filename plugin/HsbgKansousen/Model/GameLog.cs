using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HsbgKansousen.Model
{
    // docs/log-schema.md と 1:1 の DTO。HDT の型には依存しない。
    // プロパティ名は Output/JsonSettings の SnakeCaseNamingStrategy で snake_case に変換される。
    // 「取れなかった」は null（キーを省略しない）。

    public sealed class GameLog
    {
        public const string CurrentSchemaVersion = "1.6.2";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>"complete" | "incomplete" | "not_battlegrounds"</summary>
        public string Status { get; set; } = LogStatus.Incomplete;
        public GeneratorInfo Generator { get; set; } = new GeneratorInfo();
        public GameInfo Game { get; set; } = new GameInfo();
        public PlayerInfo Player { get; set; } = new PlayerInfo();
        public ResultInfo Result { get; set; } = new ResultInfo();
        public List<OpponentRecord> Opponents { get; set; } = new List<OpponentRecord>();
        public List<TurnRecord> Turns { get; set; } = new List<TurnRecord>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public static class LogStatus
    {
        public const string Complete = "complete";
        public const string Incomplete = "incomplete";
        public const string NotBattlegrounds = "not_battlegrounds";
    }

    public sealed class GeneratorInfo
    {
        public string Plugin { get; set; } = "HsbgKansousen";
        public string PluginVersion { get; set; } = "0.0.0";
        public string? HdtVersion { get; set; }
        public int? HearthstoneBuild { get; set; }
    }

    public sealed class GameInfo
    {
        public string GameId { get; set; } = "";
        public string Mode { get; set; } = "battlegrounds";
        /// <summary>"solo" | "duos"</summary>
        public string Variant { get; set; } = "solo";
        /// <summary>HDT の GameType 名（GT_BATTLEGROUNDS, GT_BATTLEGROUNDS_FRIENDLY, ...）</summary>
        public string? GameType { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? DurationSec { get; set; }
        public string? Region { get; set; }
        public CardRef Anomaly { get; set; } = new CardRef();
        public CardRef GlobalOldGod { get; set; } = new CardRef();
        public List<string> MinionTypes { get; set; } = new List<string>();
        public int? CombatDamageCap { get; set; }
        public int? LobbySize { get; set; }
    }

    /// <summary>カード参照。取れなければ全て null のまま（オブジェクト自体は null にしない）。</summary>
    public class CardRef
    {
        public string? CardId { get; set; }
        public string? Name { get; set; }
        public int? DbfId { get; set; }

        [JsonIgnore]
        public bool IsEmpty => CardId == null && DbfId == null;
    }

    public sealed class HeroChoice : CardRef
    {
        public int? StartingArmor { get; set; }
    }

    public sealed class TrinketRecord : CardRef
    {
        /// <summary>獲得ターン。不明なら null</summary>
        public int? Turn { get; set; }
    }

    public sealed class PlayerInfo
    {
        public CardRef Hero { get; set; } = new CardRef();
        public List<HeroChoice> HeroChoices { get; set; } = new List<HeroChoice>();
        /// <summary>ヒーローの無料リロールを使った場合のみ、リロール前の選択肢。使っていなければ null</summary>
        public List<HeroChoice>? HeroChoicesBeforeReroll { get; set; }
        public CardRef HeroPower { get; set; } = new CardRef();
        public int? StartingHealth { get; set; }
        public int? StartingArmor { get; set; }
        public List<TrinketRecord> Trinkets { get; set; } = new List<TrinketRecord>();
        public QuestInfo Quest { get; set; } = new QuestInfo();
    }

    public sealed class QuestInfo
    {
        public string? CardId { get; set; }
        public string? Name { get; set; }
        public string? RewardCardId { get; set; }
        public int? CompletedTurn { get; set; }
    }

    public sealed class ResultInfo
    {
        public int? Placement { get; set; }
        public int? MmrBefore { get; set; }
        public int? MmrAfter { get; set; }
        public int? MmrDelta { get; set; }
        public int? EliminatedTurn { get; set; }
        public int? FinalTavernTier { get; set; }
        public int? TotalTurns { get; set; }
    }

    public sealed class OpponentRecord
    {
        public int PlayerId { get; set; }
        public CardRef Hero { get; set; } = new CardRef();
        /// <summary>自分の敗退時点で確定している順位。未確定なら null</summary>
        public int? Placement { get; set; }
        /// <summary>自分の敗退時点のリーダーボード順位</summary>
        public int? EstimatedPlacement { get; set; }
        public int? EliminatedTurn { get; set; }
        public int? FinalHealth { get; set; }
        public int? FinalArmor { get; set; }
        public int? FinalTavernTier { get; set; }
        public List<CardRef> Trinkets { get; set; } = new List<CardRef>();
    }
}
