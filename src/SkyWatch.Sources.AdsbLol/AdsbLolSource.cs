using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyWatch.Contracts;

namespace SkyWatch.Sources.AdsbLol;

/// <summary>
/// Polls adsb.lol watch zones plus the global emergency and military views. The
/// producer turns each snapshot into position events keyed by aircraft hex.
/// </summary>
public sealed class AdsbLolSource : IAircraftSource
{
    private readonly HttpClient _httpClient;
    private readonly AdsbLolOptions _options;
    private readonly ILogger<AdsbLolSource> _logger;
    private readonly TimeProvider _timeProvider;

    public AdsbLolSource(
        HttpClient httpClient,
        IOptions<AdsbLolOptions> options,
        ILogger<AdsbLolSource> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
    }

    public async IAsyncEnumerable<AircraftSnapshot> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            AircraftSnapshot? snapshot = null;

            try
            {
                snapshot = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "adsb.lol poll failed; keeping the source alive.");
            }

            if (snapshot is not null)
            {
                yield return snapshot;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds)), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>One poll across all configured zones, military and emergency views.</summary>
    public async Task<AircraftSnapshot> PollOnceAsync(CancellationToken cancellationToken)
    {
        var requests = new List<FetchRequest>();

        foreach (WatchZone zone in _options.Zones)
        {
            string path = string.Create(
                CultureInfo.InvariantCulture,
                $"/v2/point/{zone.Latitude:0.######}/{zone.Longitude:0.######}/{Math.Clamp(zone.RadiusNm, 0, 250)}");

            requests.Add(new FetchRequest(path, "adsb.lol/point", zone.Name));
        }

        if (_options.IncludeMilitary)
        {
            requests.Add(new FetchRequest("/v2/mil", "adsb.lol/mil", "global"));
        }

        if (_options.IncludeEmergencySquawks)
        {
            foreach (string squawk in _options.EmergencySquawks)
            {
                requests.Add(new FetchRequest($"/v2/sqk/{squawk}", $"adsb.lol/sqk:{squawk}", "global"));
            }
        }

        var results = new List<FetchResult>(requests.Count);

        foreach (FetchRequest request in requests)
        {
            results.Add(await FetchAsync(request.Path, request.Source, request.Zone, cancellationToken)
                .ConfigureAwait(false));

            if (_options.RequestSpacingMs > 0)
            {
                await Task.Delay(_options.RequestSpacingMs, cancellationToken).ConfigureAwait(false);
            }
        }

        long newest = results
            .Where(result => result.Now > 0)
            .Select(result => result.Now)
            .DefaultIfEmpty(0)
            .Max();

        DateTimeOffset snapshotAt = newest > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(newest)
            : _timeProvider.GetUtcNow();

        var byHex = new Dictionary<string, AircraftPosition>(StringComparer.OrdinalIgnoreCase);

        foreach (FetchResult result in results)
        {
            foreach (AdsbAircraft aircraft in result.Aircraft)
            {
                AircraftPosition? position = ToPosition(aircraft, result, snapshotAt);

                if (position is null)
                {
                    continue;
                }

                if (byHex.TryGetValue(position.Hex, out AircraftPosition? existing))
                {
                    byHex[position.Hex] = Merge(existing, position);
                }
                else
                {
                    byHex[position.Hex] = position;
                }
            }
        }

        AircraftPosition[] positions = byHex.Values
            .OrderBy(position => position.Hex, StringComparer.Ordinal)
            .ToArray();

        int total = positions.Length;

        if (_options.MaxPositionsPerSnapshot > 0 && total > _options.MaxPositionsPerSnapshot)
        {
            int step = (int)Math.Ceiling((double)total / _options.MaxPositionsPerSnapshot);
            positions = positions
                .Where((_, index) => index % step == 0)
                .Take(_options.MaxPositionsPerSnapshot)
                .ToArray();
        }

        _logger.LogInformation(
            "adsb.lol snapshot at {SnapshotAt}: {Count}/{Total} unique aircraft selected.",
            snapshotAt,
            positions.Length,
            total);

        return new AircraftSnapshot(snapshotAt, positions);
    }

    private async Task<FetchResult> FetchAsync(
        string path,
        string source,
        string zone,
        CancellationToken cancellationToken)
    {
        try
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(path, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1)
                {
                    TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    TimeSpan cap = TimeSpan.FromSeconds(Math.Max(1, _options.MaxRetryAfterSeconds));

                    if (delay > cap)
                    {
                        delay = cap;
                    }

                    _logger.LogWarning("adsb.lol {Source} rate-limited; retrying in {Delay}.", source, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("adsb.lol {Source} returned {StatusCode}.", source, response.StatusCode);
                    return new FetchResult(source, zone, 0, []);
                }

                AdsbResponse? payload = await response.Content
                    .ReadFromJsonAsync<AdsbResponse>(SkyWatchJson.Options, cancellationToken)
                    .ConfigureAwait(false);

                return new FetchResult(
                    source,
                    zone,
                    payload?.Now ?? 0,
                    payload?.Aircraft ?? []);
            }

            return new FetchResult(source, zone, 0, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "adsb.lol {Source} request failed.", source);
            return new FetchResult(source, zone, 0, []);
        }
    }

    private static AircraftPosition? ToPosition(
        AdsbAircraft aircraft,
        FetchResult result,
        DateTimeOffset snapshotAt)
    {
        string? hex = NullIfWhiteSpace(aircraft.Hex)?.ToLowerInvariant();

        if (hex is null)
        {
            return null;
        }

        (int? altitudeBaro, bool baroGround) = ParseAltitude(aircraft.AltBaro);
        (int? altitudeGeom, bool geomGround) = ParseAltitude(aircraft.AltGeom);
        int dbFlags = aircraft.DbFlags ?? 0;

        return new AircraftPosition
        {
            EventId = $"{hex}:{snapshotAt.ToUnixTimeMilliseconds()}",
            SnapshotAt = snapshotAt,
            Source = result.Source,
            Zone = result.Zone,
            Hex = hex,
            Callsign = NullIfWhiteSpace(aircraft.Flight),
            Registration = NullIfWhiteSpace(aircraft.Registration),
            AircraftType = NullIfWhiteSpace(aircraft.Type),
            Category = NullIfWhiteSpace(aircraft.Category),
            Latitude = aircraft.Latitude,
            Longitude = aircraft.Longitude,
            AltitudeBaroFt = altitudeBaro,
            AltitudeGeomFt = altitudeGeom,
            GroundSpeedKt = aircraft.GroundSpeed,
            TrackDeg = aircraft.Track,
            VerticalRateFpm = aircraft.GeomRate ?? aircraft.BaroRate,
            Squawk = NullIfWhiteSpace(aircraft.Squawk),
            Emergency = NullIfWhiteSpace(aircraft.Emergency) ?? "none",
            OnGround = baroGround || geomGround,
            SeenPositionSeconds = aircraft.SeenPosition,
            Military = (dbFlags & 1) != 0 || result.Source.EndsWith("/mil", StringComparison.Ordinal),
            PrivacyIcao = (dbFlags & 4) != 0,
            Ladd = (dbFlags & 8) != 0
        };
    }

    private static AircraftPosition Merge(AircraftPosition existing, AircraftPosition candidate)
    {
        bool candidateEmergency = candidate.IsEmergency;

        return existing with
        {
            Callsign = existing.Callsign ?? candidate.Callsign,
            Registration = existing.Registration ?? candidate.Registration,
            AircraftType = existing.AircraftType ?? candidate.AircraftType,
            Category = existing.Category ?? candidate.Category,
            Latitude = existing.Latitude ?? candidate.Latitude,
            Longitude = existing.Longitude ?? candidate.Longitude,
            AltitudeBaroFt = existing.AltitudeBaroFt ?? candidate.AltitudeBaroFt,
            AltitudeGeomFt = existing.AltitudeGeomFt ?? candidate.AltitudeGeomFt,
            GroundSpeedKt = existing.GroundSpeedKt ?? candidate.GroundSpeedKt,
            TrackDeg = existing.TrackDeg ?? candidate.TrackDeg,
            VerticalRateFpm = existing.VerticalRateFpm ?? candidate.VerticalRateFpm,
            Squawk = candidateEmergency ? candidate.Squawk : existing.Squawk ?? candidate.Squawk,
            Emergency = candidateEmergency ? candidate.Emergency : existing.Emergency ?? candidate.Emergency,
            OnGround = existing.OnGround || candidate.OnGround,
            SeenPositionSeconds = Minimum(existing.SeenPositionSeconds, candidate.SeenPositionSeconds),
            Military = existing.Military || candidate.Military,
            PrivacyIcao = existing.PrivacyIcao || candidate.PrivacyIcao,
            Ladd = existing.Ladd || candidate.Ladd,
            Zone = existing.Zone == "global" ? candidate.Zone : existing.Zone
        };
    }

    private static (int? Feet, bool Ground) ParseAltitude(JsonElement? element)
    {
        if (element is not { } value)
        {
            return (null, false);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int feet))
        {
            return (feet, false);
        }

        if (value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), "ground", StringComparison.OrdinalIgnoreCase))
        {
            return (0, true);
        }

        return (null, false);
    }

    private static double? Minimum(double? left, double? right) =>
        left.HasValue && right.HasValue
            ? Math.Min(left.Value, right.Value)
            : left ?? right;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FetchRequest(string Path, string Source, string Zone);

    private sealed record FetchResult(
        string Source,
        string Zone,
        long Now,
        IReadOnlyList<AdsbAircraft> Aircraft);
}
