using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;

namespace BitfinexConnector.Mappers
{
    public class BitfinexTickerMapper : IDataMapper<decimal[], Ticker>
    {
        public Ticker MapFromArray(decimal[] source, string pair = null)
        {
            if (source == null || source.Length < 10)
                throw new ArgumentException("Invalid ticker data format");

            return new Ticker
            {
                Pair = pair ?? string.Empty,
                BidPrice = source[0],
                BidSize = source[1],
                AskPrice = source[2],
                AskSize = source[3],
                DailyChange = source[4],
                DailyChangeRelative = source[5],
                LastPrice = source[6],
                Volume = source[7],
                High = source[8],
                Low = source[9]
            };
        }

        public IEnumerable<Ticker> MapFromArrays(IEnumerable<decimal[]> source, string pair = null)
        {
            return source.Select(s => MapFromArray(s, pair));
        }
    }
}
