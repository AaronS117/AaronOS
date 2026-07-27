using System.Text.Json;
using System.Text.Json.Serialization;

namespace AaronOS.Modules.Medical.Withings;

/// <summary>
/// Every Withings response is wrapped in this envelope and returns HTTP 200 regardless of outcome.
/// <c>status == 0</c> is success; anything else is an error carried in the body of a "successful"
/// HTTP response, so the status code alone tells you nothing.
/// </summary>
public record WithingsEnvelope<T>(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("body")] T? Body,
    [property: JsonPropertyName("error")] string? Error);

public record WithingsTokenBody(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("userid")] string? UserId,
    [property: JsonPropertyName("scope")] string? Scope);

public record WithingsSleepSummaryBody(
    [property: JsonPropertyName("series")] List<WithingsSleepSeries>? Series,
    [property: JsonPropertyName("more")] bool More,
    [property: JsonPropertyName("offset")] int Offset);

public record WithingsSleepSeries(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("startdate")] long StartDate,
    [property: JsonPropertyName("enddate")] long EndDate,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("data")] WithingsSleepData? Data);

/// <summary>
/// Nightly aggregates. All durations are seconds. Every field is nullable because which ones come
/// back depends on the device generation and firmware — an absent field must stay absent rather than
/// silently become zero, since "no REM recorded" and "zero REM" are different claims.
/// </summary>
public record WithingsSleepData(
    [property: JsonPropertyName("total_sleep_time")] int? TotalSleepTime,
    [property: JsonPropertyName("lightsleepduration")] int? LightSleepDuration,
    [property: JsonPropertyName("deepsleepduration")] int? DeepSleepDuration,
    [property: JsonPropertyName("remsleepduration")] int? RemSleepDuration,
    [property: JsonPropertyName("wakeupduration")] int? WakeUpDuration,
    [property: JsonPropertyName("durationtosleep")] int? DurationToSleep,
    [property: JsonPropertyName("wakeupcount")] int? WakeUpCount,
    [property: JsonPropertyName("sleep_score")] int? SleepScore,
    [property: JsonPropertyName("hr_average")] int? HeartRateAverage,
    [property: JsonPropertyName("hr_min")] int? HeartRateMin,
    [property: JsonPropertyName("hr_max")] int? HeartRateMax,
    [property: JsonPropertyName("rr_average")] int? BreathingRateAverage,
    [property: JsonPropertyName("breathing_disturbances_intensity")] int? BreathingDisturbances,
    [property: JsonPropertyName("snoring")] int? Snoring,
    [property: JsonPropertyName("snoringepisodecount")] int? SnoringEpisodeCount)
{
    /// <summary>
    /// Time actually asleep. Prefers the API's own total and falls back to summing the stages, since
    /// <c>total_sleep_time</c> is a newer field that older units do not report.
    /// </summary>
    public int AsleepSeconds =>
        TotalSleepTime
        ?? (LightSleepDuration ?? 0) + (DeepSleepDuration ?? 0) + (RemSleepDuration ?? 0);
}

public static class WithingsJson
{
    /// <summary>
    /// Withings is inconsistent about quoting numbers — the same field can arrive as 12345 or
    /// "12345" depending on the endpoint. Reading numbers from strings costs nothing and removes a
    /// whole class of deserialization failure.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
