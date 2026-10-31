using System.Collections.Generic;
using System.Linq;
using HsbgKansousen.Model;

namespace HsbgKansousen.Logic
{
    /// <summary>
    /// 100ms ごとの観測値（1 tick）。GameSession が HDT の Entities から作る。HDT には依存しない。
    /// </summary>
    public sealed class ShopTick
    {
        /// <summary>ショップ（Bob の盤面）に並んでいるミニオン/スペルの Entity ID</summary>
        public HashSet<int> ShopIds { get; set; } = new HashSet<int>();
        /// <summary>自分の手札の Entity ID</summary>
        public HashSet<int> HandIds { get; set; } = new HashSet<int>();
        /// <summary>手札のうちスペル（BATTLEGROUND_SPELL / SPELL）の Entity ID</summary>
        public HashSet<int> HandSpellIds { get; set; } = new HashSet<int>();
        /// <summary>
        /// 手札の Entity のうち、ショップ以外の「発見」で作られたものの作成元。key = 手札の Entity ID、
        /// value = <see cref="CardSource"/> の値。作成元は CREATOR / DISPLAYED_CREATOR を辿って EntitySnapshotter が分類する
        /// </summary>
        public Dictionary<int, string> HandSources { get; set; } = new Dictionary<int, string>();
        /// <summary>いま発見（3 択）で提示されている Entity ID（HDT の Player.OfferedEntityIds）。選択が終わると空になる</summary>
        public HashSet<int> OfferedIds { get; set; } = new HashSet<int>();
        /// <summary>自分の盤面のミニオンの Entity ID</summary>
        public HashSet<int> BoardIds { get; set; } = new HashSet<int>();
        /// <summary>プレイヤー Entity の RESOURCES（所持ゴールド）。タグが無ければ null</summary>
        public int? Resources { get; set; }
        /// <summary>プレイヤー Entity の RESOURCES_USED（0 もあり得る）。プレイヤー Entity が無ければ null</summary>
        public int? ResourcesUsed { get; set; }
        /// <summary>
        /// プレイヤー Entity の TEMP_RESOURCES（上限を超えた一時ゴールド）。所持ゴールドが満タンのときの売却やコインは
        /// RESOURCES_USED を減らせないのでこちらが増える（2026-10-12 実機）。不明なら null
        /// </summary>
        public int? TempResources { get; set; }
        /// <summary>
        /// プレイヤー Entity の NUM_RESOURCES_SPENT_THIS_GAME（払ったゴールドの累計。戻ったゴールドでは減らない）。不明なら null。
        /// 売却と購入が同じ tick に入ると RESOURCES_USED の差分だけでは売却が相殺されるので、払った分をこれで足し戻す
        /// </summary>
        public int? ResourcesSpent { get; set; }
        public int TechLevel { get; set; }
        public int Triples { get; set; }
        /// <summary>自分の PLAY 中のヒーローパワー Entity ID（通常 1 つ。2 つ持つヒーローもいる）</summary>
        public HashSet<int> HeroPowerIds { get; set; } = new HashSet<int>();
        /// <summary>
        /// 自分のヒーローパワー Entity のうち EXHAUSTED（使用済み）が立っているもの。ゾーンは問わない:
        /// ゲイルウィング（ダンガーのグリフォン）のように使った瞬間に SETASIDE / REMOVEDFROMGAME へ移るヒーローパワーがあり、
        /// PLAY 中の Entity だけ見ていると使用を取り逃す（2026-09-26 実機: 8 回使って 0 回記録）
        /// </summary>
        public HashSet<int> ExhaustedHeroPowerIds { get; set; } = new HashSet<int>();

        public int? GoldRemaining => Resources.HasValue && ResourcesUsed.HasValue ? Resources.Value - ResourcesUsed.Value + (TempResources ?? 0) : (int?)null;
    }

