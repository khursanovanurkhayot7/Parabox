using System;
using Luxodd.Game.Scripts.HelpersAndUtils.Logger;
using Luxodd.Game.Scripts.Network.Payloads;
#if NEWTONSOFT_JSON
using Newtonsoft.Json;
#endif

namespace Luxodd.Game.Scripts.Network.CommandHandler
{
    public class GetMerchantPrizeInfoRequestCommandHandler : BaseCommandHandler
    {
        public GetMerchantPrizeInfoRequestCommandHandler(WebSocketService webSocketService) : base(webSocketService)
        {
        }

        public override void SendCommand(Action onCommandComplete, params object[] parameters)
        {
            _onCommandCompletedCallback = onCommandComplete;
            SendStatus = CommandSendStatus.Pending;

            var commandRequest = new CommandRequestJson()
            {
                Type = nameof(CommandRequestType.GetMerchantPrizeInfoRequest)
            };

#if NEWTONSOFT_JSON
            var commandRequestJson = JsonConvert.SerializeObject(commandRequest);
            WebSocketService.SendCommand(CommandRequestType.GetMerchantPrizeInfoRequest, commandRequestJson,
                OnCommandResponseSuccessHandler);
#endif
        }

        protected override void OnCommandResponseSuccessHandler(CommandRequestHandler responseHandler)
        {
            base.OnCommandResponseSuccessHandler(responseHandler);

#if NEWTONSOFT_JSON
            var responseJson = responseHandler.RawResponse;
            var responseObject = JsonConvert.DeserializeObject<MerchantPrizeInfoResponse>(responseJson);
            LoggerHelper.Log(
                $"[{DateTime.Now}][{GetType().Name}][{nameof(OnCommandResponseSuccessHandler)}] OK, isActive: {responseObject?.IsActive}, payloadIsNull: {responseObject?.Payload == null}");

            ResponseHandler.Payload = responseObject;
#endif

            _onCommandCompletedCallback?.Invoke();
        }
    }
}
