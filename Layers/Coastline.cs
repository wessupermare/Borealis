using Borealis.Data;

using OsmSharp.Complete;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Coastline : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) => Array.Empty<(string Name, Coordinate Centerpoint)>();

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;
	readonly ConcurrentBag<Route> _coastlines = new();

	public Coastline(Colorscheme color, ICompleteOsmGeo[] source)
	{
		_color = color;
		LoadData(source);
	}

	void LoadData(ICompleteOsmGeo[] source)
	{
		var coastlines =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteWay w && w.GetTag("natural") == "coastline").Cast<CompleteWay>()
				.Select(w => new Route(w.GetTag("ref") ?? "", w.Nodes.ToCoordinates().ToArray())).ToImmutableArray();

		Dictionary<Coordinate, Route> startpoints = new();
		Dictionary<Coordinate, Route> endpoints = new();

		foreach (var clWay in coastlines)
		{
			Coordinate startPoint = clWay.Points.First(),
						 endPoint = clWay.Points.Last();

			Route newRoute = clWay;
			while (endpoints.TryGetValue(startPoint, out Route? r))
			{
				newRoute = r + newRoute;
				endpoints.Remove(startPoint);
				startPoint = newRoute.Points.First();
			}

			endpoints[endPoint] = newRoute;

			while (startpoints.TryGetValue(endPoint, out Route? r))
			{
				newRoute += r;
				startpoints.Remove(endPoint);
				endPoint = newRoute.Points.Last();
			}

			startpoints[startPoint] = newRoute;
		}

		foreach (Route r in startpoints.Count <= endpoints.Count ? startpoints.Values : endpoints.Values)
			_coastlines.Add(r);//.Simplified(0.01));

		OnInvalidating?.Invoke();
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.StrokeSize = 1;
		canvas.StrokeColor = _color.Coastline;
		canvas.FontColor = _color.Coastline;

		foreach (var coastline in _coastlines)
			canvas.DrawPath(coastline);
	}
}
