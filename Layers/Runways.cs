using Borealis.Data;

using OsmSharp.Complete;
using OsmSharp.Streams.Complete;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class Runways : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_runwayLabels.Select(kvp => ($"Runway {kvp.Value.Label} @ {kvp.Value.Airport}", kvp.Key)).Where(v => pattern.IsMatch(v.Item1));

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Left | ILayer.ClickType.Single) || _scope.LastTransform is not Transformer t)
			return false;

		if (_runwayLabels.Select(kvp => (kvp.Value, point.Distance(t.WorldToLocalPoint(kvp.Key))))
				.Where(i => i.Item2 < 20)
				.OrderBy(i => i.Item2)
				.Select(i => i.Value)
				.FirstOrDefault() is not (string label, string airport))
			return false;

		if (_selected.TryGetValue(airport, out ImmutableHashSet<string>? selectedRwys))
			if (selectedRwys.Contains(label))
				_selected[airport] = selectedRwys.Remove(label);
			else
				_selected[airport] = selectedRwys.Add(label);
		else
			_selected.TryAdd(airport, new[] { label }.ToImmutableHashSet());

		OnInvalidating?.Invoke();

		return true;
	}

	public IEnumerable<(string Airport, string Runway)> Selected => _selected.SelectMany(kvp => kvp.Value.Select(v => (kvp.Key, v)));

	readonly Colorscheme _color, _labelColor;
	readonly Scope _scope;
	readonly ConcurrentDictionary<ulong, Route> _runways = new();
	readonly ConcurrentDictionary<Coordinate, (string Label, string Airport)> _runwayLabels = new();
	readonly ConcurrentDictionary<Coordinate, Coordinate> _runwayOpposites = new();
	readonly ConcurrentDictionary<string, ImmutableHashSet<string>> _selected = new();

	public Runways(Colorscheme strokeColor, Colorscheme labelColor, Airports airports, CIFP cifp, Scope scope, ICompleteOsmGeo[] source)
	{
		(_color, _labelColor, _scope) = (strokeColor, labelColor, scope);

		async Task LoadAsync(Airports ap)
		{
			var cD = LoadCifpDataAsync(ap, cifp);

			LoadOsmData(source);

			await cD; OnInvalidating?.Invoke();
		};

		if (airports.Known.Any())
			_ = LoadAsync(airports);
		else
			airports.OnAirportsChanged += async ap => await LoadAsync(ap);
	}

	void LoadOsmData(ICompleteOsmGeo[] source)
	{
		var runways =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "runway" && w.GetTag("abandoned") != "yes").Cast<CompleteWay>()
				.Select(n =>
					new KeyValuePair<ulong, Route>(
						(ulong)n.Id,
						new("", n.Nodes.ToCoordinates().ToArray())
					)
				);

		foreach (var kvp in runways)
			_runways.TryAdd(kvp.Key, kvp.Value);
	}

	Task LoadCifpDataAsync(Airports ap, CIFP cifp) =>
		Task.Run(() =>
			ap.Known.AsParallel().AsUnordered().ForAll(icao =>
			{
				if (cifp.Runways.TryGetValue(icao, out var runways) && runways is not null)
				{
					HashSet<string> idsToProcess = new(runways.Select(rw => rw.Identifier));

					foreach (var rw in runways)
					{
						var ep = (CIFPReader.Coordinate)rw.Endpoint;
						_runwayLabels.TryAdd(ep, (rw.Identifier, icao));

						if (!idsToProcess.Contains(rw.Identifier))
							continue;

						idsToProcess.Remove(rw.Identifier);

						if (!idsToProcess.Contains(rw.OppositeIdentifier))
							return;

						idsToProcess.Remove(rw.OppositeIdentifier);
						_runwayOpposites.TryAdd(ep, (CIFPReader.Coordinate)runways.Single(rw2 => rw2.Identifier == rw.OppositeIdentifier).Endpoint);
					}
				}
			})
		);

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.StrokeSize = canvas.WorldScale switch {
			< 0.00003f => 15,
			< 0.00006f => 10,
			< 0.0001f => 5,
			_ => 2
		};
		canvas.StrokeColor = _color.Runway;
		canvas.FontColor = _labelColor.Runway;

		foreach (var runway in _runways.Values)
			canvas.DrawPath(runway, null, 0.002f, 0, 0, null, 0.0001f);

		if (canvas.WorldScale >= 0.0001f)
			return;

		canvas.StrokeColor = new(0xFF, 0xFF, 0xFF, 0x88);
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = new float[] { 5, 2 };

		foreach (var rwPair in _runwayOpposites.Where(op => canvas.IsOnScreen(op.Key) || canvas.IsOnScreen(op.Value)))
			canvas.DrawLine(rwPair.Key, rwPair.Value);

		foreach (var label in _runwayLabels.Where(lbl => canvas.IsOnScreen(lbl.Key)))
			canvas.DrawString(label.Value.Label, label.Key, HorizontalAlignment.Center);
	}
}
