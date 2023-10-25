using Borealis.Data;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class Arrivals : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_arrivals.SelectMany(kvp => kvp.Value.Select(r => ($"{r.Name} @ {kvp.Key}", r.Average())).Where(r => pattern.IsMatch(r.Item1)));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color, _labelColor;
	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _arrivals = new();
	readonly Airports _airports;
	readonly Runways _runways;

	public Arrivals(Colorscheme color, Colorscheme labelColor, Airports airports, Runways runways, CIFP cifp)
	{
		(_color, _labelColor, _airports, _runways) = (color, labelColor, airports, runways);

		if (airports.Known.Any())
			_ = Task.Run(() => { LoadProcedures(airports, cifp); OnInvalidating?.Invoke(); });
		else
			airports.OnAirportsChanged += async ap => await Task.Run(() => { LoadProcedures(ap, cifp); OnInvalidating?.Invoke(); });
	}

	void LoadProcedures(Airports airports, CIFP cifp) =>
		airports.Known.AsParallel().AsUnordered()
			.Where(ap => cifp.Procedures.Any(pg => pg.Value.Any(p => p is CIFPReader.STAR a && a.Airport == ap)))
			.ForAll(ap =>
			{
				var stars = cifp.Procedures.SelectMany(pg => pg.Value.Where(p => p is CIFPReader.STAR a && a.Airport == ap)).Cast<CIFPReader.STAR>();

				List<Route> arrivals = new();
				static Route collapseProc(string name, IEnumerable<CIFPReader.Procedure.Instruction> proc)
				{
					var icoords =
						proc.Where(i => i.Endpoint is CIFPReader.ICoordinate).Select(i => i.Endpoint).Cast<CIFPReader.ICoordinate>()
						.Select(c => c is CIFPReader.NamedCoordinate nc ? ((Coordinate)nc.GetCoordinate(), nc.Name) : (c.GetCoordinate(), null));

					return new Route(name, icoords.ToArray());
				}

				foreach (var star in stars)
				{
					arrivals.Add(collapseProc(star.Name, star.SelectRoute(null, null)));

					foreach (var (transitionIn, transitionOut) in star.EnumerateTransitions())
						arrivals.Add(collapseProc($"{(transitionIn is null ? "" : transitionIn + ".")}{star.Name}{(transitionOut is null ? ".RWALL" : "." + transitionOut)}", star.SelectRoute(transitionIn, transitionOut)));
				}

				_arrivals.TryAdd(ap, arrivals.ToImmutableHashSet());
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float cutoff = 0.0001f;

		if (canvas.WorldScale < cutoff || !_arrivals.Any())
			return;

		canvas.StrokeColor = _color.Arrival;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = new float[] { 10, 15 };
		canvas.FontColor = _labelColor.Arrival;

		bool checkDisplay(string airport, Route v)
		{
			if (!v.Any(c => canvas.IsOnScreen(c.Point)))
				return false;

			string rawName = v.Name.Split('.')[^1][2..];
			if (rawName == "ALL" && _airports.Selected.Contains(airport))
				return true;

			string[] checkNames =
				rawName.EndsWith("ALL")
				? new[] { rawName[..^3] + "L", rawName[..^3] + "C", rawName[..^3] + "R" }
				: rawName.EndsWith("B")
				  ? new[] { rawName[..^1] + "L", rawName[..^1] + "R" }
				  : new[] { rawName };

			if (checkNames.Any(cn => _runways.Selected.Contains((airport, cn))))
				return true;

			if (_airports.Selected.Contains(airport) && !_runways.Selected.Any(kvp => kvp.Airport == airport))
				return true;

			return false;
		}

		foreach (var (ap, route) in _arrivals.SelectMany(i => i.Value.Where(v => checkDisplay(i.Key, v)).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), cutoff, null, cutoff, 0.002f, cutoff, 0.005f);
	}
}
