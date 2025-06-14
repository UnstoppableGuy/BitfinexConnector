using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;

namespace BitfinexConnector.Mappers
{
    public class BitfinexCandleMapper : IDataMapper<decimal[], Candle>
    {
        public Candle MapFromArray(decimal[] source, string pair = null)
        {
            if (source == null || source.Length < 6)
                throw new ArgumentException("Invalid candle data format");

            return new Candle
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)source[0]),
                Open = source[1],
                Close = source[2],
                High = source[3],
                Low = source[4],
                Volume = source[5],
                Pair = pair
            };
        }

        public IEnumerable<Candle> MapFromArrays(IEnumerable<decimal[]> source, string pair = null)
        {
            return source.Select(s => MapFromArray(s, pair));
        }
    }
}
