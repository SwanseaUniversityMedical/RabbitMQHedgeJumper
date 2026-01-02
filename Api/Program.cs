using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using RabbitMQ.Client.Framing;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://keycloak:8080/realms/demo";
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = "api"
        };
    });

builder.Services.AddAuthorization();


//builder.Services.AddAuthentication("Bearer")
//    .AddJwtBearer("Bearer", options =>
//    {
//        options.Authority = "http://keycloak:8080/realms/demo";
//        options.RequireHttpsMetadata = false;

//        options.TokenValidationParameters = new TokenValidationParameters
//        {
//            ValidateAudience = false
//            //TODO: this need to change
//        };
//    });


builder.Services.AddAuthorization();

builder.Services.AddSingleton<IConnection>(sp =>
{
    var cfg = builder.Configuration;

    var factory = new ConnectionFactory
    {
        HostName = cfg["RabbitMq:Host"],
        UserName = cfg["RabbitMq:Username"],
        Password = cfg["RabbitMq:Password"]
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

var app = builder.Build();

 
app.UseAuthorization();

var exchange = builder.Configuration["RabbitMq:Exchange"]!;
var queue = builder.Configuration["RabbitMq:Queue"]!;
var dlq = builder.Configuration["RabbitMq:Dlq"]!;
var dlExchange = builder.Configuration["RabbitMq:Dlexchange"]!;
var routingKey = builder.Configuration["RabbitM:RoutingKey"]!;

await using (var channel = await app.Services.GetRequiredService<IConnection>()
    .CreateChannelAsync())
{
    await channel.ExchangeDeclareAsync(dlExchange, ExchangeType.Topic, true);
    await channel.QueueDeclareAsync(dlq, true, false, false);
    await channel.QueueBindAsync(dlq, dlExchange, dlq);

    await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, true);
    await channel.QueueDeclareAsync(queue, true, false, false, new Dictionary<string, object>
    {
        ["x-dead-letter-exchange"] = dlExchange,
        ["x-dead-letter-routing-key"] = dlq
    });

    await channel.QueueBindAsync(queue, exchange, routingKey);
}

app.MapPost("/messages", async (HttpRequest request, IConnection conn) =>
{
    await using var channel = await conn.CreateChannelAsync();

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var props = new BasicProperties { Persistent = true };

    await channel.BasicPublishAsync(
        exchange,
        routingKey,
        mandatory: false,
        props,
        Encoding.UTF8.GetBytes(body)
    );

    return Results.Accepted();
}).RequireAuthorization();

app.Run();
