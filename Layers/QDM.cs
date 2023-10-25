using Borealis.Data;

using CIFPReader;

using Microsoft.Maui.Graphics.Win2D;

using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;
using Coordinate = Borealis.Data.Coordinate;

namespace Borealis.Layers;
public class QDM : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) => Array.Empty<(string Name, Coordinate Centerpoint)>();

	public bool Interact(PointF _, Coordinate lastPoint, ILayer.ClickType type)
	{
		if (type == ILayer.ClickType.Hover && _fromPoint is not null)
		{
			_lastPoint = lastPoint;
			return true;
		}

		if (type != (ILayer.ClickType.Left | ILayer.ClickType.Double))
			return false;

		_lastPoint = lastPoint;

		if (_fromPoint is null)
			_fromPoint = lastPoint;
		else if (_toPoint is null)
			_toPoint = lastPoint;
		else
		{
			_fromPoint = null;
			_toPoint = null;
		}

		OnInvalidating?.Invoke();
		return true;
	}

	readonly Colorscheme _color, _labelColor;
	readonly CIFP _cifp;

	Coordinate? _fromPoint, _toPoint;
	Coordinate _lastPoint = new();

	public QDM(Colorscheme color, Colorscheme labelColor, CIFP cifp) => (_color, _labelColor, _cifp) = (color, labelColor, cifp);

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		if (_fromPoint is Coordinate f)
		{
			canvas.StrokeSize = 1;
			canvas.StrokeColor = _color.QDM;
			canvas.FillColor = new(0);
			canvas.FontColor = _labelColor.QDM;

			Coordinate endpoint = _toPoint is Coordinate t ? t : _lastPoint;

			canvas.DrawLine(f.ToPoint(), endpoint.ToPoint());

			(float? bearingF, float distance) = f.GetBearingDistance(endpoint);
			int bearing;

			try
			{
				bearing = ((int)Math.Round((float)_cifp.Navaids.GetLocalMagneticVariation(f).Variation + (bearingF ?? 0)) + 360) % 360;
				bearing = bearing == 0 ? 360 : bearing;
			} catch { bearing = (int?)bearingF ?? 0; } // Magvar failure.


			string data = $"{bearing:000}\n{distance:0.0}";
			W2DCanvasState state = (W2DCanvasState)originalCanvas.GetType().GetProperty("CurrentState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(originalCanvas)!;
			SizeF textSize = canvas.GetStringSize("0", state.Font ?? Microsoft.Maui.Graphics.Font.Default, state.FontSize);
			RectF boundingBox = new(endpoint.Longitude, endpoint.Latitude, textSize.Width * Math.Max(4f, 3.5f + (int)Math.Log10(distance + .1)), textSize.Height * 3.5f);
			canvas.FillRectangle(boundingBox);
			canvas.DrawString(data, boundingBox, HorizontalAlignment.Left, VerticalAlignment.Center);
		}
	}
}
