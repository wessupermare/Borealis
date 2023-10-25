using Borealis.Data;

using Microsoft.UI.Xaml.Input;

using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class RangeRings : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) => Array.Empty<(string Name, Coordinate Centerpoint)>();

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;

	public RangeRings(Colorscheme color) => _color = color;

	public KeyboardAccelerator GetAccelerator()
	{
		KeyboardAccelerator retval = new() { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.R };
		retval.Invoked += (_, _) => { Active = !Active; OnInvalidating?.Invoke(); };

		return retval;
	}

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		canvas.FillColor = _color.Background;
		canvas.StrokeSize = 1;
		canvas.StrokeColor = _color.RangeRings;
		canvas.FontColor = _color.RangeRings;

		float vOffset = canvas.HeightToDistance(10f),
			  hOffset = canvas.WidthToDistance(15f);

		Coordinate centerPoint = canvas.Center;
		int tspacing = (int)((canvas.Center + new Coordinate(0, canvas.Width)).DistanceTo(canvas.Center) / 10);

		int spacing = tspacing switch {
			< 3 => 5,
			< 10 => 10,
			< 50 => 25,
			< 200 => 100,
			< 500 => 500,
			_ => 1000
		};

		float increment = centerPoint.FixRadialDistance(90, spacing).Longitude - centerPoint.Longitude;

		PointF localCtr = canvas.WorldToLocalPoint(centerPoint);
		originalCanvas.DrawLine(localCtr.Add(new(0, 5)), localCtr.Add(new(0, -5)));
		originalCanvas.DrawLine(localCtr.Add(new(5, 0)), localCtr.Add(new(-5, 0)));

		for (float ringNum = 1; ringNum * increment < canvas.Width; ringNum++)
			originalCanvas.DrawCircle(localCtr, canvas.DistanceToWidth(ringNum * increment));

		// Draw text after boxes for correct overlap area.
		for (float ringNum = 1; ringNum * increment < canvas.Width; ringNum++)
		{
			string data = $"{ringNum * spacing:000}";
			SizeF worldArea = canvas.GetStringSize(data, HorizontalAlignment.Center, VerticalAlignment.Center);
			SizeF localArea = canvas.WorldToLocalSize(worldArea);
			localArea.Width *= 3;
			localArea.Height *= 1.5f;
			RectF boxArea = new(localCtr.Add(new(canvas.DistanceToWidth(ringNum * increment) - localArea.Width / 2, 0)), localArea);
			originalCanvas.FillRectangle(boxArea);
			originalCanvas.DrawString(data, boxArea, HorizontalAlignment.Center, VerticalAlignment.Center);
		}
	}
}
