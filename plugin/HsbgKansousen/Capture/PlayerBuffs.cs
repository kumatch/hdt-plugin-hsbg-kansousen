using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HsbgKansousen.Logic;
using HsbgKansousen.Model;

namespace HsbgKansousen.Capture
{
    /// <summary>
    /// 盤面に乗らない「この対戦中」の強化 1 種類の読み方（docs/log-schema.md「この対戦中の強化」、docs/hdt-research.md §3.20）。
    /// 自分の値はプレイヤー Entity に付くエンチャントの TAG_SCRIPT_DATA_NUM_1/2 か、プレイヤー Entity のタグ（両方あれば大きいほう）。
    /// 相手の値は戦闘ごとに作られるコピー（相手のエンチャントの複製か、TagTransferPlayerEnchant のタグ）から読む。
    /// </summary>
    internal sealed class PlayerBuffKind
    {
        /// <summary>HearthDb: CardIds.NonCollectible.Neutral.TagtransferplayerenchantDnt</summary>
        public const string TagTransferCardId = "Bacon_TagTransferPlayerE";

        /// <summary>events.jsonl のフィールド名の頭（beetle_attack 等）</summary>
        public string Key { get; set; } = "";
        public string? EnchantCardId { get; set; }
        public GameTag? AttackTag { get; set; }
        public GameTag? HealthTag { get; set; }
        /// <summary>相手の値を TagTransferPlayerEnchant の AttackTag / HealthTag から読む（false ならエンチャントのコピーから）</summary>
        public bool OpponentFromTagTransfer { get; set; }
        public int BaseAttack { get; set; }
        public int BaseHealth { get; set; }
        public Func<TurnRecord, BuffStatsRecord?> GetPlayer { get; set; } = _ => null;
        public Action<TurnRecord, BuffStatsRecord?> SetPlayer { get; set; } = (_, __) => { };
        public Action<CombatRecord, BuffStatsRecord?> SetOpponent { get; set; } = (_, __) => { };

        /// <summary>甲虫: HearthDb RunedProgenitor_BeetleArmyPlayerEnchantDnt。甲虫のステータス = 甲虫 BG28_603t（2/2）+ 強化</summary>
        public static readonly PlayerBuffKind Beetle = new PlayerBuffKind
        {
            Key = "beetle",
            EnchantCardId = "BG31_808pe",
            BaseAttack = BeetleBase.Attack,
            BaseHealth = BeetleBase.Health,
            GetPlayer = t => t.Beetle,
            SetPlayer = (t, r) => t.Beetle = r,
            SetOpponent = (c, r) => c.OpponentBeetle = r,
        };

        /// <summary>
        /// 血の宝石: HearthDb MoonBaconJazzer_BloodGemPlayerEnchantDnt とプレイヤーの BACON_BLOODGEMBUFF*VALUE。
        /// 宝石 1 個が与える量 = 基本の +1/+1 + 強化（HDT 1.59.7 の BloodGemCounter / Bob's Buddy と同じく 2 つの大きいほう）
        /// </summary>
        public static readonly PlayerBuffKind BloodGem = new PlayerBuffKind
        {
            Key = "blood_gem",
            EnchantCardId = "BG26_159pe",
            AttackTag = GameTag.BACON_BLOODGEMBUFFATKVALUE,
            HealthTag = GameTag.BACON_BLOODGEMBUFFHEALTHVALUE,
            BaseAttack = 1,
            BaseHealth = 1,
            GetPlayer = t => t.BloodGem,
            SetPlayer = (t, r) => t.BloodGem = r,
            SetOpponent = (c, r) => c.OpponentBloodGem = r,
        };

        /// <summary>酒場の呪文: プレイヤーの TAVERN_SPELL_ATTACK/HEALTH_INCREASE（呪文が与える攻撃力・体力に足される量）。エンチャントは無い</summary>
        public static readonly PlayerBuffKind TavernSpell = new PlayerBuffKind
        {
            Key = "tavern_spell",
            AttackTag = GameTag.TAVERN_SPELL_ATTACK_INCREASE,
            HealthTag = GameTag.TAVERN_SPELL_HEALTH_INCREASE,
            OpponentFromTagTransfer = true,
            GetPlayer = t => t.TavernSpell,
            SetPlayer = (t, r) => t.TavernSpell = r,
            SetOpponent = (c, r) => c.OpponentTavernSpell = r,
        };

        /// <summary>酒場のミニオン: "Shop Buff Player Enchant (DNT)"（酒場のミニオンに足される量）</summary>
        public static readonly PlayerBuffKind TavernMinion = new PlayerBuffKind
        {
            Key = "tavern_minion",
            EnchantCardId = "BG_ShopBuff",
            GetPlayer = t => t.TavernMinion,
            SetPlayer = (t, r) => t.TavernMinion = r,
            SetOpponent = (c, r) => c.OpponentTavernMinion = r,
        };

