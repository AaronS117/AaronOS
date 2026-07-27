using System.Text.Json.Serialization;

namespace AaronOS.Modules.Nutrition.Usda;

public record UsdaSearchResult(
    [property: JsonPropertyName("fdcId")] int FdcId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dataType")] string? DataType);

public record UsdaSearchResponse(
    [property: JsonPropertyName("foods")] List<UsdaSearchResult> Foods);

public record UsdaNutrientInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unitName")] string UnitName);

public record UsdaFoodNutrient(
    [property: JsonPropertyName("nutrient")] UsdaNutrientInfo Nutrient,
    [property: JsonPropertyName("amount")] decimal? Amount);

public record UsdaFoodDetail(
    [property: JsonPropertyName("fdcId")] int FdcId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("foodNutrients")] List<UsdaFoodNutrient> FoodNutrients);