    /// <summary>手札に現れたカードの作成元の種類（<see cref="ShopTick.HandSources"/> の値）。</summary>
    public static class CardSource
    {
        /// <summary>「悪夢の発見」ボタン（BG36_Button_DarkGift。ゴールドを払ってミニオンを 1 体発見）</summary>
        public const string DarkGift = "dark_gift";
        /// <summary>自分のヒーローパワーによる発見（ゴミを宝に 等）</summary>
        public const string HeroPower = "hero_power";
        /// <summary>トリプル報酬（TB_BaconShop_Triples_01）の発見</summary>
        public const string Triple = "triple";
    }

    /// <summary>tick 間の差分から検出した 1 つの行動。events.jsonl に書くために GameSession へ返す。</summary>
    public sealed class ShopAction
    {
        public const string Buy = "buy";
        public const string Sell = "sell";
        public const string Reroll = "reroll";
        public const string TierUp = "tier_up";
        public const string HeroPower = "hero_power";
        public const string Spell = "spell";
        public const string Play = "play";
        public const string Triple = "triple";
        /// <summary>「悪夢の発見」でミニオンを得た（EntityId = 選んだカード、OfferedIds = 3 択）</summary>
        public const string DarkGift = "dark_gift";
        /// <summary>ヒーローパワー / トリプル報酬などの発見で手札にカードを得た（Source に作成元）</summary>
        public const string Discover = "discover";
        /// <summary>ショップに並んだカード（ShopIds、Trigger に <see cref="ShopTrigger"/>）。schema 1.5.0</summary>
        public const string Shop = "shop";
        /// <summary>
        /// 売却以外でゴールドが増えた（酒場のコイン、青甲羅トートラン等。RESOURCES_USED が減ったのに盤面から消えたミニオンが無い）。
        /// Value = 1。schema 1.6.0（1.5.x 以前はこれも card_id = null の sell だった）
        /// </summary>
        public const string GoldGain = "gold_gain";

        public string Type { get; set; } = "";
        /// <summary>関係する Entity（購入/プレイ/スペル/発見で選んだカード）。無ければ null</summary>
        public int? EntityId { get; set; }
        /// <summary>tier_up なら新しいティア、sell/triple なら個数</summary>
        public int? Value { get; set; }
        /// <summary>discover の作成元（<see cref="CardSource"/>）。それ以外は null</summary>
        public string? Source { get; set; }
        /// <summary>発見の 3 択（提示された Entity ID）。分からなければ null</summary>
        public List<int>? OfferedIds { get; set; }
        /// <summary>shop: 並んだカードの Entity ID（並び順は GameSession が ZonePosition で付ける）</summary>
        public List<int>? ShopIds { get; set; }
        /// <summary>shop: <see cref="ShopTrigger"/> の値</summary>
        public string? Trigger { get; set; }
    }

    /// <summary>ショップのカードが見えたきっかけ（<see cref="ShopAction.Trigger"/>）。</summary>
    public static class ShopTrigger
    {
        /// <summary>ターンで最初に見えた酒場（凍結したカードを含む）</summary>
        public const string TurnStart = "turn_start";
        /// <summary>丸ごと入れ替わった（リロール）</summary>
        public const string Reroll = "reroll";
        /// <summary>買い切って空になった後に並んだ</summary>
        public const string Refill = "refill";
        /// <summary>途中から増えたカード（効果で追加など）。増えた分だけ</summary>
        public const string Added = "added";
    }