        /// <summary>酒場のエレメンタル: HearthDb ElementalShopBuffPlayerEnchantmentDnt（酒場のエレメンタルに足される量。全ミニオンの分とは別）</summary>
        public static readonly PlayerBuffKind TavernElemental = new PlayerBuffKind
        {
            Key = "tavern_elemental",
            EnchantCardId = "BG_ShopBuff_Elemental",
            GetPlayer = t => t.TavernElemental,
            SetPlayer = (t, r) => t.TavernElemental = r,
            SetOpponent = (c, r) => c.OpponentTavernElemental = r,
        };

        public static readonly PlayerBuffKind[] All = { Beetle, BloodGem, TavernSpell, TavernMinion, TavernElemental };

        /// <summary>自分のエンチャント。強化が最初に起きたときに作られ、以後ゲーム中ずっとプレイヤー Entity に付いている。</summary>
        public Entity? PlayerEnchant(IEnumerable<Entity> entities, int playerId, int? playerEntityId)
        {
            if (EnchantCardId == null) return null;
            return entities
                .Where(x => x.CardId == EnchantCardId && x.IsInPlay && x.IsControlledBy(playerId)
                            && (!playerEntityId.HasValue || x.GetTag(GameTag.ATTACHED) == playerEntityId.Value))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        /// <summary>
        /// 戦闘相手の値の入れ物（戦闘ごとに相手のプレイヤー Entity に作られる）。<paramref name="minId"/> 以下の ID は前の戦闘の残骸。
        /// 前の戦闘のコピーは後片付けで REMOVEDFROMGAME に移る（値は残る）ので除外する。
        /// </summary>
        public Entity? OpponentSource(IEnumerable<Entity> entities, int opponentId, int minId)
        {
            var cardId = OpponentFromTagTransfer ? TagTransferCardId : EnchantCardId;
            if (cardId == null) return null;
            return entities
                .Where(x => x.Id > minId && x.CardId == cardId && x.IsControlledBy(opponentId)
                            && !x.IsInZone(Zone.REMOVEDFROMGAME))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();
        }

        /// <summary>自分の値（エンチャントとプレイヤー Entity のタグの大きいほう）。どちらも無ければ null。</summary>
        public BuffStatsReading? ReadPlayer(Entity? enchant, Entity? playerEntity)
        {
            var tags = playerEntity != null && AttackTag.HasValue && HealthTag.HasValue;
            return BuffStatsReading.FromSources(
                enchant?.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_1), enchant?.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_2),
                tags ? playerEntity!.GetTag(AttackTag!.Value) : (int?)null, tags ? playerEntity!.GetTag(HealthTag!.Value) : (int?)null,
                BaseAttack, BaseHealth);
        }

        /// <summary>相手の値（エンチャントのコピーなら TAG_SCRIPT_DATA_NUM_1/2、TagTransfer なら AttackTag / HealthTag）。</summary>
        public BuffStatsReading? ReadOpponent(Entity? source)
        {
            if (source == null) return null;
            if (OpponentFromTagTransfer)
            {
                if (!AttackTag.HasValue || !HealthTag.HasValue) return null;
                return new BuffStatsReading(source.GetTag(AttackTag.Value), source.GetTag(HealthTag.Value), BaseAttack, BaseHealth);
            }
            return new BuffStatsReading(source.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_1), source.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_2),
                BaseAttack, BaseHealth);
        }

        /// <summary>甲虫 BG28_603t の基本ステータス（HDT の BeetlesSizeCounter と同じくカードから。取れなければ 2/2）。</summary>
        private static class BeetleBase
        {
            public static readonly int Attack;
            public static readonly int Health;

            static BeetleBase()
            {
                Attack = Health = 2;
                try
                {
                    if (HearthDb.Cards.All.TryGetValue("BG28_603t", out var card) && card.Attack > 0 && card.Health > 0)
                    {
                        Attack = card.Attack;
                        Health = card.Health;
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.PluginLogger.Warn("PlayerBuffKind: " + ex.Message);
                }
            }
        }
    }

    /// <summary>1 種類の強化の、1 戦闘ぶんの観測（GameSession が種類ごとに 1 つ持つ）。</summary>
    internal sealed class PlayerBuffChannel
    {
        public PlayerBuffChannel(PlayerBuffKind kind) => Kind = kind;

        public PlayerBuffKind Kind { get; }
        public BuffStatsTracker Tracker { get; } = new BuffStatsTracker();
        /// <summary>自分のエンチャント（ゲーム中ずっとある。エンチャントの無い種類は null のまま）</summary>
        public Entity? PlayerEnchant { get; set; }
        /// <summary>この戦闘の相手の値の入れ物</summary>
        public Entity? OpponentSource { get; set; }

        /// <summary>まだ探す必要があるか（エンチャントのある種類で自分のエンチャントが無い、または相手の入れ物が無い）</summary>
        public bool NeedsScan => (Kind.EnchantCardId != null && PlayerEnchant == null) || OpponentSource == null;

        public void Reset(bool keepPlayer)
        {
            Tracker.Reset();
            OpponentSource = null;
            if (!keepPlayer) PlayerEnchant = null;
        }
    }
}
