using Borealis.Data;

using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class ColorEditor : ILayer
{
	public event Action<(Colorscheme Primary, Colorscheme Labels)>? ColorsChanged;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) { yield break; }
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	readonly Scope _scope;
	Colorscheme _primary, _labels;

	public ColorEditor(Scope scope, Colorscheme primary, Colorscheme labels) => (_scope, _primary, _labels) = (scope, primary, labels);

	int _loadedGeos = 0;
	public void IncrementLoadCount() => _loadedGeos++;

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		return false;
	}

	public void Draw(Transformer tr, ICanvas canvas)
	{
		tr.FillColor = new();
		tr.FillCanvas();

		float hp = _scope.LastBoundingRect?.Height / 100 ?? 10,
			  wp = _scope.LastBoundingRect?.Width / 100 ?? 20;

		canvas.FontSize = 20;
		canvas.FontColor = new(1f);

		void draw(string name, Func<Colorscheme, Color> selector, int column, int row)
		{
			float leftOffset = wp * column * 20,
				   topOffset = hp * (row + 2) * 5;

			canvas.DrawString(name, leftOffset + 5, topOffset, wp * 10, hp * 5, HorizontalAlignment.Right, VerticalAlignment.Top);

			canvas.StrokeColor = selector(_primary);
			canvas.StrokeSize = 2;

			canvas.FillColor = selector(_primary);
			canvas.FillRectangle(leftOffset + wp * 12, topOffset, wp, hp * 5.1f);
			canvas.FillColor = selector(_labels);

			if (selector(_labels).Alpha < 0.9f)
				canvas.DrawRectangle(leftOffset + wp * 14, topOffset, wp, hp * 5.1f);

			canvas.FillRectangle(leftOffset + wp * 14, topOffset, wp, hp * 5.1f);
		}

		draw("Aircraft", c => c.Aircraft, 1, 0);

		draw("Route", c => c.Route, 1, 2);
		draw("Departure", c => c.Departure, 1, 3);
		draw("Arrival", c => c.Arrival, 1, 4);
		draw("Approach", c => c.Approach, 1, 5);

		draw("Class B", c => c.ClassB, 1, 7);
		draw("Class C", c => c.ClassC, 1, 8);
		draw("Class D", c => c.ClassD, 1, 9);

		draw("Cursor", c => c.Cursor, 1, 11);
		draw("QDM", c => c.QDM, 1, 12);
		draw("Range Rings", c => c.RangeRings, 1, 13);
		draw("Background", c => c.Background, 1, 14);

		draw("Airport", c => c.Airport, 3, 0);

		draw("Runway", c => c.Runway, 3, 2);
		draw("Taxiway", c => c.Taxiway, 3, 3);
		draw("Taxilane", c => c.Taxilane, 3, 4);
		draw("Gate", c => c.Gate, 3, 5);
		draw("Apron", c => c.Apron, 3, 6);
		draw("Building", c => c.Building, 3, 7);

		draw("Fir Boundary", c => c.FirBoundary, 3, 9);
		draw("Coastline", c => c.Coastline, 3, 10);

		canvas.DrawString($"{_loadedGeos} loaded…", wp * 65, hp * 80, wp * 10, hp * 5, HorizontalAlignment.Right, VerticalAlignment.Top);
	}
}