    /// <summary>
    /// ショップ中の行動（購入 / 売却 / リロール / ティアアップ / ヒーローパワー / スペル / プレイ / トリプル / 悪夢の発見）を
    /// tick 間の差分から数える。専用の GameTag が無いため推定であり、許容する誤差は ±1。
    /// </summary>
    public sealed class ShopActionTracker
    {
        private ShopTick? _prev;
        private readonly HashSet<int> _everInShop = new HashSet<int>();
        /// <summary>直近に提示された 3 択。選択が終わると HDT 側は空にするので、選んだカードが手札に来た tick のために覚えておく</summary>
        private List<int>? _lastOffered;
        /// <summary>このターンに PLAY 中だったヒーローパワー Entity ID。使用後にゾーンが変わっても EXHAUSTED の遷移を追う</summary>
        private readonly HashSet<int> _heroPowersSeen = new HashSet<int>();
        /// <summary>このターンにショップのカードを 1 度でも記録したか（最初の記録が turn_start）</summary>
        private bool _shopSeen;
        /// <summary>
        /// 売却の Entity 対応付け。RESOURCES_USED の減少と盤面からの消滅は同じ tick とは限らないので、
        /// どちらも <see cref="SellMatchWindow"/> tick だけ待って突き合わせる（盤面から消えて手札に行っていないミニオン = 売ったもの）。
        /// 対応が付かなかった減少は売却ではなくゴールド獲得（酒場のコイン等も RESOURCES_USED を 1 減らす。2026-10-12 に power.log で確認）
        /// </summary>
        private readonly List<(int Id, int Age)> _vanished = new List<(int Id, int Age)>();
        private readonly List<int> _pendingSells = new List<int>();
        public const int SellMatchWindow = 3;

        public int Buys { get; private set; }
        /// <summary>盤面のミニオンと対応が付いた売却の数（schema 1.6.0。1.5.x 以前はゴールド獲得も含んでいた）</summary>
        public int Sells { get; private set; }
        /// <summary>売却以外で増えたゴールド（<see cref="ShopAction.GoldGain"/> の数）。schema 1.6.0</summary>
        public int GoldGained { get; private set; }
        public int Rerolls { get; private set; }
        public int TierUps { get; private set; }
        public int HeroPowerUses { get; private set; }
        public int SpellsPlayed { get; private set; }
        public int Plays { get; private set; }
        public int Triples { get; private set; }
        public int DarkGifts { get; private set; }
        public int Ticks { get; private set; }

        /// <summary>新しいショップターンの開始時に呼ぶ。最初の tick はベースラインになる。</summary>
        public void Reset()
        {
            _prev = null;
            _everInShop.Clear();
            _heroPowersSeen.Clear();
            _lastOffered = null;
            _shopSeen = false;
            _vanished.Clear();
            _pendingSells.Clear();
            Buys = Sells = GoldGained = Rerolls = TierUps = HeroPowerUses = SpellsPlayed = Plays = Triples = DarkGifts = Ticks = 0;
        }

