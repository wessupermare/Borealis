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

	public bool Interact(PointF target, Coordinate _, ILayer.ClickType type)
	{
		if (!type.HasFlag(ILayer.ClickType.Single | ILayer.ClickType.Left))
			return false;

		if (_switchBoxes.Keys.Select(k => (RectF?)k).FirstOrDefault(k => k?.Contains(target) ?? false) is not RectF hit)
			return false;

		string ap = _switchBoxes[hit];

#pragma warning disable CA1868 // Unnecessary call to 'Contains(item)'
		_selectedAirports = _selectedAirports.Contains(ap) ? _selectedAirports.Remove(ap) : _selectedAirports.Add(ap);
#pragma warning restore CA1868 // Unnecessary call to 'Contains(item)'

		OnInvalidating?.Invoke();
		return true;
	}

	readonly Colorscheme _color, _labelColor;
	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _arrivals = new();
	readonly Airports _airports;
	readonly Runways _runways;
	readonly Cursor _cursor;

	private ImmutableHashSet<string> _selectedAirports = new HashSet<string>().ToImmutableHashSet();
	private ImmutableDictionary<RectF, string> _switchBoxes = new Dictionary<RectF, string>().ToImmutableDictionary();

	public Arrivals(Colorscheme color, Colorscheme labelColor, Airports airports, Runways runways, CIFP cifp, Cursor cursor)
	{
		(_color, _labelColor, _airports, _runways, _cursor) = (color, labelColor, airports, runways, cursor);

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

				List<Route> arrivals = [];
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

				_arrivals.TryAdd(ap, [..arrivals]);
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float routeCutoff = 0.0001f;
		const float toggleCutoff = 0.0018f;

		if (canvas.WorldScale < routeCutoff || _arrivals.IsEmpty)
			return;

		canvas.StrokeColor = _color.Arrival;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [10, 15];
		canvas.FontColor = _labelColor.Arrival;

		bool checkDisplay(string airport, Route v)
		{
			if (!v.Any(c => canvas.IsOnScreen(c.Point)))
				return false;

			string rawName = v.Name.Split('.')[^1][2..];
			if (rawName == "ALL" && _selectedAirports.Contains(airport))
				return true;

			string[] checkNames =
				rawName.EndsWith("ALL")
				? new[] { rawName[..^3] + "L", rawName[..^3] + "C", rawName[..^3] + "R" }
				: rawName.EndsWith('B')
				  ? new[] { rawName[..^1] + "L", rawName[..^1] + "R" }
				  : new[] { rawName };

			if (checkNames.Any(cn => _runways.Selected.Contains((airport, cn))))
				return true;

			if (_selectedAirports.Contains(airport) && !_runways.Selected.Any(kvp => kvp.Airport == airport))
				return true;

			return false;
		}

		foreach (var (ap, route) in _arrivals.SelectMany(i => i.Value.Where(v => checkDisplay(i.Key, v)).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), routeCutoff, null, routeCutoff, 0.002f, routeCutoff, 0.005f);

		if (canvas.WorldScale > toggleCutoff)
			return;

		originalCanvas.ResetStroke();
		originalCanvas.FillColor = _color.Arrival.WithAlpha(0.5f);
		originalCanvas.StrokeColor = _labelColor.Arrival;
		originalCanvas.FontColor = _labelColor.Arrival;
		originalCanvas.FontSize = 10;
		SizeF size = new(25, 15);
		PointF offset = new(-size.Width / 2, 2 * size.Height / 3);

		Dictionary<RectF, string> apTargets = [];

		foreach ((string ap, PointF apPoint) in _airports.GetAllVisible().Where(kvp => kvp.Value.DistanceTo(_cursor.Coordinate) < 5).Select(kvp => (kvp.Key, canvas.WorldToLocalPoint(kvp.Value).Add(offset))))
		{
			RectF box = new(apPoint, size);
			apTargets.Add(box, ap);

			if (_selectedAirports.Contains(ap))
				originalCanvas.DrawRectangle(box);

			originalCanvas.DrawString("STAR", box, HorizontalAlignment.Center, VerticalAlignment.Center);
		}

		_switchBoxes = apTargets.ToImmutableDictionary();
	}
}
