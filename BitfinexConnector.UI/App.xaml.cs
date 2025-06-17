using BitfinexConnector.Clients;
using BitfinexConnector.Interfaces;
using BitfinexConnector.Mappers;
using BitfinexConnector.Models;
using BitfinexConnector.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;

namespace BitfinexConnector.UI
{
    public partial class App : Application
    {
        private IHost _host;

        public App()
        {        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                _host = CreateHostBuilder().Build();
                await _host.StartAsync();

                // Создаем MainWindow через DI контейнер
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске приложения: {ex.Message}\n\nДетали: {ex}",
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .UseSerilog((context, configuration) =>
                {
                    // Создаем папку для логов если её нет
                    var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDirectory);

                    configuration
                        .WriteTo.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                        .WriteTo.File(
                            path: Path.Combine(logDirectory, "HQTest-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                            shared: true)
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                        .Enrich.FromLogContext();
                })
                .ConfigureServices((context, services) =>
                {
                    try
                    {
                        // Register HttpClient
                        services.AddHttpClient();

                        // Register mappers
                        services.AddSingleton<IDataMapper<decimal[], Trade>, BitfinexTradeMapper>();
                        services.AddSingleton<IDataMapper<decimal[], Candle>, BitfinexCandleMapper>();
                        services.AddSingleton<IDataMapper<decimal[], Ticker>, BitfinexTickerMapper>();

                        // Register clients
                        services.AddSingleton<IRestClient, RestClient>();
                        services.AddSingleton<IWebSocketClient, WebSocketClient>();
                        services.AddSingleton<ITestConnector, Connector>();

                        // Register exchange and portfolio calculator
                        services.AddSingleton<IExchange, BitfinexExchange>();
                        services.AddSingleton<IPortfolioCalculator, PortfolioCalculatorService>();

                        // Register main window как Singleton для WPF приложения
                        services.AddSingleton<MainWindow>();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при настройке DI: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        throw;
                    }
                });
        }
    }
}