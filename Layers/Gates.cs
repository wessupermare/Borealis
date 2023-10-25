using Borealis.Data;

using OsmSharp.Complete;
using OsmSharp.Streams.Complete;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Gates : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_gates.Values.Where(r => pattern.IsMatch("Gate " + r.Name)).Select(r => ("Gate " + r.Name, r.Average()));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;
	readonly ConcurrentDictionary<ulong, Route> _gates = new();

	public Gates(Colorscheme color, ICompleteOsmGeo[] source)
	{
		_color = color;
		LoadData(source);
	}

	void LoadData(ICompleteOsmGeo[] source)
	{
		var gates =
			source
				.AsParallel().AsUnordered()
				.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "parking_position").Cast<CompleteWay>()
				.Select(n =>
					new KeyValuePair<ulong, Route>(
						(ulong)n.Id,
						new(n.GetTag("ref") ?? "", n.Nodes.ToCoordinates().ToArray())
					)
				);

		foreach (var kvp in gates)
			_gates.TryAdd(kvp.Key, kvp.Value);

		OnInvalidating?.Invoke();
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.StrokeSize = 1;
		canvas.StrokeColor = _color.Gate;
		canvas.FontColor = _color.Gate;

		foreach (var taxiway in _gates.Values)
			canvas.DrawPath(taxiway, null, 0.00005f, 0, 0, null, 0.00001f);
	}
}
