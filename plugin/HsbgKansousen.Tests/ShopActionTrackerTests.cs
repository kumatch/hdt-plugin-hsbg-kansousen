using System.Collections.Generic;
using HsbgKansousen.Logic;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class ShopActionTrackerTests
    {
        private static ShopTick Tick(int[] shop, int[]? hand = null, int[]? handSpells = null, int? used = 0, int tech = 1, int triples = 0, bool hpExhausted = false, int[]? board = null)
            => new ShopTick
            {
                ShopIds = new HashSet<int>(shop),
                BoardIds = new HashSet<int>(board ?? new int[0]),
                HandIds = new HashSet<int>(hand ?? new int[0]),
                HandSpellIds = new HashSet<int>(handSpells ?? new int[0]),
                ResourcesUsed = used,
                TechLevel = tech,
                Triples = triples,
                // ヒーローパワー Entity は 1 つ（id 500）が PLAY 中、という通常のヒーローの形
                HeroPowerIds = new HashSet<int> { 500 },
                ExhaustedHeroPowerIds = hpExhausted ? new HashSet<int> { 500 } : new HashSet<int>(),
            };

        [Fact]
        public void FirstTick_IsBaseline_NoActions()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, hand: new[] { 9 }, used: 3, tech: 2));
            Assert.Equal(0, t.Buys + t.Sells + t.Rerolls + t.TierUps + t.HeroPowerUses + t.SpellsPlayed);
        }

        [Fact]
        public void Buy_ShopEntityMovesToHand()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 0));
            t.Observe(Tick(new[] { 1, 3 }, hand: new[] { 2 }, used: 3));
            Assert.Equal(1, t.Buys);
            Assert.Equal(0, t.Rerolls);
            Assert.Equal(0, t.Sells);
        }

        [Fact]
        public void CardCreatedInHand_IsNotABuy()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            t.Observe(Tick(new[] { 1, 2, 3 }, hand: new[] { 50 }));
            Assert.Equal(0, t.Buys);
        }

        [Fact]
        public void Reroll_ReplacesWholeShop()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 0));
            t.Observe(Tick(new[] { 4, 5, 6 }, used: 1));
            t.Observe(Tick(new[] { 7, 8, 9 }, used: 2));
            Assert.Equal(2, t.Rerolls);
            Assert.Equal(0, t.Buys);
        }

        [Fact]
        public void BuyingLastCardThenNewShop_NotCountedAsReroll()
        {
            // 空になったショップは比較対象が無いのでリロール扱いにしない（過小評価側に倒す）
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }));
            t.Observe(Tick(new int[0], hand: new[] { 1 }));
            t.Observe(Tick(new[] { 2, 3, 4 }, hand: new[] { 1 }));
            Assert.Equal(1, t.Buys);
            Assert.Equal(0, t.Rerolls);
        }

        [Fact]
        public void Sell_DecreasesResourcesUsed()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 5, board: new[] { 20, 21, 22 }));
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 4, board: new[] { 21, 22 }));
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 2));
            Assert.Equal(3, t.Sells);
            Assert.Equal(0, t.GoldGained);
        }

        [Fact]
        public void TierUp_AndTriples_AndHeroPower()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, tech: 2, triples: 0));
            t.Observe(Tick(new[] { 1 }, tech: 3, triples: 1, hpExhausted: true));
            t.Observe(Tick(new[] { 1 }, tech: 3, triples: 1, hpExhausted: true));
            Assert.Equal(1, t.TierUps);
            Assert.Equal(1, t.Triples);
            Assert.Equal(1, t.HeroPowerUses);
        }

        [Fact]
        public void HeroPower_ReplacedOnUse_IsCounted()
        {
            // ゲイルウィング: 使うと同じログバッチで SETASIDE/REMOVEDFROMGAME に移り EXHAUSTED=1 になる。次のターンに新しい Entity が作られる
            var t = new ShopActionTracker();
            var a = Tick(new[] { 1 }); a.HeroPowerIds = new HashSet<int> { 229 }; a.ExhaustedHeroPowerIds = new HashSet<int>();
            var b = Tick(new[] { 1 }); b.HeroPowerIds = new HashSet<int>(); b.ExhaustedHeroPowerIds = new HashSet<int> { 229 };
            var c = Tick(new[] { 1 }); c.HeroPowerIds = new HashSet<int>(); c.ExhaustedHeroPowerIds = new HashSet<int> { 229 };
            t.Observe(a);
            var actions = t.Observe(b);
            t.Observe(c);
            Assert.Equal(ShopAction.HeroPower, Assert.Single(actions).Type);
            Assert.Equal(1, t.HeroPowerUses);
        }

        [Fact]
        public void TwoHeroPowers_EachUseCounted()
        {
            var t = new ShopActionTracker();
            var a = Tick(new[] { 1 }); a.HeroPowerIds = new HashSet<int> { 10, 11 }; a.ExhaustedHeroPowerIds = new HashSet<int>();
            var b = Tick(new[] { 1 }); b.HeroPowerIds = new HashSet<int> { 10, 11 }; b.ExhaustedHeroPowerIds = new HashSet<int> { 10 };
            var c = Tick(new[] { 1 }); c.HeroPowerIds = new HashSet<int> { 10, 11 }; c.ExhaustedHeroPowerIds = new HashSet<int> { 10, 11 };
            t.Observe(a); t.Observe(b); t.Observe(c);
            Assert.Equal(2, t.HeroPowerUses);
        }

        [Fact]
        public void HeroPower_ExhaustedAtTurnStart_NotCounted()
        {
            // ターン開始時点で既に使用済み（前ターンの持ち越し等）はベースラインなので数えない
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, hpExhausted: true));
            t.Observe(Tick(new[] { 1 }, hpExhausted: true));
            Assert.Equal(0, t.HeroPowerUses);
        }

        [Fact]
        public void TierUp_FromZeroBaseline_Ignored()
        {
            // ターン 1 開始直後はヒーローの PLAYER_TECH_LEVEL が 0 のことがある
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, tech: 0));
            t.Observe(Tick(new[] { 1 }, tech: 1));
            Assert.Equal(0, t.TierUps);
        }

        [Fact]
        public void Spell_LeavingHand_IsPlayed()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, hand: new[] { 20, 21 }, handSpells: new[] { 21 }));
            t.Observe(Tick(new[] { 1 }, hand: new[] { 20 }, handSpells: new int[0]));
            t.Observe(Tick(new[] { 1 }, hand: new int[0], handSpells: new int[0]));
            Assert.Equal(1, t.SpellsPlayed);
        }

        private static ShopTick WithSources(ShopTick tick, Dictionary<int, string>? sources = null, int[]? offered = null)
        {
            if (sources != null) tick.HandSources = sources;
            if (offered != null) tick.OfferedIds = new HashSet<int>(offered);
            return tick;
        }

        [Fact]
        public void DarkGift_CardCreatedInHand_IsDarkGiftNotBuy()
        {
            // 悪夢の発見: 3 ゴールド払う → 3 択が提示される → 選んだカードがショップを経由せず手札に来る
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 0));
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 3));                                   // ゴールドだけ減る（ボタンの PLAY）
            t.Observe(WithSources(Tick(new[] { 1, 2, 3 }, used: 3), offered: new[] { 42, 43, 44 }));   // 3 択が開いている
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 43 }, used: 3),
                sources: new Dictionary<int, string> { [43] = CardSource.DarkGift }));      // 選択完了。HDT 側の 3 択は空に戻る
            var gift = Assert.Single(actions);
            Assert.Equal(ShopAction.DarkGift, gift.Type);
            Assert.Equal(43, gift.EntityId);
            Assert.Equal(CardSource.DarkGift, gift.Source);
            Assert.Equal(new List<int> { 42, 43, 44 }, gift.OfferedIds);
            Assert.Equal(1, t.DarkGifts);
            Assert.Equal(0, t.Buys);
            Assert.Equal(1, t.ToActions(3).DarkGifts);
        }

        [Fact]
        public void DarkGift_ThenPlayed_CountsAsPlay()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            t.Observe(WithSources(Tick(new[] { 1, 2, 3 }, hand: new[] { 43 }), sources: new Dictionary<int, string> { [43] = CardSource.DarkGift }));
            var tick = Tick(new[] { 1, 2, 3 });
            tick.BoardIds = new HashSet<int> { 43 };
            var actions = t.Observe(tick);
            Assert.Equal(ShopAction.Play, Assert.Single(actions).Type);
            Assert.Equal(1, t.Plays);
            Assert.Equal(1, t.DarkGifts);
        }

        [Fact]
        public void HeroPowerDiscover_SameTick_AttachesCardToHeroPower()
        {
            // ゴミを宝に: 選んだカードの HAND 移動とヒーローパワーの EXHAUSTED が同じログバッチ（= 同じ tick）で来る
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            t.Observe(WithSources(Tick(new[] { 1, 2, 3 }), offered: new[] { 50, 51, 52 }));
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 51 }, hpExhausted: true),
                sources: new Dictionary<int, string> { [51] = CardSource.HeroPower }));
            var hp = Assert.Single(actions);
            Assert.Equal(ShopAction.HeroPower, hp.Type);
            Assert.Equal(51, hp.EntityId);
            Assert.Equal(new List<int> { 50, 51, 52 }, hp.OfferedIds);
            Assert.Equal(1, t.HeroPowerUses);
            Assert.Equal(0, t.DarkGifts);
            Assert.Equal(0, t.Buys);
        }

        [Fact]
        public void TripleRewardDiscover_IsDiscoverEvent_NotBuy()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            t.Observe(WithSources(Tick(new[] { 1, 2, 3 }), offered: new[] { 60, 61, 62 }));
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 60 }),
                sources: new Dictionary<int, string> { [60] = CardSource.Triple }));
            var d = Assert.Single(actions);
            Assert.Equal(ShopAction.Discover, d.Type);
            Assert.Equal(CardSource.Triple, d.Source);
            Assert.Equal(new List<int> { 60, 61, 62 }, d.OfferedIds);
            Assert.Equal(0, t.Buys);
            Assert.Equal(0, t.DarkGifts);
        }

        [Fact]
        public void CreatedByHeroPowerOrTriple_NotOffered_IsIgnored()
        {
            // Barov の Tavern Coin（ヒーローパワーが毎ターン生成）、トリプルで出来たゴールデンなどは発見ではない
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 80, 81 }),
                sources: new Dictionary<int, string> { [80] = CardSource.HeroPower, [81] = CardSource.Triple }));
            Assert.Empty(actions);
            Assert.Equal(0, t.Buys);
            Assert.Equal(0, t.DarkGifts);
        }

        [Fact]
        public void DarkGift_WithoutObservedOffer_IsStillCounted()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 90 }),
                sources: new Dictionary<int, string> { [90] = CardSource.DarkGift }));
            var g = Assert.Single(actions);
            Assert.Equal(ShopAction.DarkGift, g.Type);
            Assert.Null(g.OfferedIds);
            Assert.Equal(1, t.DarkGifts);
        }

        [Fact]
        public void OfferedIds_NotMatchingChosenCard_AreNotAttached()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }));
            t.Observe(WithSources(Tick(new[] { 1, 2, 3 }), offered: new[] { 70, 71, 72 }));
            var actions = t.Observe(WithSources(
                Tick(new[] { 1, 2, 3 }, hand: new[] { 99 }),
                sources: new Dictionary<int, string> { [99] = CardSource.DarkGift }));
            Assert.Null(Assert.Single(actions).OfferedIds);
        }

        private static List<ShopAction> Shops(List<ShopAction> actions) => actions.FindAll(a => a.Type == ShopAction.Shop);
        private static List<ShopAction> Sells(List<ShopAction> actions) => actions.FindAll(a => a.Type == ShopAction.Sell);
        private static List<ShopAction> GoldGains(List<ShopAction> actions) => actions.FindAll(a => a.Type == ShopAction.GoldGain);

        [Fact]
        public void Shop_TurnStartAndReroll_RecordWholeShop()
        {
            var t = new ShopActionTracker();
            var first = Shops(t.Observe(Tick(new[] { 3, 1, 2 })));
            Assert.Equal(ShopTrigger.TurnStart, Assert.Single(first).Trigger);
            Assert.Equal(new List<int> { 1, 2, 3 }, first[0].ShopIds);
            // 購入で減っただけなら記録しない
            Assert.Empty(Shops(t.Observe(Tick(new[] { 1, 3 }, hand: new[] { 2 }, used: 3))));
            var reroll = Shops(t.Observe(Tick(new[] { 4, 5, 6 }, hand: new[] { 2 }, used: 4)));
            Assert.Equal(ShopTrigger.Reroll, Assert.Single(reroll).Trigger);
            Assert.Equal(new List<int> { 4, 5, 6 }, reroll[0].ShopIds);
        }

        [Fact]
        public void Shop_EmptyAtFirstTick_FirstFillIsTurnStart_ThenAddedAndRefill()
        {
            var t = new ShopActionTracker();
            Assert.Empty(Shops(t.Observe(Tick(new int[0]))));
            var start = Shops(t.Observe(Tick(new[] { 1, 2 })));
            Assert.Equal(ShopTrigger.TurnStart, Assert.Single(start).Trigger);
            // 効果で 1 枚増えた: 増えた分だけ
            var added = Shops(t.Observe(Tick(new[] { 1, 2, 7 })));
            Assert.Equal(ShopTrigger.Added, Assert.Single(added).Trigger);
            Assert.Equal(new List<int> { 7 }, added[0].ShopIds);
            // 買い切って空 → 新しい酒場
            t.Observe(Tick(new int[0], hand: new[] { 1, 2, 7 }));
            var refill = Shops(t.Observe(Tick(new[] { 8, 9 }, hand: new[] { 1, 2, 7 })));
            Assert.Equal(ShopTrigger.Refill, Assert.Single(refill).Trigger);
            Assert.Equal(0, t.Rerolls);
        }

        [Fact]
        public void Sell_IsMatchedToMinionThatLeftTheBoard()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2, 3 }, used: 5, board: new[] { 20, 21 }));
            var sell = Assert.Single(Sells(t.Observe(Tick(new[] { 1, 2, 3 }, used: 4, board: new[] { 21 }))));
            Assert.Equal(20, sell.EntityId);
            Assert.Equal(1, t.Sells);
        }

        [Fact]
        public void Sell_GoldChangeAndBoardChangeInDifferentTicks_AreMatched()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, used: 5, board: new[] { 20, 21 }));
            // 先にゴールドだけ戻る
            Assert.Empty(Sells(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20, 21 }))));
            var sell = Assert.Single(Sells(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20 }))));
            Assert.Equal(21, sell.EntityId);
        }

        [Fact]
        public void GoldUpWithoutMinionLeaving_IsGoldGain_AfterWindow_OrAtFlush()
        {
            // 酒場のコイン等: RESOURCES_USED は 1 減るが盤面からは何も消えない（2026-10-12 の power.log で確認）
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, used: 5, board: new[] { 20 }));
            var emitted = new List<ShopAction>();
            emitted.AddRange(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20 })));
            for (var i = 0; i < ShopActionTracker.SellMatchWindow; i++)
                emitted.AddRange(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20 })));
            Assert.Empty(Sells(emitted));
            Assert.Null(Assert.Single(GoldGains(emitted)).EntityId);
            // ショップ終了時に待っている減少もゴールド獲得
            t.Observe(Tick(new[] { 1 }, used: 3, board: new[] { 20 }));
            Assert.Equal(ShopAction.GoldGain, Assert.Single(t.FlushPending()).Type);
            Assert.Equal(0, t.Sells);
            Assert.Equal(2, t.GoldGained);
            Assert.Equal(2, t.ToActions(3).GoldGained);
        }

        [Fact]
        public void CoinThenSell_CountsOneOfEach()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, used: 6, board: new[] { 20, 21 }));
            var emitted = new List<ShopAction>();
            emitted.AddRange(t.Observe(Tick(new[] { 1 }, used: 5, board: new[] { 20, 21 })));   // コイン
            emitted.AddRange(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20 })));       // 21 を売却
            for (var i = 0; i < ShopActionTracker.SellMatchWindow; i++)
                emitted.AddRange(t.Observe(Tick(new[] { 1 }, used: 4, board: new[] { 20 })));
            emitted.AddRange(t.FlushPending());
            Assert.Equal(21, Assert.Single(Sells(emitted)).EntityId);
            Assert.Single(GoldGains(emitted));
            Assert.Equal(1, t.Sells);
            Assert.Equal(1, t.GoldGained);
        }

        private static ShopTick Gold(ShopTick tick, int temp, int spent)
        {
            tick.TempResources = temp;
            tick.ResourcesSpent = spent;
            return tick;
        }

        [Fact]
        public void SellAndBuyInSameTick_SellIsCountedFromSpentTotal()
        {
            // 2026-10-12 実機: 売却（RESOURCES_USED 1 → 0）と購入（0 → 3）が同じ tick に入り、差分は +2 だけ。払った累計 +3 で売却の 1 を戻す
            var t = new ShopActionTracker();
            t.Observe(Gold(Tick(new[] { 1, 2 }, used: 1, board: new[] { 20 }), temp: 0, spent: 87));
            var emitted = t.Observe(Gold(Tick(new[] { 2 }, hand: new[] { 1 }, used: 3), temp: 0, spent: 90));
            Assert.Equal(20, Assert.Single(Sells(emitted)).EntityId);
            Assert.Equal(1, t.Buys);
            Assert.Equal(0, t.GoldGained);
        }

        [Fact]
        public void SellAtFullGold_RaisesTempResources()
        {
            // 2026-10-12 実機: RESOURCES_USED が 0（満タン）のときの売却は TEMP_RESOURCES が 1 増える。それを使ったリロールは戻りではない
            var t = new ShopActionTracker();
            t.Observe(Gold(Tick(new[] { 1 }, used: 0, board: new[] { 20, 21 }), temp: 0, spent: 121));
            var emitted = new List<ShopAction>();
            emitted.AddRange(t.Observe(Gold(Tick(new[] { 1 }, used: 0, board: new[] { 20 }), temp: 1, spent: 121)));
            emitted.AddRange(t.Observe(Gold(Tick(new[] { 2 }, used: 0, board: new[] { 20 }), temp: 0, spent: 122)));
            emitted.AddRange(t.FlushPending());
            Assert.Equal(21, Assert.Single(Sells(emitted)).EntityId);
            Assert.Empty(GoldGains(emitted));
            Assert.Equal(1, t.Rerolls);
        }

        [Fact]
        public void MaxGoldChange_IsNotAGoldGain()
        {
            var t = new ShopActionTracker();
            var a = Tick(new[] { 1 }, used: 5);
            a.Resources = 10;
            t.Observe(a);
            var b = Tick(new[] { 1 }, used: 0);
            b.Resources = 3;
            var emitted = t.Observe(b);
            emitted.AddRange(t.FlushPending());
            Assert.Empty(GoldGains(emitted));
        }

        [Fact]
        public void MinionReturnedToHand_IsNotMatchedToASell()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1 }, used: 5, board: new[] { 20 }));
            t.Observe(Tick(new[] { 1 }, hand: new[] { 20 }, used: 5));
            var emitted = new List<ShopAction>();
            emitted.AddRange(t.Observe(Tick(new[] { 1 }, hand: new[] { 20 }, used: 4)));
            emitted.AddRange(t.FlushPending());
            Assert.Empty(Sells(emitted));
            Assert.Single(GoldGains(emitted));
        }

        [Fact]
        public void Reset_ClearsCounts()
        {
            var t = new ShopActionTracker();
            t.Observe(Tick(new[] { 1, 2 }));
            t.Observe(Tick(new[] { 1 }, hand: new[] { 2 }));
            Assert.Equal(1, t.Buys);
            t.Reset();
            Assert.Equal(0, t.Buys);
            Assert.Equal(0, t.Ticks);
            var a = t.ToActions(7);
            Assert.Equal(7, a.GoldSpent);
            Assert.Equal(0, a.Buys);
            Assert.Equal(0, a.DarkGifts);
        }
    }
}
