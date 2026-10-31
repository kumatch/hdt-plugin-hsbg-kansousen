using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HsbgKansousen.Output
{
    /// <summary>ログ JSON のシリアライズ設定。プラグインとテストで共有する。</summary>
    public static class JsonSettings
    {
        public static JsonSerializerSettings Create() => new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = false,
                },
            },
            // 「取れなかった」を null で明示する（キーを省略しない）
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            Formatting = Formatting.Indented,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
        };
    }
}
