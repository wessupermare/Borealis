using Borealis.Data;

using OsmSharp.Complete;
using OsmSharp.Streams.Complete;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

namespace Borealis.Layers;
public class Buildings : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) => Array.Empty<(string, Coordinate)>();

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;
	readonly ConcurrentDictionary<ulong, Route> _buildings = new();

	public Buildings(Colorscheme color, ICompleteOsmGeo[] source)
	{
		_color = color;
		LoadData(source);
	}

	void LoadData(ICompleteOsmGeo[] source)
	{
		ConcurrentDictionary<ulong, Route> ways = new();

		var buildings =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "terminal").Cast<CompleteWay>()
				.Select(w =>
					new KeyValuePair<ulong, Route>(
						(ulong)w.Id,
						new(w.GetTag("ref") ?? "", w.Nodes.ToCoordinates().ToArray())
					)
				);

		buildings = buildings.Concat(
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteRelation r && r.GetTag("aeroway") == "terminal").Cast<CompleteRelation>()
				.Select(r =>
					new KeyValuePair<ulong, Route>(
						(ulong)r.Id,
						new(r.GetTag("icao") ?? "", r.Members.Select(m => m.Member).Where(m => m is CompleteWay).Cast<CompleteWay>().SelectMany(w => w.Nodes).ToCoordinates().ToArray())
					)
				));

		foreach (var kvp in buildings)
			_buildings.TryAdd(kvp.Key, kvp.Value);

		OnInvalidating?.Invoke();
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.FontColor = _color.Building;
		canvas.FillColor = _color.Building;

		foreach (var building in _buildings.Values)
			canvas.FillPath(building, null, 0.0005f, 0, 0, null, 0.00005f);
	}
}
