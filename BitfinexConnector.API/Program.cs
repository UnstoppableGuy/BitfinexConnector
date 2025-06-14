using BitfinexConnector.Clients;
using BitfinexConnector.Interfaces;
using BitfinexConnector.Mappers;
using BitfinexConnector.Models;
using BitfinexConnector.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();


builder.Services.AddHttpClient<BitfinexExchange>();
builder.Services.AddHttpClient<RestClient>(client =>
{
    client.BaseAddress = new Uri("https://api-pub.bitfinex.com/v2/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IRestClient>(provider =>
{
    var httpClient = provider.GetRequiredService<HttpClient>();
    var logger = provider.GetRequiredService<ILogger<RestClient>>();
    var tradeMapper = provider.GetRequiredService<IDataMapper<decimal[], Trade>>();
    var candleMapper = provider.GetRequiredService<IDataMapper<decimal[], Candle>>();
    var tickerMapper = provider.GetRequiredService<IDataMapper<decimal[], Ticker>>();

    return new RestClient(httpClient, logger, tradeMapper, candleMapper, tickerMapper);
});

//Mappers
builder.Services.AddSingleton<IDataMapper<decimal[], Trade>, BitfinexTradeMapper>();
builder.Services.AddSingleton<IDataMapper<decimal[], Candle>, BitfinexCandleMapper>();
builder.Services.AddSingleton<IDataMapper<decimal[], Ticker>, BitfinexTickerMapper>();

builder.Services.AddScoped<IPortfolioCalculator, PortfolioCalculatorService>();
builder.Services.AddSingleton<IExchange, BitfinexExchange>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
