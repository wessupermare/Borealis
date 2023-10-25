using Borealis.Data;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class IAPs : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_approaches.SelectMany(kvp => kvp.Value.Select(r => ($"{r.Name} @ {kvp.Key}", r.Average())).Where(r => pattern.IsMatch(r.Item1)));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _approaches = new();
	readonly Colorscheme _lineScheme, _labelScheme;
	readonly Airports _airports;
	readonly Runways _runways;

	public IAPs(Colorscheme strokeColor, Colorscheme labelColor, Airports airports, Runways runways, CIFP cifp)
	{
		(_lineScheme, _labelScheme, _airports, _runways) = (strokeColor, labelColor, airports, runways);

		if (airports.Known.Any())
			_ = Task.Run(() => { LoadApproaches(airports, cifp); OnInvalidating?.Invoke(); });
		else
			airports.OnAirportsChanged += async ap => await Task.Run(() => { LoadApproaches(ap, cifp); OnInvalidating?.Invoke(); });
	}

	void LoadApproaches(Airports airports, CIFP cifp) =>
		airports.Known.AsParallel().AsUnordered()
			.Where(ap => cifp.Procedures.Any(pg => pg.Value.Any(p => p is CIFPReader.Approach a && a.Airport == ap)))
			.ForAll(ap =>
			{
				var iaps = cifp.Procedures.SelectMany(pg => pg.Value.Where(p => p is CIFPReader.Approach a && a.Airport == ap)).Cast<CIFPReader.Approach>();

				List<Route> apps = new();
				static Route collapseApp(string name, IEnumerable<CIFPReader.Procedure.Instruction> app)
				{
					var icoords =
						app.Where(i => i.Endpoint is CIFPReader.ICoordinate).Select(i => i.Endpoint).Cast<CIFPReader.ICoordinate>()
						.Select(c => c is CIFPReader.NamedCoordinate nc ? ((Coordinate)nc.GetCoordinate(), nc.Name) : (c.GetCoordinate(), null));

					return new Route(name, icoords.ToArray());
				}

				foreach (var app in iaps)
				{
					apps.Add(collapseApp(app.Name, app.SelectRoute(null, null)));

					foreach (var (transition, _) in app.EnumerateTransitions())
						apps.Add(collapseApp($"{transition}.{app.Name}", app.SelectRoute(transition, null)));
				}

				_approaches.TryAdd(ap, apps.ToImmutableHashSet());
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float cutoff = 0.0001f;

		if (canvas.WorldScale < cutoff || !_approaches.Any())
			return;

		canvas.StrokeColor = _lineScheme.Approach;
		canvas.StrokeSize = 1;
		canvas.FontColor = _labelScheme.Approach;

		foreach (var (ap, route) in _approaches.SelectMany(i => i.Value.Where(v => ((_airports.Selected.Contains(i.Key) && !_runways.Selected.Any(rw => rw.Airport == i.Key)) || _runways.Selected.Contains((i.Key, v.Name.Split('.')[^1][1..]))) && v.Any(c => canvas.IsOnScreen(c.Point))).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), cutoff, null, cutoff, 0.002f, cutoff, 0.005f);
	}
}
