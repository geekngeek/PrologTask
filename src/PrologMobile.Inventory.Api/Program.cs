using PrologMobile.Inventory.Api;
using PrologMobile.Inventory.Core.Aggregation;
using PrologMobile.Inventory.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CategoryAggregator>();
builder.Services.AddScoped<ICategorySummaryService, CategorySummaryService>();
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddOptions<InventoryApiOptions>().ValidateOnStart();
builder.Services.AddExceptionHandler<InventoryExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new UtcTimestampJsonConverter());
    options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
    if (builder.Environment.IsDevelopment())
        options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.MapGet("/api/categories", async (ICategorySummaryService summaries, CancellationToken cancellationToken) =>
    Results.Ok(await summaries.GetSummaryAsync(cancellationToken)));

app.Run();
