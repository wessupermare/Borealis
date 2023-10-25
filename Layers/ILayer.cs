using Borealis.Data;

using System.Text.RegularExpressions;

using static Borealis.Layers.ILayer;

namespace Borealis.Layers;
public interface ILayer
{
	private static readonly string[] AllEndpoints = new[] {
		"https://overpass-api.de/api/interpreter",
		"https://overpass.kumi.systems/api/interpreter",
		"https://maps.mail.ru/osm/tools/overpass/api/interpreter"
	};

	public static string OsmEndpoint => AllEndpoints[Random.Shared.Next(AllEndpoints.Length)];

	public bool Active { get; set; }

	public bool TopMost { get; }

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern);

	public bool Interact(PointF point, Coordinate position, ClickType type);

	public abstract void Draw(Transformer canvas, ICanvas originalCanvas);
	
	[Flags]
	public enum ClickType
	{
		Left	= 0b00_01,
		Right	= 0b00_10,

		Hover	= 0b00_00,
		Single	= 0b01_00,
		Double	= 0b10_00,
	}
}

public class Background : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) => Array.Empty<(string, Coordinate)>();

	public bool Interact(PointF _1, Coordinate _2, ClickType _3) => false;

	readonly Colorscheme _color;

	public Background(Colorscheme fillColor) => _color = fillColor;

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.FillColor = _color.Background;
		canvas.FillCanvas();

		canvas.StrokeColor = _color.RangeRings;
		canvas.StrokeDashPattern = new float[] { 30, 30 };
		Route negativeBoundary = new("World Boundary"),
			  positiveBoundary = new("World Boundary");

		for (int deg = 90; deg >= -90; --deg)
		{
			negativeBoundary.Add(new(deg, -180));
			positiveBoundary.Add(new(deg, 180));
		}

		Route topBoundary = new("World Boundary") {
			new(90, -180),
			new(90, 180)
		},
			  bottomBoundary = new("World Boundary") {
			new(-90, -180),
			new(-90, 180)
		};

		canvas.DrawPath(negativeBoundary);
		canvas.DrawPath(positiveBoundary);
		canvas.DrawPath(topBoundary);
		canvas.DrawPath(bottomBoundary);
	}
}

static partial class Extensions
{
	public static string? GetTag(this OsmSharp.Complete.ICompleteOsmGeo geo, string tag) => geo.Tags.TryGetValue(tag, out var tagValue) ? tagValue : null;
}