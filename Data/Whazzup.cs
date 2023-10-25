using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Borealis.Data;
public class Whazzup
{
	const string WHAZZUP_URL = @"https://api.ivao.aero/v2/tracker/whazzup";

	readonly HttpClient _http;
	DateTime _lastRefresh = DateTime.MinValue;

	public event Action<Aircraft[]> OnCacheUpdated;

	public Whazzup(HttpClient http) => (_http, OnCacheUpdated) = (http, _ => { });

	private Aircraft[] _cache = Array.Empty<Aircraft>();
	public async Task<Aircraft[]> GetAircraftAsync(CancellationToken cancellationToken)
	{
		if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(15))
			return _cache;

		try
		{
			if (await _http.GetFromJsonAsync<JsonObject>(WHAZZUP_URL + $"?{DateTime.UtcNow.Second}{DateTime.UtcNow.Microsecond}", cancellationToken) is not JsonObject res)
				return _cache;

			if (res["clients"]?["pilots"] is not JsonArray pilots)
				return _cache;

			List<Aircraft> aircraft = new();

			foreach (JsonObject pilot in pilots.Where(n => n is JsonObject).Cast<JsonObject>())
			{
				if (pilot["callsign"]?.GetValue<string>() is not string callsign
				 || pilot["lastTrack"] is not JsonObject lastTrack
				 || pilot["flightPlan"] is not JsonObject flightPlan
				 || pilot["userId"]?.GetValue<uint>() is not uint vid
				 || pilot["rating"]?.GetValue<int>() is not int rating)
					continue;

				if (lastTrack["latitude"]?.GetValue<float>() is not float lat ||
					lastTrack["longitude"]?.GetValue<float>() is not float lon ||
					lastTrack["groundSpeed"]?.GetValue<uint>() is not uint spd ||
					lastTrack["altitude"]?.GetValue<int>() is not int alt ||
					lastTrack["heading"]?.GetValue<int>() is not int hdg ||
					flightPlan["flightRules"]?.GetValue<string>() is not string rules ||
					flightPlan["departureId"]?.GetValue<string>()?.ToUpperInvariant() is not string origin ||
					flightPlan["arrivalId"]?.GetValue<string>()?.ToUpperInvariant() is not string destination ||
					flightPlan["route"]?.GetValue<string>()?.ToUpperInvariant() is not string route ||
					flightPlan["level"]?.GetValue<string>()?.ToUpperInvariant() is not string filedAlt ||
					flightPlan["speed"]?.GetValue<string>()?.ToUpperInvariant() is not string filedSpd)
					continue;

				int filedAltFt = filedAlt[0] switch {
					'V' => -1,
					'F' or 'A' => int.Parse(filedAlt[1..]) * 100,
					'S' or 'M' => (int)(int.Parse(filedAlt[1..]) * 32.8f),
					_ => throw new NotImplementedException()
				};

				int filedSpdKts = filedSpd[0] switch {
					'N' => int.Parse(filedSpd[1..]),
					'K' => (int)(int.Parse(filedSpd[1..]) * 0.54f),
					'M' => (int)(int.Parse(filedSpd[1..]) * 6.44f),
					_ => throw new NotImplementedException()
				};

				aircraft.Add(new(rules, callsign, new(lat, lon), spd, alt, hdg, (origin, route, destination, filedAltFt, filedSpdKts), (vid, (byte)rating)));
			}

			(_lastRefresh, _cache) = (DateTime.UtcNow, aircraft.ToArray());
			OnCacheUpdated(_cache);
			return _cache;
		}
		catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException) // Whazzup timed out… again.
		{
			return _cache;
		}
	}

	readonly CancellationTokenSource _monitorKill = new();
	Task? _monitorTask;
	public bool IsMonitoring => _monitorTask is not null;

	public void BeginMonitoring()
	{
		if (_monitorTask is not null)
			throw new NotSupportedException("Cannot have multiple Whazzup monitors.");

		_monitorTask = Task.Run(async () =>
		{
			while (!_monitorKill.Token.IsCancellationRequested)
			{
				try
				{
					await GetAircraftAsync(_monitorKill.Token);
					await Task.Delay(TimeSpan.FromSeconds(20), _monitorKill.Token);
				}
				catch (TaskCanceledException) { }
			}
		});
	}

	public async Task EndMonitoringAsync()
	{
		if (_monitorTask is null)
			throw new NotSupportedException("Must begin monitoring before ending monitoring.");

        _monitorKill.Cancel();
		await _monitorTask;
	}

	public void EndMonitoring() => EndMonitoringAsync().Wait();
}

public record Aircraft(string FlightRules, string Callsign, Coordinate Position, uint GroundSpeed, int Altitude, int Heading, (string Origin, string Route, string Destination, int Altitude, int Speed) Route, (uint Vid, byte Rating) Pilot) { }