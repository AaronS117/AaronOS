using System.Text.Json;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.Withings;

namespace AaronOS.Modules.Medical.Tests;

public class WithingsSleepMapperTests
{
    /// <summary>Unix seconds for a UTC wall-clock time, so tests read as times rather than numbers.</summary>
    private static long Utc(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static WithingsSleepSeries Series(
        long start, long end, string zone = "UTC", WithingsSleepData? data = null, string? date = null) =>
        new(1, zone, start, end, date, data ?? Data());

    private static WithingsSleepData Data(
        int? total = 25200, int? light = null, int? deep = null, int? rem = null,
        int? awake = null, int? toSleep = null, int? wakeCount = null, int? score = null,
        int? hrAvg = null, int? hrMin = null, int? hrMax = null, int? rrAvg = null,
        int? breathing = null, int? snoring = null, int? snoringEpisodes = null) =>
        new(total, light, deep, rem, awake, toSleep, wakeCount, score,
            hrAvg, hrMin, hrMax, rrAvg, breathing, snoring, snoringEpisodes);

    [Fact]
    public void NightIsFiledUnderTheDateYouWokeUpNotTheDateYouWentToBed()
    {
        // The rule that matters most: 23:00 on the 20th through 07:00 on the 21st is the 21st's sleep,
        // because that is the morning it belongs to. Withings' own date field would say the 20th.
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), date: "2026-07-20")
        ]);

        var night = Assert.Single(nights);
        Assert.Equal(new DateOnly(2026, 7, 21), night.Date);
    }

    [Fact]
    public void WakeDateIsResolvedInTheSeriesTimezoneNotUtc()
    {
        // 03:00 UTC on the 21st is 22:00 on the 20th in Chicago, so this sleep belongs to the 20th.
        // The timestamps straddle the UTC date boundary on purpose: reading them as UTC files the
        // night on the 21st and this test fails, which is the whole point of it.
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 21, 1), Utc(2026, 7, 21, 3), "America/Chicago", Data(total: 7200))
        ]);

        Assert.Equal(new DateOnly(2026, 7, 20), Assert.Single(nights).Date);
    }

    [Fact]
    public void UnknownTimezoneFallsBackInsteadOfDroppingTheNight()
    {
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), "Mars/Olympus_Mons")
        ]);

        Assert.Single(nights);
    }

    [Fact]
    public void SecondsBecomeHours()
    {
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), data: Data(total: 27000))
        ]);

        Assert.Equal(7.5m, Assert.Single(nights).Hours);
    }

    [Fact]
    public void AsleepTimeFallsBackToSummingStagesWhenTotalIsAbsent()
    {
        // Older units do not report total_sleep_time at all.
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7),
                data: Data(total: null, light: 14400, deep: 5400, rem: 3600))
        ]);

        Assert.Equal(6.5m, Assert.Single(nights).Hours);
    }

    [Fact]
    public void PeriodsEndingOnTheSameDateAreCombinedAndCounted()
    {
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 3), data: Data(total: 14400, wakeCount: 1)),
            Series(Utc(2026, 7, 21, 4), Utc(2026, 7, 21, 7), data: Data(total: 10800, wakeCount: 2))
        ]);

        var night = Assert.Single(nights);
        Assert.Equal(7m, night.Hours);              // 4h + 3h, summed
        Assert.Equal(2, night.PeriodCount);
        Assert.True(night.IsFragmented);
        Assert.Equal(3, night.WakeUpCount);         // counts add up too
        Assert.Equal(new DateTime(2026, 7, 20, 23, 0, 0), night.BedTime);   // earliest start
        Assert.Equal(new DateTime(2026, 7, 21, 7, 0, 0), night.WakeTime);   // latest end
    }

    [Fact]
    public void PointInTimeReadingsComeFromTheLongestPeriod()
    {
        // A short nap must not drag the night's score and heart rate around.
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 6), data: Data(total: 25200, score: 80, hrAvg: 58)),
            Series(Utc(2026, 7, 21, 13), Utc(2026, 7, 21, 14), data: Data(total: 3600, score: 20, hrAvg: 90))
        ]);

        var night = Assert.Single(nights);
        Assert.Equal(80, night.SleepScore);
        Assert.Equal(58, night.HeartRateAverage);
    }

    [Fact]
    public void HeartRateRangeSpansAllPeriods()
    {
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 6), data: Data(total: 25200, hrMin: 52, hrMax: 70)),
            Series(Utc(2026, 7, 21, 13), Utc(2026, 7, 21, 14), data: Data(total: 3600, hrMin: 48, hrMax: 66))
        ]);

        var night = Assert.Single(nights);
        Assert.Equal(48, night.HeartRateMin);
        Assert.Equal(70, night.HeartRateMax);
    }

    [Fact]
    public void AbsentFieldsStayNullRatherThanBecomingZero()
    {
        // "This unit does not report REM" must never render as "you got no REM sleep".
        var night = Assert.Single(WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), data: Data(total: 25200))
        ]));

        Assert.Null(night.RemSeconds);
        Assert.Null(night.DeepSeconds);
        Assert.Null(night.SleepScore);
        Assert.Null(night.HeartRateAverage);
        Assert.Null(night.BreathingDisturbances);
        Assert.Equal("—", night.StagesDisplay);
    }

    [Fact]
    public void PartiallyReportedStagesSumOnlyWhatIsPresent()
    {
        var night = Assert.Single(WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 22), Utc(2026, 7, 21, 3), data: Data(total: 18000, deep: 3600)),
            Series(Utc(2026, 7, 21, 4), Utc(2026, 7, 21, 7), data: Data(total: 10800))
        ]));

        Assert.Equal(3600, night.DeepSeconds);
        Assert.Null(night.RemSeconds);
    }

    [Fact]
    public void SeparateNightsStaySeparate()
    {
        var nights = WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 21, 23), Utc(2026, 7, 22, 7)),
            Series(Utc(2026, 7, 19, 23), Utc(2026, 7, 20, 7))
        ]);

        Assert.Equal(2, nights.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), nights[0].Date);   // oldest first
        Assert.Equal(new DateOnly(2026, 7, 22), nights[1].Date);
    }

    [Fact]
    public void SeriesWithoutDataOrWithAnInvalidWindowAreSkipped()
    {
        var nights = WithingsSleepMapper.ToNights([
            new WithingsSleepSeries(1, "UTC", Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), null, null),
            Series(Utc(2026, 7, 21, 7), Utc(2026, 7, 21, 7))
        ]);

        Assert.Empty(nights);
    }

    [Fact]
    public void EfficiencyComparesTimeAsleepAgainstTimeInBed()
    {
        var night = Assert.Single(WithingsSleepMapper.ToNights([
            Series(Utc(2026, 7, 20, 23), Utc(2026, 7, 21, 7), data: Data(total: 21600))
        ]));

        Assert.Equal(8m, night.TimeInBedHours);
        Assert.Equal(75, night.EfficiencyPercent);   // 6h asleep in 8h abed
    }

    [Fact]
    public void RealResponseShapeDeserializesAndMaps()
    {
        // Guards the JSON contract itself: field names, the status envelope, and Withings' habit of
        // quoting numbers inconsistently.
        const string json = """
        {
          "status": 0,
          "body": {
            "series": [
              {
                "id": 2773588367,
                "timezone": "America/Chicago",
                "startdate": 1784696400,
                "enddate": 1784725200,
                "date": "2026-07-20",
                "data": {
                  "total_sleep_time": "25200",
                  "lightsleepduration": 14400,
                  "deepsleepduration": 7200,
                  "remsleepduration": 3600,
                  "wakeupduration": 1800,
                  "wakeupcount": 2,
                  "sleep_score": 74,
                  "hr_average": 58,
                  "hr_min": 49,
                  "hr_max": 71,
                  "rr_average": 15,
                  "breathing_disturbances_intensity": 12,
                  "snoring": 900,
                  "snoringepisodecount": 3
                }
              }
            ],
            "more": false,
            "offset": 0
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<WithingsEnvelope<WithingsSleepSummaryBody>>(
            json, WithingsJson.Options);
        var body = envelope.Require("the test payload");

        var night = Assert.Single(WithingsSleepMapper.ToNights(body.Series!));
        Assert.Equal(7m, night.Hours);                 // read from a quoted number
        Assert.Equal(74, night.SleepScore);
        Assert.Equal(58, night.HeartRateAverage);
        Assert.Equal(12, night.BreathingDisturbances);
        Assert.Equal(3, night.SnoringEpisodes);
        Assert.Equal(RecordSource.Imported, night.Source);
        Assert.Equal("deep 2 · REM 1 · light 4", night.StagesDisplay);
    }

    [Fact]
    public void NonZeroStatusIsAnErrorEvenThoughTheHttpCallSucceeded()
    {
        const string json = """{"status": 401, "body": null, "error": "invalid_token"}""";

        var envelope = JsonSerializer.Deserialize<WithingsEnvelope<WithingsSleepSummaryBody>>(
            json, WithingsJson.Options);

        var error = Assert.Throws<WithingsApiException>(() => envelope.Require("the sleep request"));
        Assert.Equal(401, error.Status);
        Assert.Contains("invalid_token", error.Message);
    }
}
