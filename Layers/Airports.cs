using Borealis.Data;

using OsmSharp;
using OsmSharp.Complete;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Airports : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<string> Selected => _selected.Where(kvp => kvp.Value).Select(kvp => kvp.Key);
	public IEnumerable<string> Known => _airports.Keys;
	public Coordinate Centerpoint => _airports.Values.Average();

	public event Action<Airports>? OnAirportsChanged;

	readonly Colorscheme _color;
	readonly Scope _scope;
	readonly ConcurrentDictionary<string, Coordinate> _airports = new();
	readonly ConcurrentDictionary<string, bool> _selected = new();

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_airports.Where(kvp => pattern.IsMatch(kvp.Key)).Select(kvp => (kvp.Key, kvp.Value));

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Left | ILayer.ClickType.Single) || (_scope.LastTransform?.WorldScale ?? 0) < 0.0005f || _scope.LastTransform is not Transformer t)
			return false;

		if (CheckSelected(point) is not string selected)
			return false;

		if (_selected.TryGetValue(selected, out bool e))
			_selected[selected] = !e;
		else
			_selected.TryAdd(selected, true);

		OnInvalidating?.Invoke();

		return true;
	}

	public string? CheckSelected(PointF point)
	{
		if ((_scope.LastTransform?.WorldScale ?? 0) < 0.0005f || _scope.LastTransform is not Transformer t)
			return null;


		return Known.Select(ap => (ap, point.Distance(t.WorldToLocalPoint(_airports[ap]))))
				.Where(i => i.Item2 < 20)
				.OrderBy(i => i.Item2)
				.Select(i => i.ap)
				.FirstOrDefault();
	}

	public ImmutableDictionary<string, Coordinate> GetAllVisible()
	{
		if ((_scope.LastTransform?.WorldScale ?? 0) < 0.0005f || _scope.LastTransform is not Transformer t)
			return new Dictionary<string, Coordinate>().ToImmutableDictionary();

		return Known.Where(ap => t.IsOnScreen(_airports[ap])).ToImmutableDictionary(ap => ap, ap => _airports[ap]);
	}

	public Airports(Colorscheme color, Scope scope, ICompleteOsmGeo[] source)
	{
		(_color, _scope) = (color, scope);
		_ = LoadDataAsync(scope.Teleport, source);
	}

	async Task LoadDataAsync(Action<Coordinate> setCenter, ICompleteOsmGeo[] source)
	{
		var airports =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is Node n && n.GetTag("aeroway") == "aerodrome" && (n.Tags.ContainsKey("icao") || n.Tags.ContainsKey("faa") || n.Tags.ContainsKey("iata")))
				.Cast<Node>()
				.Select(n =>
				{
					var label = n.GetTag("icao") ?? n.GetTag("faa") ?? n.Tags["iata"];
					return new KeyValuePair<string, Coordinate>(label, (Coordinate)n);
				});

		airports = airports.Concat(
			source
					.AsParallel().AsUnordered()
					.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "aerodrome" && (w.Tags.ContainsKey("icao") || w.Tags.ContainsKey("faa") || w.Tags.ContainsKey("iata")))
					.Cast<CompleteWay>()
					.Select(w =>
					{
						var label = w.GetTag("icao") ?? w.GetTag("faa") ?? w.Tags["iata"];
						var cp = w.Nodes.ToCoordinates().Average();
						return new KeyValuePair<string, Coordinate>(label, cp);
					}
					));

		airports = airports.Concat(
			source
					.AsParallel().AsUnordered()
					.Where(i => i is CompleteRelation r && r.GetTag("aeroway") == "aerodrome" && (r.Tags.ContainsKey("icao") || r.Tags.ContainsKey("faa") || r.Tags.ContainsKey("iata")))
					.Cast<CompleteRelation>()
					.Select(r =>
					{
						var label = r.GetTag("icao") ?? r.GetTag("faa") ?? r.Tags["iata"];
						return new KeyValuePair<string, Coordinate>(
							label,
							r.Members
								.Select(n => n.Member)
								.Where(n => n is CompleteWay).Cast<CompleteWay>()
								.SelectMany(w => w.Nodes.ToCoordinates())
								.Average()
						);
					}
					));

		Dictionary<string, Coordinate> data = new(airports.DistinctBy(kvp => kvp.Key));

		foreach (var ap in data)
			_airports.TryAdd(ap.Key, ap.Value);

		setCenter(_airports.Values.Average());
		await Task.Run(() => { OnAirportsChanged?.Invoke(this); OnInvalidating?.Invoke(); });
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		if (canvas.WorldScale < 0.0005f)
			return;

		var visibleAirports = _airports.Where(ap => canvas.IsOnScreen(ap.Value)).ToArray();

		if (!visibleAirports.Any())
			return;

		canvas.FontSize = canvas.WorldScale switch {
			< 0.005f => 12,
			< 0.01f => 8,
			_ => 5f
		};
		canvas.FillColor = _color.Airport;
		canvas.FontColor = _color.Airport;

		foreach (var airport in visibleAirports)
		{
			canvas.FillCircle(airport.Value, canvas.WidthToDistance(2));
			canvas.DrawString(airport.Key, airport.Value + new Coordinate(canvas.HeightToDistance(4), 0), HorizontalAlignment.Center);
		}
	}
}