        /// <summary>1 tick を観測し、この tick で検出した行動を返す（無ければ空）。</summary>
        public List<ShopAction> Observe(ShopTick tick)
        {
            var actions = new List<ShopAction>();
            Ticks++;
            if (tick.OfferedIds.Count > 0)
                _lastOffered = tick.OfferedIds.OrderBy(x => x).ToList();
            _heroPowersSeen.UnionWith(tick.HeroPowerIds);
            if (_prev == null)
            {
                _prev = tick;
                _everInShop.UnionWith(tick.ShopIds);
                if (tick.ShopIds.Count > 0)
                {
                    _shopSeen = true;
                    actions.Add(ShopSnapshot(ShopTrigger.TurnStart, tick.ShopIds));
                }
                return actions;
            }
            var prev = _prev;

            // 手札に新しく現れた ID: ショップに並んでいたなら購入、発見の作成元が分かるなら dark_gift / discover
            foreach (var id in tick.HandIds)
            {
                if (prev.HandIds.Contains(id)) continue;
                if (_everInShop.Contains(id))
                {
                    Buys++;
                    actions.Add(new ShopAction { Type = ShopAction.Buy, EntityId = id });
                }
                else if (tick.HandSources.TryGetValue(id, out var source))
                {
                    var offered = _lastOffered != null && _lastOffered.Contains(id) ? _lastOffered : null;
                    if (source == CardSource.DarkGift)
                    {
                        // 3 択を観測できなくても数える（ボタン由来のカードは選んだ 1 枚だけ）
                        DarkGifts++;
                        actions.Add(new ShopAction { Type = ShopAction.DarkGift, EntityId = id, Source = source, OfferedIds = offered });
                    }
                    else if (offered != null)
                    {
                        // ヒーローパワー / トリプル報酬が作るカードは発見以外にもある（Barov の Tavern Coin、A. F. Kay の Triple Reward、
                        // トリプルで出来たゴールデン）。3 択に含まれていたものだけを「発見して選んだ」として記録する
                        actions.Add(new ShopAction { Type = ShopAction.Discover, EntityId = id, Source = source, OfferedIds = offered });
                    }
                }
            }

            // プレイ: 手札にあった ID が自分の盤面に現れた（ミニオン）
            foreach (var id in tick.BoardIds)
            {
                if (!prev.BoardIds.Contains(id) && prev.HandIds.Contains(id))
                {
                    Plays++;
                    actions.Add(new ShopAction { Type = ShopAction.Play, EntityId = id });
                }
            }

            // リロール: ショップが丸ごと入れ替わった（購入では 1 枚ずつしか減らない）
            var rerolled = prev.ShopIds.Count > 0 && tick.ShopIds.Count > 0 && !prev.ShopIds.Overlaps(tick.ShopIds);
            if (rerolled)
            {
                Rerolls++;
                actions.Add(new ShopAction { Type = ShopAction.Reroll });
            }

            // ショップに並んだカード（schema 1.5.0）。ターン開始時とリロールは全体、途中で増えたカードは増えた分だけ
            var fresh = tick.ShopIds.Where(id => !_everInShop.Contains(id)).ToList();
            if (rerolled)
                actions.Add(ShopSnapshot(ShopTrigger.Reroll, tick.ShopIds));
            else if (fresh.Count > 0)
            {
                if (!_shopSeen)
                    actions.Add(ShopSnapshot(ShopTrigger.TurnStart, tick.ShopIds));
                else if (prev.ShopIds.Count == 0)
                    actions.Add(ShopSnapshot(ShopTrigger.Refill, tick.ShopIds));
                else
                    actions.Add(ShopSnapshot(ShopTrigger.Added, fresh));
            }
            if (tick.ShopIds.Count > 0) _shopSeen = true;

            // 売却: 戻ったゴールドの分（売却は 1 ゴールド戻る）。どのミニオンかは盤面から消えた Entity と突き合わせる
            for (var i = 0; i < _vanished.Count; i++) _vanished[i] = (_vanished[i].Id, _vanished[i].Age + 1);
            for (var i = 0; i < _pendingSells.Count; i++) _pendingSells[i]++;
            foreach (var id in prev.BoardIds)
            {
                if (!tick.BoardIds.Contains(id) && !tick.HandIds.Contains(id))
                    _vanished.Add((id, 0));
            }
            var gained = GoldReturned(prev, tick);
            for (var i = 0; i < gained; i++) _pendingSells.Add(0);
            while (_pendingSells.Count > 0 && _vanished.Count > 0)
            {
                // 古いものから対応付ける
                _pendingSells.RemoveAt(0);
                var v = _vanished[0];
                _vanished.RemoveAt(0);
                Sells++;
                actions.Add(new ShopAction { Type = ShopAction.Sell, EntityId = v.Id, Value = 1 });
            }
            while (_pendingSells.Count > 0 && _pendingSells[0] >= SellMatchWindow)
            {
                // 盤面から消えたミニオンが見つからない減少は、売却ではなくゴールド獲得（酒場のコイン、青甲羅トートラン等）
                _pendingSells.RemoveAt(0);
                actions.Add(GoldGain());
            }
            _vanished.RemoveAll(v => v.Age >= SellMatchWindow);

            // ティアアップ
            if (tick.TechLevel > prev.TechLevel && prev.TechLevel > 0)
            {
                TierUps += tick.TechLevel - prev.TechLevel;
                actions.Add(new ShopAction { Type = ShopAction.TierUp, Value = tick.TechLevel });
            }

            // ヒーローパワー: このターンに PLAY 中だった Entity ごとに EXHAUSTED が立った回数（使用後にゾーンが変わっても追う）。
            // 同じ tick にヒーローパワー由来の発見があれば、選んだカードをこのイベントに載せる
            foreach (var hpId in _heroPowersSeen)
            {
                if (!tick.ExhaustedHeroPowerIds.Contains(hpId) || prev.ExhaustedHeroPowerIds.Contains(hpId)) continue;
                HeroPowerUses++;
                var hp = new ShopAction { Type = ShopAction.HeroPower };
                var pick = actions.FirstOrDefault(a => a.Type == ShopAction.Discover && a.Source == CardSource.HeroPower);
                if (pick != null)
                {
                    actions.Remove(pick);
                    hp.EntityId = pick.EntityId;
                    hp.OfferedIds = pick.OfferedIds;
                }
                actions.Add(hp);
            }

            // スペル: 手札にあったスペルが手札から消えた（プレイ）
            foreach (var id in prev.HandSpellIds)
            {
                if (!tick.HandIds.Contains(id))
                {
                    SpellsPlayed++;
                    actions.Add(new ShopAction { Type = ShopAction.Spell, EntityId = id });
                }
            }

            // トリプル
            if (tick.Triples > prev.Triples)
            {
                var n = tick.Triples - prev.Triples;
                Triples += n;
                actions.Add(new ShopAction { Type = ShopAction.Triple, Value = n });
            }

            _everInShop.UnionWith(tick.ShopIds);
            _prev = tick;
            return actions;
        }

