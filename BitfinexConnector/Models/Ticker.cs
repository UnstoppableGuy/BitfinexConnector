namespace BitfinexConnector.Models
{
    public class Ticker
    {
        public string Pair { get; set; }
        public decimal BidPrice { get; set; }
        public decimal BidSize { get; set; }
        public decimal AskPrice { get; set; }
        public decimal AskSize { get; set; }
        public decimal DailyChange { get; set; }
        public decimal DailyChangeRelative { get; set; }
        public decimal LastPrice { get; set; }
        public decimal Volume { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
    }
}
