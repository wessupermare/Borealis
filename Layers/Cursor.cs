using Borealis.Data;

namespace Borealis.Layers;
internal class Cursor : ILayer
{
	public PointF Position { private get; set; }

	readonly Color _color;

	public Cursor(Color color) => _color = color;

	public void Draw(Transformer canvas)
	{
		float radius = 12;
		PointF offset = new(10, -25);

		canvas.FontColor = _color;
		canvas.StrokeColor = _color;

		canvas.DrawLine(canvas.LocalToWorldPoint(Position.Add(new(0, radius))), canvas.LocalToWorldPoint(Position.Add(new(radius, 0))));
		canvas.DrawLine(canvas.LocalToWorldPoint(Position.Add(new(0, radius))), canvas.LocalToWorldPoint(Position.Add(new(-radius, 0))));

		canvas.DrawLine(canvas.LocalToWorldPoint(Position.Add(new(0, -radius))), canvas.LocalToWorldPoint(Position.Add(new(radius, 0))));
		canvas.DrawLine(canvas.LocalToWorldPoint(Position.Add(new(0, -radius))), canvas.LocalToWorldPoint(Position.Add(new(-radius, 0))));

		Coordinate worldspace = canvas.LocalToWorldPoint(Position);
		PointF boxpoint = Position.Add(offset);
		RectF textBox = new(boxpoint.X, boxpoint.Y, 100, 20);
		canvas.DrawString($"({Math.Abs(worldspace.Latitude):0.00}{(worldspace.Latitude < 0 ? "S" : "N")}, {Math.Abs(worldspace.Longitude):0.00}{(worldspace.Longitude < 0 ? "W" : "E")})", canvas.LocalToWorldArea(textBox), HorizontalAlignment.Left, VerticalAlignment.Center);
	}
}
