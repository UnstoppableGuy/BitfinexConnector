using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BitfinexConnector.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IWebSocketClient _connector;
        private readonly IPortfolioCalculator _portfolioCalculator;
        private readonly ILogger<MainWindow> _logger;
        private DispatcherTimer _statusTimer { get; set; }

        public ObservableCollection<TradeDisplayModel> BuyTrades { get; set; }
        public ObservableCollection<TradeDisplayModel> SellTrades { get; set; }
        public ObservableCollection<CandleDisplayModel> Candles { get; set; }
        public ObservableCollection<PortfolioDisplayModel> Portfolio { get; set; }

        private readonly Dictionary<string, decimal> _holdings = new()
        {
            { "BTC", 1m },
            { "XRP", 15000m },
            { "XMR", 50m },
            { "DSH", 30m }
        };

        private bool _isConnected = false;
        private const int MAX_DISPLAY_ITEMS = 100;
        private int _totalTradesCount = 0;
        private int _totalCandlesCount = 0;

        public MainWindow(IWebSocketClient connector, IPortfolioCalculator portfolioCalculator, ILogger<MainWindow> logger)
        {
            InitializeComponent();

            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
            _portfolioCalculator = portfolioCalculator ?? throw new ArgumentNullException(nameof(portfolioCalculator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeCollections();
            SetupEventHandlers();
            InitializePortfolio();
            InitializeStatusTimer();
        }

        private void InitializeCollections()
        {
            BuyTrades = new ObservableCollection<TradeDisplayModel>();
            SellTrades = new ObservableCollection<TradeDisplayModel>();
            Candles = new ObservableCollection<CandleDisplayModel>();
            Portfolio = new ObservableCollection<PortfolioDisplayModel>();

            BuyTradesGrid.ItemsSource = BuyTrades;
            SellTradesGrid.ItemsSource = SellTrades;
            CandlesGrid.ItemsSource = Candles;
            PortfolioGrid.ItemsSource = Portfolio;
        }

        private void SetupEventHandlers()
        {
            _connector.NewBuyTrade += OnNewBuyTrade;
            _connector.NewSellTrade += OnNewSellTrade;
            _connector.CandleSeriesProcessing += OnNewCandle;
        }

        private void InitializePortfolio()
        {
            foreach (var holding in _holdings)
            {
                Portfolio.Add(new PortfolioDisplayModel
                {
                    Currency = holding.Key,
                    Amount = holding.Value,
                    ValueInUSD = 0,
                    PercentageOfTotal = 0
                });
            }
        }

        private void InitializeStatusTimer()
        {
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += UpdateDataCountDisplay;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isConnected) return;

                var selectedPair = ((ComboBoxItem)PairComboBox.SelectedItem).Content.ToString();

                UpdateConnectionStatus("Connecting...", Colors.Orange);
                ConnectButton.IsEnabled = false;
                PairComboBox.IsEnabled = false;

                // Clear existing data
                BuyTrades.Clear();
                SellTrades.Clear();
                Candles.Clear();
                _totalTradesCount = 0;
                _totalCandlesCount = 0;

                // Subscribe to trades and candles
                _connector.SubscribeTrades(selectedPair, 100);
                _connector.SubscribeCandles(selectedPair, 60); // 1 minute candles

                _isConnected = true;
                UpdateConnectionStatus("Connected", Colors.LimeGreen);
                DisconnectButton.IsEnabled = true;
                ConnectionStatusText.Text = $"Connected to {selectedPair}";

                _statusTimer.Start();

                _logger.LogInformation("Connected to {Pair}", selectedPair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting");
                UpdateConnectionStatus("Connection Failed", Colors.Red);
                ConnectButton.IsEnabled = true;
                PairComboBox.IsEnabled = true;
                MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isConnected) return;

                var selectedPair = ((ComboBoxItem)PairComboBox.SelectedItem).Content.ToString();

                _connector.UnsubscribeTrades(selectedPair);
                _connector.UnsubscribeCandles(selectedPair);

                _isConnected = false;
                _statusTimer.Stop();

                UpdateConnectionStatus("Disconnected", Colors.Gray);
                ConnectButton.IsEnabled = true;
                PairComboBox.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                ConnectionStatusText.Text = "Disconnected";

                _logger.LogInformation("Disconnected from {Pair}", selectedPair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting");
                MessageBox.Show($"Disconnection failed: {ex.Message}", "Disconnection Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshPortfolioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshPortfolioButton.IsEnabled = false;
                RefreshPortfolioButton.Content = "🔄 Calculating...";

                var balances = await _portfolioCalculator.CalculatePortfolioBalanceAsync(_holdings);
                decimal totalValue = 0;

                // Update portfolio display with USD values
                foreach (var item in Portfolio)
                {
                    if (_holdings.TryGetValue(item.Currency, out var amount))
                    {
                        var rate = await GetExchangeRateToUSD(item.Currency);
                        item.ValueInUSD = amount * rate;
                        totalValue += item.ValueInUSD;
                    }
                }

                // Calculate percentages
                foreach (var item in Portfolio)
                {
                    item.PercentageOfTotal = totalValue > 0 ? item.ValueInUSD / totalValue : 0;
                }

                // Update UI summary
                TotalValueText.Text = totalValue.ToString("C");
                LastUpdatedText.Text = DateTime.Now.ToString("HH:mm:ss");

                ConnectionStatusText.Text = $"Portfolio updated - Total: {totalValue:C}";
                _logger.LogInformation("Portfolio refreshed successfully. Total value: {TotalValue:C}", totalValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing portfolio");
                MessageBox.Show($"Portfolio refresh failed: {ex.Message}", "Portfolio Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshPortfolioButton.IsEnabled = true;
                RefreshPortfolioButton.Content = "🔄 Refresh Portfolio";
            }
        }

        private async Task<decimal> GetExchangeRateToUSD(string currency)
        {
            // Simplified exchange rates - in production, get from API
            return currency switch
            {
                "BTC" => 45000m,
                "XRP" => 0.5m,
                "XMR" => 150m,
                "DASH" => 80m,
                _ => 1m
            };
        }

        private void OnNewBuyTrade(Trade trade)
        {
            Dispatcher.Invoke(() =>
            {
                var tradeModel = new TradeDisplayModel(trade);
                BuyTrades.Insert(0, tradeModel);
                _totalTradesCount++;

                // Keep only recent items for performance
                while (BuyTrades.Count > MAX_DISPLAY_ITEMS)
                {
                    BuyTrades.RemoveAt(BuyTrades.Count - 1);
                }

                UpdateLastUpdateTime();
                AnimateNewData(tradeModel);
            });
        }

        private void OnNewSellTrade(Trade trade)
        {
            Dispatcher.Invoke(() =>
            {
                var tradeModel = new TradeDisplayModel(trade);
                SellTrades.Insert(0, tradeModel);
                _totalTradesCount++;

                // Keep only recent items for performance
                while (SellTrades.Count > MAX_DISPLAY_ITEMS)
                {
                    SellTrades.RemoveAt(SellTrades.Count - 1);
                }

                UpdateLastUpdateTime();
                AnimateNewData(tradeModel);
            });
        }

        private void OnNewCandle(Candle candle)
        {
            Dispatcher.Invoke(() =>
            {
                // Update existing candle or add new one
                var existing = Candles.FirstOrDefault(c => c.Timestamp == candle.Timestamp);
                if (existing != null)
                {
                    existing.UpdateFromCandle(candle);
                }
                else
                {
                    var candleModel = new CandleDisplayModel(candle);
                    Candles.Insert(0, candleModel);
                    _totalCandlesCount++;

                    // Keep only recent items for performance
                    while (Candles.Count > MAX_DISPLAY_ITEMS)
                    {
                        Candles.RemoveAt(Candles.Count - 1);
                    }

                    AnimateNewData(candleModel);
                }

                UpdateLastUpdateTime();
            });
        }

        private void AnimateNewData(object dataModel)
        {
            // Simple flash animation for new data
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            var flashCount = 0;

            timer.Tick += (s, e) =>
            {
                flashCount++;
                if (flashCount >= 6) // Flash 3 times
                {
                    timer.Stop();
                }
            };

            timer.Start();
        }

        private void UpdateConnectionStatus(string status, Color color)
        {
            StatusText.Text = status;
            StatusIndicator.Fill = new SolidColorBrush(color);
        }

        private void UpdateLastUpdateTime()
        {
            LastUpdateText.Text = $"Last Update: {DateTime.Now:HH:mm:ss.fff}";
        }

        private void UpdateDataCountDisplay(object sender, EventArgs e)
        {
            DataCountText.Text = $"{_totalTradesCount} trades, {_totalCandlesCount} candles";
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _statusTimer?.Stop();
                _connector?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            base.OnClosed(e);
        }
    }

    // Enhanced display models with INotifyPropertyChanged for automatic UI updates
    public class TradeDisplayModel : INotifyPropertyChanged
    {
        private long _id;
        private DateTimeOffset _timestamp;
        private decimal _amount;
        private decimal _price;
        private string _pair;

        public TradeDisplayModel(Trade trade)
        {
            Id = (long)Convert.ToDecimal(trade.Id);
            Timestamp = trade.Time;
            Amount = Math.Abs(trade.Amount); // Show absolute value
            Price = trade.Price;
            Pair = trade.Pair;
        }

        public long Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public DateTimeOffset Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }

        public string Pair
        {
            get => _pair;
            set => SetProperty(ref _pair, value);
        }

        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class CandleDisplayModel : INotifyPropertyChanged
    {
        private DateTimeOffset _timestamp;
        private decimal _open;
        private decimal _close;
        private decimal _high;
        private decimal _low;
        private decimal _volume;
        private string _pair;

        public CandleDisplayModel(Candle candle)
        {
            UpdateFromCandle(candle);
        }

        public void UpdateFromCandle(Candle candle)
        {
            Timestamp = candle.Timestamp;
            Open = candle.Open;
            Close = candle.Close;
            High = candle.High;
            Low = candle.Low;
            Volume = candle.Volume;
            Pair = candle.Pair;
        }

        public DateTimeOffset Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public decimal Open
        {
            get => _open;
            set => SetProperty(ref _open, value);
        }

        public decimal Close
        {
            get => _close;
            set => SetProperty(ref _close, value);
        }

        public decimal High
        {
            get => _high;
            set => SetProperty(ref _high, value);
        }

        public decimal Low
        {
            get => _low;
            set => SetProperty(ref _low, value);
        }

        public decimal Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        public string Pair
        {
            get => _pair;
            set => SetProperty(ref _pair, value);
        }

        public string TimeString => Timestamp.ToString("HH:mm:ss");

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class PortfolioDisplayModel : INotifyPropertyChanged
    {
        private string _currency;
        private decimal _amount;
        private decimal _valueInUSD;
        private decimal _percentageOfTotal;

        public string Currency
        {
            get => _currency;
            set => SetProperty(ref _currency, value);
        }

        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        public decimal ValueInUSD
        {
            get => _valueInUSD;
            set => SetProperty(ref _valueInUSD, value);
        }

        public decimal PercentageOfTotal
        {
            get => _percentageOfTotal;
            set => SetProperty(ref _percentageOfTotal, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}