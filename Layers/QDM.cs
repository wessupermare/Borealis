using Borealis.Data;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Borealis.Layers;
internal class QDM : ILayer
{
	public PointF CursorPosition { private get; set; }

	readonly Color _color;

	Coordinate? _fromPoint, _toPoint;
	Coordinate _lastPoint = new();

	public QDM(Color color) => _color = color;

	public void Draw(Transformer canvas)
	{
		_lastPoint = canvas.LocalToWorldPoint(CursorPosition);

		if (_fromPoint is Coordinate f)
		{
			canvas.StrokeColor = _color;
			canvas.FontColor = _color;

			Coordinate endpoint = _toPoint is Coordinate t ? t : _lastPoint;

			canvas.DrawLine(f.ToPoint(), endpoint.ToPoint());

			(float? bearing, float distance) = f.GetBearingDistance(endpoint);
			canvas.DrawString($"{(int)(bearing ?? 0):000}\n{distance:0.0}", endpoint.Longitude, endpoint.Latitude, HorizontalAlignment.Left);
		}
	}

	public void DoubleClick()
	{
		if (_fromPoint is null)
			_fromPoint = _lastPoint;
		else if (_toPoint is null)
			_toPoint = _lastPoint;
		else
		{
			_fromPoint = null;
			_toPoint = null;
		}
	}
}
