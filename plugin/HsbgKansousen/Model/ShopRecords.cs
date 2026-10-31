using System.Collections.Generic;

namespace HsbgKansousen.Model
{
    /// <summary>
    /// 手札やショップに見えたカード 1 枚。ミニオンは攻撃力・体力・金色も持つ（手札のミニオンは強化されていることがある）。
    /// schema 1.5.0 で追加（1.4.x 以前の hand_end_of_shop は card_id / name / dbf_id だけ）
    /// </summary>
    public sealed class CardSnapshot : CardRef
    {
        /// <summary>"minion" | "spell" | "other"</summary>
        public string? CardType { get; set; }
        /// <summary>TECH_LEVEL（酒場の呪文にもある）。無ければ null</summary>
        public int? Tier { get; set; }
        /// <summary>ミニオンのみ。それ以外は null</summary>
        public int? Attack { get; set; }
        public int? Health { get; set; }
        public bool? Golden { get; set; }
    }

    /// <summary>
    /// ショップ（Bob の酒場）に並んだカード。ターン開始時・リロールごとに全体、それ以外で増えたカードは増えた分だけ。
    /// schema 1.5.0 で追加
    /// </summary>
    public sealed class ShopOffer
    {
        /// <summary>
        /// "turn_start"（ターンで最初に見えた酒場）| "reroll"（丸ごと入れ替わった）|
        /// "refill"（買い切って空になった後に並んだ）| "added"（効果などで途中から増えたカードだけ）
        /// </summary>
        public string Trigger { get; set; } = "";
        public List<CardSnapshot> Cards { get; set; } = new List<CardSnapshot>();
    }

    /// <summary>ショップ開始時のロビー（リーダーボードに出ている他プレイヤーのティアと HP）。schema 1.5.0 で追加</summary>
    public sealed class LobbyEntry
    {
        public int PlayerId { get; set; }
        public int? TavernTier { get; set; }
        public int? Health { get; set; }
        public int? Armor { get; set; }
        /// <summary>PLAYER_LEADERBOARD_PLACE</summary>
        public int? LeaderboardPlace { get; set; }
        public bool Eliminated { get; set; }
    }
}
