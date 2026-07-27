using System.Net.Http;
using System.Net.Http.Json;

namespace AaronOS.Modules.Nutrition.Usda;

public record UsdaIngredientFacts(
    string Name, int FdcId, decimal? CaloriesPer100g, decimal? ProteinPer100g,
    decimal? FatPer100g, decimal? CarbsPer100g, decimal? FiberPer100g, decimal? SodiumMgPer100g);

/// <summary>
/// Thin client for the two USDA FoodData Central endpoints this app needs — search and food
/// detail. Not a general-purpose FDC SDK. Owns a single static HttpClient, matching
/// AaronOS.Modules.Finance.Plaid.PlaidApiClient's pattern (this app doesn't register
/// IHttpClientFactory anywhere).
/// </summary>
public class UsdaApiClient(UsdaCredentialStore credentialStore)
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://api.nal.usda.gov/fdc/v1";

    private string RequireApiKey()
    {
        var apiKey = credentialStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("No USDA FoodData Central API key configured.");
        }

        return apiKey;
    }

    public async Task<List<UsdaSearchResult>> SearchAsync(string query)
    {
        var apiKey = RequireApiKey();
        var url = $"{BaseUrl}/foods/search?query={Uri.EscapeDataString(query)}&api_key={apiKey}&pageSize=25";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UsdaSearchResponse>();
        return body?.Foods ?? [];
    }

    public async Task<UsdaIngredientFacts> GetFactsAsync(int fdcId)
    {
        var apiKey = RequireApiKey();
        var url = $"{BaseUrl}/food/{fdcId}?api_key={apiKey}";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var detail = (await response.Content.ReadFromJsonAsync<UsdaFoodDetail>())!;

        decimal? Find(string nameContains) => detail.FoodNutrients
            .FirstOrDefault(n => n.Nutrient.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            ?.Amount;

        return new UsdaIngredientFacts(
            detail.Description,
            detail.FdcId,
            Find("Energy"),
            Find("Protein"),
            Find("Total lipid"),
            Find("Carbohydrate"),
            Find("Fiber"),
            Find("Sodium"));
    }
}
