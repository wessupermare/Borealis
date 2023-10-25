using Borealis.Data;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class Departures : ILayer
{
	public bool Active { get; set; } = false;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_departures.SelectMany(kvp => kvp.Value.Select(r => ($"{r.Name} @ {kvp.Key}", r.Average())).Where(r => pattern.IsMatch(r.Item1)));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color, _labelColor;
	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _departures = new();
	readonly Airports _airports;

	public Departures(Colorscheme color, Colorscheme labelColor, Airports airports, CIFP cifp)
	{
		(_color, _labelColor, _airports) = (color, labelColor, airports);

		if (airports.Known.Any())
			_ = Task.Run(() => { LoadProcedures(airports, cifp); OnInvalidating?.Invoke(); });
		else
			airports.OnAirportsChanged += async ap => await Task.Run(() => { LoadProcedures(ap, cifp); OnInvalidating?.Invoke(); });
	}

	void LoadProcedures(Airports airports, CIFP cifp) =>
		airports.Known.AsParallel().AsUnordered()
			.Where(ap => cifp.Procedures.Any(pg => pg.Value.Any(p => p is CIFPReader.SID a && a.Airport == ap)))
			.ForAll(ap =>
			{
				var sids = cifp.Procedures.SelectMany(pg => pg.Value.Where(p => p is CIFPReader.SID a && a.Airport == ap)).Cast<CIFPReader.SID>();

				List<Route> departures = new();
				static Route collapseProc(string name, IEnumerable<CIFPReader.Procedure.Instruction> proc)
				{
					var icoords =
						proc.Where(i => i.Endpoint is CIFPReader.ICoordinate).Select(i => i.Endpoint).Cast<CIFPReader.ICoordinate>()
						.Select(c => c is CIFPReader.NamedCoordinate nc ? ((Coordinate)nc.GetCoordinate(), nc.Name) : (c.GetCoordinate(), null));

					return new Route(name, icoords.ToArray());
				}

				foreach (var sid in sids)
				{
					departures.Add(collapseProc(sid.Name, sid.SelectRoute(null, null)));

					foreach (var (transitionIn, transitionOut) in sid.EnumerateTransitions())
						departures.Add(collapseProc($"{(transitionIn is null ? "" : transitionIn + ".")}{sid.Name}{(transitionOut is null ? "" : "." + transitionOut)}", sid.SelectRoute(transitionIn, transitionOut)));
				}

				_departures.TryAdd(ap, departures.ToImmutableHashSet());
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float cutoff = 0.0001f;

		if (canvas.WorldScale < cutoff || !_departures.Any())
			return;

		canvas.StrokeColor = _color.Departure;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = new float[] { 10, 20 };
		canvas.FontColor = _labelColor.Departure;

		foreach (var (ap, route) in _departures.SelectMany(i => i.Value.Where(v => _airports.Selected.Contains(i.Key) && v.Any(c => canvas.IsOnScreen(c.Point))).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), cutoff, null, cutoff, 0.002f, cutoff, 0.005f);
	}
}
