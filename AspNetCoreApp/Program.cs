using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Nest;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "api/docs";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API v1");
});

app.MapGet("/", context =>
{
    context.Response.Redirect("/api/docs");
    return Task.CompletedTask;
});

app.MapGet("/useful-work", async () =>
{
    var mongoDatabase = GetMongoDatabase(builder.Configuration);
    var elasticClient = GetElasticSearchClient(builder.Configuration);

    var item = GenerateRandomItem();

    await Task.WhenAll(
        SaveItemToDbAsync(mongoDatabase, item),
        SaveItemToESAsync(elasticClient, item));

    await Task.WhenAll(
        GetItemFromDbAsync(mongoDatabase, item.Id),
        GetItemsFromEsAsync(elasticClient, item.Name));
});

app.Run();

static Item GenerateRandomItem() => new()
{
    Id = Guid.NewGuid(),
    Name = Faker.Country.Name(),
    Description = Faker.Lorem.Paragraph()
};

static async Task SaveItemToDbAsync(IMongoDatabase database, Item item)
{
    var collection = database.GetCollection<BsonDocument>("items");

    var itemDocument = new BsonDocument
    {
        { "_id", BsonValue.Create(item.Id) },
        { "name", item.Name },
        { "description", item.Description }
    };

    await collection.InsertOneAsync(itemDocument);
}

static async Task SaveItemToESAsync(IElasticClient client, Item item)
{
    await client.IndexDocumentAsync(item);
}

static async Task<Item> GetItemFromDbAsync(IMongoDatabase database, Guid id)
{
    var collection = database.GetCollection<BsonDocument>("items");

    var doc = await collection.Find(doc => doc["_id"] == new BsonBinaryData(id, GuidRepresentation.Standard)).FirstOrDefaultAsync();

    return new Item
    {
        Id = doc["_id"].AsGuid,
        Name = doc["name"].AsString,
        Description = doc["description"].AsString
    };
}

static async Task<Item[]> GetItemsFromEsAsync(IElasticClient client, string text)
{
    var searchResponse = await client.SearchAsync<Item>(s => s
       .Query(q => q
           .MultiMatch(m => m
               .Fields(f => f
                   .Field(f1 => f1.Id)
                   .Field(f2 => f2.Name)
                   .Field(f3 => f3.Description)
               )
               .Query(text)
           )
       )
   );

    var results = searchResponse.Documents;
    return results.ToArray();
}

static IElasticClient GetElasticSearchClient(IConfiguration configuration)
{
    var settings = configuration.GetSection("ElasticSearch");

    var uri = new Uri(settings["Url"]!);
    var indexName = settings["DefaultIndex"];

    var connectionSettings = new ConnectionSettings(uri).DefaultIndex(indexName);
    var elasticClient = new ElasticClient(connectionSettings);

    return elasticClient;
}

static IMongoDatabase GetMongoDatabase(IConfiguration configuration)
{
    var settings = configuration.GetSection("MongoDb");

    var client = new MongoClient(settings["ConnectionString"]);
    var database = client.GetDatabase(settings["DatabaseName"]);

    return database;
}

record Item
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string Description { get; set; } = default!;
}