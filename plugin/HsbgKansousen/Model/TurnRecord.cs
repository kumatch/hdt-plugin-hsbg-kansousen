using System.Collections.Generic;

namespace HsbgKansousen.Model
{
    public sealed class TurnRecord
    {
        public int Turn { get; set; }
        /// <summary>ショップ終了時（戦闘開始時）のタヴァーンティア</summary>
        public int? TavernTier { get; set; }
        /// <summary>ショップ開始時の HP（前戦闘の結果反映後）</summary>
        public int? Health { get; set; }
        public int? Armor { get; set; }
        /// <summary>そのターンの所持ゴールド（戦闘開始時の RESOURCES）</summary>
        public int? GoldAvailable { get; set; }
        /// <summary>ショップ行動の集計。取れなければ null</summary>
        public TurnActions? Actions { get; set; }
        /// <summary>戦闘に出した盤面（左から順）</summary>
        public List<MinionRecord> BoardEndOfShop { get; set; } = new List<MinionRecord>();
        /// <summary>config.snapshot_hand=true のときのみ（既定 true。schema 1.5.0 でミニオンの攻撃力・体力・ティアを追加）</summary>
        public List<CardSnapshot>? HandEndOfShop { get; set; }
        /// <summary>このターンにショップへ並んだカード（ターン開始時・リロールごと）。観測できなければ空。schema 1.5.0 で追加</summary>
        public List<ShopOffer> ShopOffers { get; set; } = new List<ShopOffer>();
        /// <summary>ショップ開始時の他プレイヤーのティアと HP。取れなければ null。schema 1.5.0 で追加</summary>
        public List<LobbyEntry>? Lobby { get; set; }
        /// <summary>自分の神祇（Secret Deity の Sigil）。神祇の無いシーズンや取れなければ null。schema 1.2.0 で追加</summary>
        public DeityRecord? Deity { get; set; }
        public CombatRecord? Combat { get; set; }
    }

    /// <summary>
    /// 神祇（クトゥーン、ヤシャラージュ等）のステータス。Sigil の BACON_EVOLUTION_CARD_OVERWRITE_ATK/HEALTH は
    /// 印刷値への加算ではなく現在の合計値。schema 1.2.0 で追加
    /// </summary>
    public sealed class DeityRecord : CardRef
    {
        /// <summary>自分: ショップ終了時（戦闘に持ち込む値）。相手: 戦闘開始時</summary>
        public int? Attack { get; set; }
        public int? Health { get; set; }
        /// <summary>戦闘終了時（戦闘中の成長を含む）</summary>
        public int? AttackAfterCombat { get; set; }
        public int? HealthAfterCombat { get; set; }
        /// <summary>この戦闘で目覚めたか（味方の異形が規定数死んで召喚された）。不明なら null</summary>
        public bool? Awakened { get; set; }
        /// <summary>
        /// 神祇が金色か（古なる者の仮面。クトゥーンは 2 倍を分配、ヤシャラージュは 4 体を召喚）。Sigil の神祇カードが
        /// 金色版（card_id が "_G" で終わる）か、金色で目覚めたら true。schema 1.3.0 で追加
        /// </summary>
        public bool? Golden { get; set; }
    }

    public sealed class TurnActions
    {
        /// <summary>ショップ終了時の RESOURCES_USED。売却とゴールド獲得で戻った分は差し引かれる（正味。gold_available - gold_spent = 使い残し）</summary>
        public int? GoldSpent { get; set; }
        public int? Buys { get; set; }
        /// <summary>盤面のミニオンと対応が付いた売却。schema 1.5.x 以前はゴールド獲得も含んでいた</summary>
        public int? Sells { get; set; }
        /// <summary>売却以外で増えたゴールド（酒場のコイン、青甲羅トートラン等）。schema 1.6.0 で追加</summary>
        public int? GoldGained { get; set; }
        public int? Rerolls { get; set; }
        public int? TierUps { get; set; }
        public int? HeroPowerUses { get; set; }
        public int? SpellsPlayed { get; set; }
        /// <summary>「悪夢の発見」（Dark Discovery ボタン。ミニオンに付く悪夢の恵み = Dark Gift とは別）でミニオンを得た回数。schema 1.1.0 で追加</summary>
        public int? DarkGifts { get; set; }
    }

    public sealed class CombatRecord
    {
        public int? OpponentPlayerId { get; set; }
        public CardRef OpponentHero { get; set; } = new CardRef();
        public List<MinionRecord> OpponentBoard { get; set; } = new List<MinionRecord>();
        public int? OpponentHealth { get; set; }
        public int? OpponentArmor { get; set; }
        /// <summary>戦闘開始時の相手の PLAYER_TECH_LEVEL。取れなければ null。schema 1.5.0 で追加</summary>
        public int? OpponentTavernTier { get; set; }
        /// <summary>"win" | "loss" | "tie" | null</summary>
        public string? Result { get; set; }
        public int? DamageDealt { get; set; }
        public int? DamageTaken { get; set; }
        public BobsBuddyPrediction? BobsBuddy { get; set; }
        /// <summary>相手の神祇。相手の Sigil は戦闘中だけ読める。schema 1.2.0 で追加</summary>
        public DeityRecord? OpponentDeity { get; set; }
    }

    public sealed class BobsBuddyPrediction
    {
        public double Win { get; set; }
        public double Tie { get; set; }
        public double Loss { get; set; }
        /// <summary>相手を倒す確率（theirDeathRate）</summary>
        public double Lethal { get; set; }
        /// <summary>自分が倒される確率（myDeathRate）</summary>
        public double OpponentLethal { get; set; }
        /// <summary>正 = 与ダメ期待値、負 = 被ダメ期待値</summary>
        public double AvgDamage { get; set; }
        public int Simulations { get; set; }
    }
}