        /// <summary>
        /// ショップ終了時に呼ぶ。対応付けを待っていた減少をゴールド獲得として返す（events.jsonl の件数を actions.gold_gained と揃える）。
        /// 消えたミニオンが残っていれば Observe の時点で対応付いているので、ここに残るのは盤面と無関係な減少だけ。
        /// </summary>
        public List<ShopAction> FlushPending()
        {
            var actions = new List<ShopAction>();
            foreach (var _ in _pendingSells)
                actions.Add(GoldGain());
            _pendingSells.Clear();
            _vanished.Clear();
            return actions;
        }

        /// <summary>
        /// tick 間に戻ったゴールド（売却・コイン等）。所持ゴールド = RESOURCES − RESOURCES_USED + TEMP_RESOURCES なので
        /// 戻った分 = 所持ゴールドの増分 + 払った分 = −ΔRESOURCES_USED + ΔTEMP_RESOURCES + ΔNUM_RESOURCES_SPENT_THIS_GAME。
        /// RESOURCES（上限）が変わった tick は上限の変化と区別できないので数えない（ショップ中に上限が変わることはまれ）
        /// </summary>
        private static int GoldReturned(ShopTick prev, ShopTick tick)
        {
            if (!prev.ResourcesUsed.HasValue || !tick.ResourcesUsed.HasValue || prev.Resources != tick.Resources) return 0;
            var n = prev.ResourcesUsed.Value - tick.ResourcesUsed.Value;
            if (prev.TempResources.HasValue && tick.TempResources.HasValue) n += tick.TempResources.Value - prev.TempResources.Value;
            if (prev.ResourcesSpent.HasValue && tick.ResourcesSpent.HasValue) n += tick.ResourcesSpent.Value - prev.ResourcesSpent.Value;
            return n > 0 ? n : 0;
        }

        private ShopAction GoldGain()
        {
            GoldGained++;
            return new ShopAction { Type = ShopAction.GoldGain, Value = 1 };
        }

        private static ShopAction ShopSnapshot(string trigger, IEnumerable<int> ids)
            => new ShopAction { Type = ShopAction.Shop, Trigger = trigger, ShopIds = ids.OrderBy(x => x).ToList() };

        public TurnActions ToActions(int? goldSpent) => new TurnActions
        {
            GoldSpent = goldSpent,
            Buys = Buys,
            Sells = Sells,
            GoldGained = GoldGained,
            Rerolls = Rerolls,
            TierUps = TierUps,
            HeroPowerUses = HeroPowerUses,
            SpellsPlayed = SpellsPlayed,
            DarkGifts = DarkGifts,
        };
    }
}
