namespace Luxodd.Game.Scripts.Network.Payloads
{
    public class LevelEndResponsePayload
    {
#if NEWTONSOFT_JSON
        [Newtonsoft.Json.JsonProperty("prize_tickets")]
        public Newtonsoft.Json.Linq.JArray PrizeTickets { get; set; }
#else
        public object[] PrizeTickets { get; set; }
#endif
    }
}
