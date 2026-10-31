using System.Collections.Generic;

namespace HsbgKansousen.Model
{
    public sealed class MinionRecord
    {
        /// <summary>1 始まりの盤面位置（左から）</summary>
        public int Position { get; set; }
        public string? CardId { get; set; }
        public string? Name { get; set; }
        public int? Tier { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public bool Golden { get; set; }
        /// <summary>taunt, divine_shield, poisonous, venomous, reborn, windfury, stealth（schema 1.5.0 から deathrattle, rally も）</summary>
        public List<string> Keywords { get; set; } = new List<string>();
        /// <summary>
        /// 聖なる盾の枚数（DIVINE_SHIELD の値。無ければ 0）。闇のパラドックスは盾を重ねて 2 枚以上になる。
        /// schema 1.4.0 で追加（1.3.x 以前のログには無い）
        /// </summary>
        public int DivineShields { get; set; }
        /// <summary>beast, mech, ... （小文字）</summary>
        public List<string> MinionTypes { get; set; } = new List<string>();
    }
}
