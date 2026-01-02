using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

Console.WriteLine("Application starting - delay 30 seconds");
Console.WriteLine("Because can not get the healthcheck for Keycloak working ?????");
await Task.Delay(TimeSpan.FromSeconds(30));

var cfg = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()   // THIS IS REQUIRED
    .Build();

// 🔍 DEBUG THIS ONCE
Console.WriteLine($"Host     = {cfg["RabbitMq:Host"]}");
Console.WriteLine($"Username = {cfg["RabbitMq:Username"] ?? "<null>"}");
Console.WriteLine($"Password = {(cfg["RabbitMq:Password"] is null ? "<null>" : "***")}");

var factory = new ConnectionFactory
{
    HostName = cfg["RabbitMq:Host"],
    UserName = cfg["RabbitMq:Username"]
               ?? throw new InvalidOperationException("RabbitMq:Username missing"),
    Password = cfg["RabbitMq:Password"]
               ?? throw new InvalidOperationException("RabbitMq:Password missing")
  
};

var connection = await factory.CreateConnectionAsync();
var channel = await connection.CreateChannelAsync();

await channel.ExchangeDeclareAsync(cfg["RabbitMq:Dlexchange"]!, ExchangeType.Topic, true);
await channel.QueueDeclareAsync(cfg["RabbitMq:Dlq"]!, true, false, false);
await channel.QueueBindAsync(cfg["RabbitMq:Dlq"]!, cfg["RabbitMq:Dlexchange"]!, cfg["RabbitMq:Dlq"]!);

await channel.QueueDeclareAsync(
    cfg["RabbitMq:Queue"]!,
    true,
    false,
    false,
    new Dictionary<string, object>
    {
        ["x-dead-letter-exchange"] = cfg["RabbitMq:Dlexchange"]!,
        ["x-dead-letter-routing-key"] = cfg["RabbitMq:Dlq"]!
    });

var authClient = new HttpClient
{
    BaseAddress = new Uri(cfg["Auth:Authority"]!)
};
var token = await GetAccessTokenAsync(authClient, cfg);

var http = new HttpClient
{
    BaseAddress = new Uri(cfg["Api:BaseUrl"]!)
};

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg["Api:BaseUrl"]}/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        //Console.WriteLine($"send request to {cfg["Api:BaseUrl"]}/messages");
        //Console.WriteLine($"using token: {token}");
        
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        await channel.BasicAckAsync(ea.DeliveryTag, false);
    }
    catch (Exception e)
    {
        Console.WriteLine(e.ToString());
        await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
    }
};

await channel.BasicConsumeAsync(
    queue: cfg["RabbitMq:Queue"]!,
    autoAck: false,
    consumer);

Console.WriteLine("Worker running...");
await Task.Delay(Timeout.Infinite);

async Task<string> GetAccessTokenAsync(HttpClient client, IConfiguration cfg)
{
    Console.WriteLine("getting token from "+ cfg["Auth:TokenEndpoint"]);
    
    var response = await client.PostAsync(
        cfg["Auth:TokenEndpoint"]!,   // relative
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = cfg["Auth:ClientId"]!,
            ["client_secret"] = cfg["Auth:ClientSecret"]!
        })
    );

    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);

    return doc.RootElement.GetProperty("access_token").GetString()!;
}
