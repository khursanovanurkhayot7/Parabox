#if NEWTONSOFT_JSON
using Newtonsoft.Json;
#endif
using Luxodd.Game.Scripts.Network;

namespace Luxodd.Game.Scripts.Network.Payloads
{
    public class MerchantPrizeInfoResponse : CommandResponse
    {
#if NEWTONSOFT_JSON
        [JsonProperty("is_active")] public bool IsActive { get; set; }
        [JsonProperty("payload")] public new MerchantPrizeInfoPayload Payload { get; set; }
#else
        public bool IsActive { get; set; }
        public new MerchantPrizeInfoPayload Payload { get; set; }
#endif
    }
}
