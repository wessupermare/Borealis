using Borealis.Data;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class Departures : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_departures.SelectMany(kvp => kvp.Value.Select(r => ($"{r.Name} @ {kvp.Key}", r.Average())).Where(r => pattern.IsMatch(r.Item1)));

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
	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _departures = new();
	readonly Airports _airports;
	readonly Cursor _cursor;

	private ImmutableHashSet<string> _selectedAirports = new HashSet<string>().ToImmutableHashSet();
	private ImmutableDictionary<RectF, string> _switchBoxes = new Dictionary<RectF, string>().ToImmutableDictionary();

	public Departures(Colorscheme color, Colorscheme labelColor, Airports airports, CIFP cifp, Cursor cursor)
	{
		(_color, _labelColor, _airports, _cursor) = (color, labelColor, airports, cursor);

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

				List<Route> departures = [];
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

				_departures.TryAdd(ap, [.. departures]);
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float cutoff = 0.0001f;
		const float toggleCutoff = 0.0018f;

		if (canvas.WorldScale < cutoff || _departures.IsEmpty)
			return;

		canvas.StrokeColor = _color.Departure;
		canvas.StrokeSize = 1;
		canvas.StrokeDashPattern = [10, 20];
		canvas.FontColor = _labelColor.Departure;

		foreach (var (ap, route) in _departures.SelectMany(i => i.Value.Where(v => _selectedAirports.Contains(i.Key) && v.Any(c => canvas.IsOnScreen(c.Point))).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), cutoff, null, cutoff, 0.002f, cutoff, 0.005f);
		if (canvas.WorldScale > toggleCutoff)
			return;

		originalCanvas.ResetStroke();
		originalCanvas.FillColor = _color.Departure.WithAlpha(0.5f);
		originalCanvas.StrokeColor = _labelColor.Departure;
		originalCanvas.FontColor = _labelColor.Departure;
		originalCanvas.FontSize = 10;
		SizeF size = new(25, 15);
		PointF offset = new(-size.Width / 2 - size.Width - 5, 2 * size.Height / 3);

		Dictionary<RectF, string> apTargets = [];

		foreach ((string ap, PointF apPoint) in _airports.GetAllVisible().Where(kvp => kvp.Value.DistanceTo(_cursor.Coordinate) < 5).Select(kvp => (kvp.Key, canvas.WorldToLocalPoint(kvp.Value).Add(offset))))
		{
			RectF box = new(apPoint, size);
			apTargets.Add(box, ap);

			if (_selectedAirports.Contains(ap))
				originalCanvas.DrawRectangle(box);

			originalCanvas.DrawString("SID", box, HorizontalAlignment.Center, VerticalAlignment.Center);
		}

		_switchBoxes = apTargets.ToImmutableDictionary();
	}
}
