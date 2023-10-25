using Borealis.Data;

using OsmSharp.Complete;
using OsmSharp.Streams.Complete;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

using Coordinate = Borealis.Data.Coordinate;

namespace Borealis.Layers;
public class Aprons : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) => Array.Empty<(string, Coordinate)>();

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;
	readonly ConcurrentDictionary<ulong, Route> _aprons = new();

	public Aprons(Colorscheme color, ICompleteOsmGeo[] source)
	{
		_color = color;
		LoadData(source);
	}

	void LoadData(ICompleteOsmGeo[] source)
	{
		var aprons =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "apron").Cast<CompleteWay>()
				.Select(w =>
					new KeyValuePair<ulong, Route>(
						(ulong)w.Id,
						new(w.GetTag("ref") ?? "", w.Nodes.ToCoordinates().ToArray())
					)
				);

		foreach (var kvp in aprons)
			_aprons.TryAdd(kvp.Key, kvp.Value);

		OnInvalidating?.Invoke();
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.FontColor = _color.Apron;
		canvas.FillColor = _color.Apron;

		foreach (var apron in _aprons.Values)
			canvas.FillPath(apron, null, 0.0005f, 0, 0, null, 0.00005f);
	}
}
