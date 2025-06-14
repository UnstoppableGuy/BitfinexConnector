using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using System.Globalization;

namespace BitfinexConnector.Mappers
{
    public class BitfinexTradeMapper : IDataMapper<decimal[], Trade>
    {
        public Trade MapFromArray(decimal[] source, string pair = null)
        {
            if (source == null || source.Length < 4)
                throw new ArgumentException("Invalid trade data format");

            return new Trade
            {
                Id = source[0].ToString(CultureInfo.InvariantCulture),
                Time = DateTimeOffset.FromUnixTimeMilliseconds((long)source[1]),
                Amount = source[2],
                Price = source[3],
                Side = source[2] > 0 ? "Buy" : "Sell",
                Pair = pair ?? string.Empty
            };
        }

        public IEnumerable<Trade> MapFromArrays(IEnumerable<decimal[]> source, string pair = null)
        {
            return source.Select(s => MapFromArray(s, pair));
        }
    }
}
