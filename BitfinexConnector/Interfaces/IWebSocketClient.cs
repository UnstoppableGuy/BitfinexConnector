using BitfinexConnector.Models;

namespace BitfinexConnector.Interfaces
{
    public interface IWebSocketClient
    {
        event Action<Trade> NewBuyTrade;
        event Action<Trade> NewSellTrade;
        event Action<Candle> CandleSeriesProcessing;
        void SubscribeTrades(string pair, int maxCount = 100);
        void UnsubscribeTrades(string pair);
        void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0);
        void UnsubscribeCandles(string pair);
        void Dispose();
    }
}
