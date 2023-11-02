using Borealis.Data;

using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Cursor(Colorscheme color) : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) => Array.Empty<(string Name, Coordinate Centerpoint)>();

	public bool Interact(PointF pos, Coordinate coord, ILayer.ClickType type)
	{
		if (!type.HasFlag(ILayer.ClickType.Hover))
			return false;

		Position = pos;
		Coordinate = coord;
		OnInvalidating?.Invoke();
		return false;
	}

	public PointF Position { get; set; }
	public Coordinate Coordinate { get; set; }

	readonly Colorscheme _color = color;

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		float radius = 12;
		PointF offset = new(10, -25);

		canvas.FontSize = 12;
		canvas.StrokeSize = 1;
		canvas.FontColor = _color.Cursor;
		canvas.StrokeColor = _color.Cursor;

		originalCanvas.DrawLine(Position.Add(new(0, radius)), Position.Add(new(radius, 0)));
		originalCanvas.DrawLine(Position.Add(new(0, radius)), Position.Add(new(-radius, 0)));

		originalCanvas.DrawLine(Position.Add(new(0, -radius)), Position.Add(new(radius, 0)));
		originalCanvas.DrawLine(Position.Add(new(0, -radius)), Position.Add(new(-radius, 0)));

		Coordinate worldspace = canvas.LocalToWorldPoint(Position);
		PointF boxpoint = Position.Add(offset);
		RectF textBox = new(boxpoint.X, boxpoint.Y, 100, 20);
		canvas.DrawString($"({Math.Abs(worldspace.Latitude):0.00}{(worldspace.Latitude < 0 ? "S" : "N")}, {Math.Abs(worldspace.Longitude):0.00}{(worldspace.Longitude < 0 ? "W" : "E")})", canvas.LocalToWorldArea(textBox), HorizontalAlignment.Left, VerticalAlignment.Center);
	}
}
