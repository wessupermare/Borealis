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

	readonly ConcurrentDictionary<string, ImmutableHashSet<Route>> _approaches = new();
	readonly Colorscheme _color, _labelColor;
	readonly Airports _airports;
	readonly Runways _runways;
	readonly Cursor _cursor;

	private ImmutableHashSet<string> _selectedAirports = new HashSet<string>().ToImmutableHashSet();
	private ImmutableDictionary<RectF, string> _switchBoxes = new Dictionary<RectF, string>().ToImmutableDictionary();

	public IAPs(Colorscheme strokeColor, Colorscheme labelColor, Airports airports, Runways runways, CIFP cifp, Cursor cursor)
	{
		(_color, _labelColor, _airports, _runways, _cursor) = (strokeColor, labelColor, airports, runways, cursor);

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

				List<Route> apps = [];
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

				_approaches.TryAdd(ap, [..apps]);
			});

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const float cutoff = 0.0001f;
		const float toggleCutoff = 0.0018f;

		if (canvas.WorldScale < cutoff || _approaches.IsEmpty)
			return;

		canvas.StrokeColor = _color.Approach;
		canvas.StrokeSize = 1;
		canvas.FontColor = _labelColor.Approach;

		foreach (var (ap, route) in _approaches.SelectMany(i => i.Value.Where(v => ((_selectedAirports.Contains(i.Key) && !_runways.Selected.Any(rw => rw.Airport == i.Key)) || _runways.Selected.Contains((i.Key, v.Name.Split('.')[^1][1..]))) && v.Any(c => canvas.IsOnScreen(c.Point))).Select(v => (i.Key, v))))
			canvas.DrawPath(route.WithName(route.Name + $" ({ap})"), cutoff, null, cutoff, 0.002f, cutoff, 0.005f);

		if (canvas.WorldScale > toggleCutoff)
			return;

		originalCanvas.ResetStroke();
		originalCanvas.FillColor = _color.Approach.WithAlpha(0.5f);
		originalCanvas.StrokeColor = _labelColor.Approach;
		originalCanvas.FontColor = _labelColor.Approach;
		originalCanvas.FontSize = 10;
		SizeF size = new(25, 15);
		PointF offset = new(-size.Width / 2 + size.Width + 5, 2 * size.Height / 3);

		Dictionary<RectF, string> apTargets = [];

		foreach ((string ap, PointF apPoint) in _airports.GetAllVisible().Where(kvp => kvp.Value.DistanceTo(_cursor.Coordinate) < 5).Select(kvp => (kvp.Key, canvas.WorldToLocalPoint(kvp.Value).Add(offset))))
		{
			RectF box = new(apPoint, size);
			apTargets.Add(box, ap);

			if (_selectedAirports.Contains(ap))
				originalCanvas.DrawRectangle(box);

			originalCanvas.DrawString("IAP", box, HorizontalAlignment.Center, VerticalAlignment.Center);
		}

		_switchBoxes = apTargets.ToImmutableDictionary();
	}
}
