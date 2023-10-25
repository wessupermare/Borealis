using Borealis.Data;

using OsmSharp.Complete;
using OsmSharp.Streams.Complete;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

using Coordinate = Borealis.Data.Coordinate;

namespace Borealis.Layers;
public class Taxiways : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_taxiways.Where(v => pattern.IsMatch(v.Name)).Select(v => (v.Name, v.Average()));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color, _labelColor;
	readonly Route[] _borders;
	readonly ConcurrentBag<Route> _taxiways = new();
	readonly ConcurrentBag<Route> _taxilanes = new();

	public Taxiways(Colorscheme color, Colorscheme labelColor, Route[] borders, ICompleteOsmGeo[] source)
	{
		(_color, _labelColor, _borders) = (color, labelColor, borders);
		LoadData(source);
	}

	void LoadData(ICompleteOsmGeo[] source)
	{
		foreach (Route border in _borders)
		{
#if DEBUG
			string cacheDir = Path.Combine(FileSystem.CacheDirectory, "taxi");
			string wayCacheFile = Path.Combine(cacheDir, $"{border.Name}.twy");
			string laneCacheFile = Path.Combine(cacheDir, $"{border.Name}.tln");

			if (!Directory.Exists(cacheDir))
				Directory.CreateDirectory(cacheDir);

			if (File.Exists(wayCacheFile) && File.Exists(laneCacheFile) &&
				JsonSerializer.Deserialize<Route[]>(File.ReadAllText(wayCacheFile)) is Route[] ways &&
				JsonSerializer.Deserialize<Route[]>(File.ReadAllText(laneCacheFile)) is Route[] lanes)
			{
				foreach (var way in ways)
					_taxiways.Add(way);

				foreach (var lane in lanes)
					_taxilanes.Add(lane);

				return;
			}
#endif

			var taxiways =
				source
					.AsParallel().AsUnordered()
					.Where(i => i is CompleteWay && i.GetTag("aeroway") == "taxiway").Cast<CompleteWay>()
					.Select(n =>
							new Route(n.Tags.TryGetValue("ref", out string r) ? r : "", n.Nodes.ToCoordinates().ToArray())
					).ToArray();

			var taxilanes =
				source
					.AsParallel().AsUnordered()
					.Where(i => i is CompleteWay w && w.GetTag("aeroway") == "taxilane").Cast<CompleteWay>()
					.Select(w =>
							new Route(w.GetTag("ref") ?? "", w.Nodes.ToCoordinates().ToArray())
					).ToArray();

			foreach (var kvp in taxiways)
				_taxiways.Add(kvp);

			foreach (var kvp in taxilanes)
				_taxilanes.Add(kvp);
#if DEBUG
			if (taxiways.Any() && taxilanes.Any())
			{
				File.WriteAllText(wayCacheFile, JsonSerializer.Serialize(taxiways));
				File.WriteAllText(laneCacheFile, JsonSerializer.Serialize(taxilanes));
			}
#endif
		}

		OnInvalidating?.Invoke();
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.StrokeSize = canvas.WorldScale switch {
			< 0.0001f => 2,
			_ => 1
		};
		canvas.StrokeColor = _color.Taxiway;
		canvas.FontColor = _labelColor.Taxiway;

		foreach (var taxilane in _taxilanes)
			canvas.DrawPath(taxilane, null, 0.00005f, 0, 0, null, 0.00005f);

		canvas.StrokeColor = _color.Taxilane;
		canvas.FontColor = _labelColor.Taxilane;

		foreach (var taxiway in _taxiways)
			canvas.DrawPath(taxiway, null, 0.0005f, 0, 0, null, 0.00005f);
	}
}
