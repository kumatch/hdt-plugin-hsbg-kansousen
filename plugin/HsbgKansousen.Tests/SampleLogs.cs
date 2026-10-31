using System;
using System.Collections.Generic;
using HsbgKansousen.Model;

namespace HsbgKansousen.Tests
{
    /// <summary>テスト用のログ生成。実ゲーム（6 位）の形を模している（日時・ID・MMR は架空の値）。</summary>
    internal static class SampleLogs
    {
        public static readonly DateTimeOffset StartedAt = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.FromHours(9));

        public static GameLog Complete()
        {
            var log = new GameLog
            {
                Status = LogStatus.Complete,
                Generator = { PluginVersion = "0.2.0", HdtVersion = "1.58.3", HearthstoneBuild = 56608 },
                Game =
                {
                    GameId = "3b0f9a4e-2c7d-4e1a-9f6b-8d2e5c1a7b30",
                    GameType = "GT_BATTLEGROUNDS",
                    StartedAt = StartedAt,
                    EndedAt = StartedAt.AddMinutes(23),
                    DurationSec = 1380,
                    Region = "ASIA",
                    GlobalOldGod = { CardId = "BG36_Deity_Yogg", Name = "Yogg-Saron", DbfId = 130610 },
                    MinionTypes = new List<string> { "aberration", "beast", "dragon", "mechanical", "undead" },
                    CombatDamageCap = 15,
                    LobbySize = 8,
                },
                Player =
                {
                    Hero = { CardId = "TB_BaconShop_HERO_34_SKIN_C", Name = "Sir Finley Mrrgglton", DbfId = 83651 },
                    HeroChoices = new List<HeroChoice>
                    {
                        new HeroChoice { CardId = "TB_BaconShop_HERO_34_SKIN_C", Name = "Sir Finley Mrrgglton", DbfId = 83651, StartingArmor = 0 },
                        new HeroChoice { CardId = "BG21_HERO_010_SKIN_C", Name = "X", DbfId = 102864, StartingArmor = 18 },
                    },
                    HeroChoicesBeforeReroll = null,
                    HeroPower = { CardId = "TB_BaconShop_HP_035", Name = "Adventure!", DbfId = 1 },
                    StartingHealth = 60,
                    StartingArmor = 0,
                    Trinkets = new List<TrinketRecord> { new TrinketRecord { CardId = "BG36_MagicItem_400", Name = "T", DbfId = 134315, Turn = 7 } },
                },
                Result = { Placement = 6, MmrBefore = 5000, MmrAfter = 4957, MmrDelta = -43, EliminatedTurn = 12, FinalTavernTier = 5, TotalTurns = 12 },
                Opponents = new List<OpponentRecord>
                {
                    new OpponentRecord
                    {
                        PlayerId = 2, Hero = { CardId = "TB_BaconShop_HERO_41_SKIN_E", Name = "Y", DbfId = 2 },
                        Placement = 8, EstimatedPlacement = 8, EliminatedTurn = 8, FinalHealth = -2, FinalArmor = 0, FinalTavernTier = 4,
                    },
                    new OpponentRecord
                    {
                        PlayerId = 3, Hero = { CardId = "BG31_HERO_005", Name = "Z", DbfId = 3 },
                        Placement = null, EstimatedPlacement = 1, EliminatedTurn = null, FinalHealth = 15, FinalArmor = 5, FinalTavernTier = 5,
                        Trinkets = new List<CardRef> { new CardRef { CardId = "X", Name = "X", DbfId = 134639 } },
                    },
                },
                Turns = new List<TurnRecord>
                {
                    new TurnRecord
                    {
                        Turn = 1, TavernTier = 1, Health = 60, Armor = 0, GoldAvailable = 3,
                        Actions = new TurnActions { GoldSpent = 3, Buys = 0, Sells = 0, GoldGained = 0, Rerolls = 0, TierUps = 0, HeroPowerUses = 0, SpellsPlayed = 0, DarkGifts = 1 },
                        BoardEndOfShop = new List<MinionRecord>
                        {
                            new MinionRecord { Position = 1, CardId = "BG35_814", Name = "M", Tier = 1, Attack = 3, Health = 3, Golden = false, Keywords = new List<string> { "taunt" }, MinionTypes = new List<string> { "beast" } },
                            new MinionRecord { Position = 2, CardId = "BG36_360t4", Name = "Dark Paradox", Tier = 6, Attack = 10, Health = 10, Golden = false, Keywords = new List<string> { "divine_shield" }, DivineShields = 3, MinionTypes = new List<string>() },
                        },
                        HandEndOfShop = new List<CardSnapshot>
                        {
                            new CardSnapshot { CardId = "BG20_GEM", Name = "Blood Gem", DbfId = 70136, CardType = "spell", Tier = 1 },
                            new CardSnapshot { CardId = "BG28_633", Name = "Felboar", DbfId = 1, CardType = "minion", Tier = 5, Attack = 6, Health = 6, Golden = false },
                        },
                        ShopOffers = new List<ShopOffer>
                        {
                            new ShopOffer { Trigger = "turn_start", Cards = new List<CardSnapshot> { new CardSnapshot { CardId = "BG35_814", Name = "M", DbfId = 2, CardType = "minion", Tier = 1, Attack = 3, Health = 3, Golden = false } } },
                            new ShopOffer { Trigger = "reroll", Cards = new List<CardSnapshot> { new CardSnapshot { CardId = "BG28_518", Name = "Chef's Choice", DbfId = 3, CardType = "spell", Tier = 1 } } },
                        },
                        Lobby = new List<LobbyEntry>
                        {
                            new LobbyEntry { PlayerId = 3, TavernTier = 2, Health = 30, Armor = 5, LeaderboardPlace = 1, Eliminated = false },
                        },
                        Deity = new DeityRecord { CardId = "BGFYM_000", Name = "C'Thun", DbfId = 130610, Attack = 3703, Health = 3983, AttackAfterCombat = 6187, HealthAfterCombat = 6467, Awakened = true, Golden = false },
                        Combat = new CombatRecord
                        {
                            OpponentPlayerId = 7, OpponentHero = { CardId = "BG20_HERO_283_SKIN_G", Name = "O", DbfId = 7 },
                            OpponentHealth = 30, OpponentArmor = 10, OpponentTavernTier = 2, Result = "win", DamageDealt = 2, DamageTaken = 0,
                            BobsBuddy = new BobsBuddyPrediction { Win = 1, Tie = 0, Loss = 0, Lethal = 0, OpponentLethal = 0, AvgDamage = 2, Simulations = 10000 },
                            OpponentDeity = new DeityRecord { CardId = "BGFYM_000", Name = "C'Thun", DbfId = 130610, Attack = 3578, Health = 3496, AttackAfterCombat = 4626, HealthAfterCombat = 4544, Awakened = false, Golden = null },
                        },
                    },
                    new TurnRecord { Turn = 2, TavernTier = 1, Health = 60, Armor = 0 },
                },
            };
            return log;
        }

        /// <summary>Phase 2 の GameSession が実際に出す形（盤面・戦闘なし、MMR 後値未取得）</summary>
        public static GameLog Phase2Minimal()
        {
            var log = new GameLog
            {
                Status = LogStatus.Complete,
                Generator = { PluginVersion = "0.2.0" },
                Game = { GameId = "11111111-2222-3333-4444-555555555555", StartedAt = StartedAt },
                Player = { Hero = { CardId = "TB_BaconShop_HERO_34_SKIN_C" } },
                Result = { Placement = 6, MmrBefore = 5000, TotalTurns = 12, EliminatedTurn = 12, FinalTavernTier = 5 },
            };
            for (var t = 1; t <= 12; t++)
                log.Turns.Add(new TurnRecord { Turn = t, TavernTier = Math.Min(6, 1 + t / 2), Health = 60 - t * 3, Armor = 0, GoldAvailable = Math.Min(10, 2 + t), Actions = new TurnActions { GoldSpent = Math.Min(10, 2 + t) } });
            log.Notes.Add("board/combat/actions not captured (phase 2)");
            return log;
        }
    }
}
