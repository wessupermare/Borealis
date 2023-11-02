using Borealis.Data;

using Microsoft.Maui.Graphics.Text;
using Microsoft.Maui.Graphics.Win2D;
using Microsoft.UI.Input;

using System.Net;
using System.Numerics;
using System.Reflection;

using static Borealis.Data.Route;

namespace Borealis.Layers;

internal static partial class Extensions
{
	public static Color Lighten(this Color color, int increase) =>
		new((int)(color.Red * 0xFF + increase), (int)(color.Green * 0xFF + increase), (int)(color.Blue * 0xFF + increase));

	public static PointF Add(this PointF a, PointF b) =>
		new(a.X + b.X, a.Y + b.Y);
	public static PointF Subtract(this PointF a, PointF b) =>
		new(a.X - b.X, a.Y - b.Y);

	public static void ChangeCursor(this Microsoft.UI.Xaml.UIElement uiElement, InputCursor cursor)
	{
		Type type = typeof(Microsoft.UI.Xaml.UIElement);
		type.InvokeMember("ProtectedCursor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty | BindingFlags.Instance, null, uiElement, new object[] { cursor });
	}
}

public class Transformer : ICanvas
{
	readonly ICanvas _canvas;
	readonly RectF _dirtyRect;

	public float WorldScale { get; private set; }

	public readonly Func<Coordinate, bool> IsOnScreen;

	public readonly Func<float, float> HeightToDistance;
	public readonly Func<float, float> WidthToDistance;

	public readonly Func<float, float> DistanceToHeight;
	public readonly Func<float, float> DistanceToWidth;

	readonly Func<Coordinate, float> SquishLon;
	readonly Func<Coordinate, float> UnsquishLon;

	readonly Func<PointF, PointF> RotateForward;
	readonly Func<PointF, PointF> Unrotate;
	const double DEG_TO_RAD = Math.PI / 180.0;

	public Transformer(ICanvas canvas, RectF dirtyRect, Coordinate worldCenter, float scale, float rotation)
	{
		_canvas = canvas;
		_dirtyRect = dirtyRect;
		WorldScale = scale;
		double rotationRad = rotation * DEG_TO_RAD;

		SquishLon = pos => (float)(pos.Longitude * Math.Cos(DEG_TO_RAD * (pos.Latitude - worldCenter.Latitude)));
		UnsquishLon = pos => (float)(pos.Longitude / Math.Cos(DEG_TO_RAD * (pos.Latitude - worldCenter.Latitude)));

		var (sinRotF, cosRotF) = Math.SinCos(rotationRad);
		var (sinRotR, cosRotR) = Math.SinCos(-rotationRad);

		RotateForward = p => new((float)(p.X * cosRotF - p.Y * sinRotF), (float)(p.X * sinRotF + p.Y * cosRotF));
		Unrotate = p => new((float)(p.X * cosRotR - p.Y * sinRotR), (float)(p.X * sinRotR + p.Y * cosRotR));

		LocalToWorldPoint = local =>
		{
			PointF recentered = Unrotate(local.Subtract(dirtyRect.Center));

			float latitude = recentered.Y * -scale;
			Coordinate adj = new Coordinate(latitude, recentered.X * scale) + worldCenter;

			return new(adj.Latitude, UnsquishLon(adj));
		};

		WorldToLocalPoint = world =>
		{
			Coordinate recentered = world - worldCenter;
			PointF preAdj = new(SquishLon(new(world.Latitude, recentered.Longitude / scale)), recentered.Latitude / -scale);
			PointF adj = RotateForward(preAdj);

			return adj.Add(dirtyRect.Center);
		};

		IsOnScreen = world => dirtyRect.Contains(WorldToLocalPoint(world));

		HeightToDistance = height => height * scale;
		WidthToDistance = width => width * scale;

		DistanceToHeight = dist => dist / scale;
		DistanceToWidth = dist => dist / scale;
	}

	public Func<PointF, Coordinate> LocalToWorldPoint;
	public Func<Coordinate, PointF> WorldToLocalPoint;

	public SizeF LocalToWorldSize(SizeF local) =>
		new(WidthToDistance(local.Width), HeightToDistance(local.Height));

	public RectF LocalToWorldArea(RectF local) =>
		new(LocalToWorldPoint(local.Location).ToPoint(), LocalToWorldSize(local.Size));

	public SizeF WorldToLocalSize(SizeF world) =>
		new(DistanceToWidth(world.Width), DistanceToHeight(world.Height));

	public RectF WorldToLocalArea(RectF world) =>
		new(WorldToLocalPoint(world.Location.ToCoordinate()), WorldToLocalSize(world.Size));

	public float DisplayScale { get => _canvas.DisplayScale; set => _canvas.DisplayScale = value; }
	public float StrokeSize { set => _canvas.StrokeSize = value; }
	public float MiterLimit { set => _canvas.MiterLimit = value; }
	public Color StrokeColor { set => _canvas.StrokeColor = value; }
	public LineCap StrokeLineCap { set => _canvas.StrokeLineCap = value; }
	public LineJoin StrokeLineJoin { set => _canvas.StrokeLineJoin = value; }
	public float[] StrokeDashPattern { set => _canvas.StrokeDashPattern = value; }
	public float StrokeDashOffset { set => _canvas.StrokeDashOffset = value; }
	public Color FillColor { set => _canvas.FillColor = value; }
	public Color FontColor { set => _canvas.FontColor = value; }
	public IFont Font { set => _canvas.Font = value; }
	public float FontSize { set => _canvas.FontSize = value; }
	public float Alpha { set => _canvas.Alpha = value; }
	public bool Antialias { set => _canvas.Antialias = value; }
	public BlendMode BlendMode { set => _canvas.BlendMode = value; }
	public float Width => WidthToDistance(_dirtyRect.Width);
	public float Height => HeightToDistance(_dirtyRect.Height);
	public Coordinate Center => LocalToWorldPoint(_dirtyRect.Center);

	public void FillCanvas() =>
		_canvas.FillRectangle(_dirtyRect);

	public void DrawPath(PathF path) => throw new NotSupportedException();
	public void DrawPath(Route path) => DrawPath(path, null, null);
	public void DrawPath(Route path, float? minScale, float? maxScale) => DrawPath(path, minScale, maxScale, minScale, maxScale);
	public void DrawPath(Route path, float? minRouteScale, float? maxRouteScale, float? minLabelScale, float? maxLabelScale) =>
			DrawPath(path, minRouteScale, maxRouteScale, minLabelScale, maxLabelScale, minLabelScale, maxLabelScale);
	public void DrawPath(Route path, float? minRouteScale, float? maxRouteScale, float? minPointLabelScale, float? maxPointLabelScale, float? minNameLabelScale, float? maxNameLabelScale)
	{
		if (!path.Any(p => IsOnScreen(p.Point)))
			return;

		if ((minRouteScale is null || minRouteScale <= WorldScale) && (maxRouteScale is null || maxRouteScale >= WorldScale))
		{
			PathF drawPath = new();
			drawPath.MoveTo(WorldToLocalPoint(path.First().Point));

			foreach (var point in path)
				switch (point)
				{
					case StraightLineSegment s:
						drawPath.LineTo(WorldToLocalPoint(s.Point));
						break;

					case ArcSegment a:
						drawPath.QuadTo(WorldToLocalPoint(a.ControlPoint), WorldToLocalPoint(a.End));
						break;

					case InvisibleSegment i:
						drawPath.MoveTo(WorldToLocalPoint(i.Point));
						break;
				}

			_canvas.DrawPath(drawPath);
		}

		if ((minPointLabelScale is null || minPointLabelScale <= WorldScale) && (maxPointLabelScale is null || maxPointLabelScale >= WorldScale))
			foreach (var p in path.Where(p => p.PointLabel is not null))
				DrawString(p.PointLabel!, p.Point, HorizontalAlignment.Center);

		if ((minNameLabelScale is null || minNameLabelScale <= WorldScale) && (maxNameLabelScale is null || maxNameLabelScale >= WorldScale) && !string.IsNullOrWhiteSpace(path.Name))
			DrawString(path.Name, path.Select(p => p.Point).Average(), HorizontalAlignment.Center);
	}

	public void FillPath(PathF path, WindingMode windingMode) => throw new NotSupportedException();
	public void FillPath(Route path) => FillPath(path, null, null);
	public void FillPath(Route path, float? minScale, float? maxScale) => FillPath(path, minScale, maxScale, minScale, maxScale);
	public void FillPath(Route path, float? minRouteScale, float? maxRouteScale, float? minLabelScale, float? maxLabelScale) =>
			FillPath(path, minRouteScale, maxRouteScale, minLabelScale, maxLabelScale, minLabelScale, maxLabelScale);
	public void FillPath(Route path, float? minRouteScale, float? maxRouteScale, float? minPointLabelScale, float? maxPointLabelScale, float? minNameLabelScale, float? maxNameLabelScale)
	{
		if (!path.Any(p => IsOnScreen(p.Point)))
			return;

		if ((minRouteScale is null || minRouteScale <= WorldScale) && (maxRouteScale is null || maxRouteScale >= WorldScale))
		{
			PathF drawPath = new();
			drawPath.MoveTo(WorldToLocalPoint(path.First().Point));

			foreach (var point in path)
				switch (point)
				{
					case StraightLineSegment s:
						drawPath.LineTo(WorldToLocalPoint(s.Point));
						break;

					case ArcSegment a:
						drawPath.QuadTo(WorldToLocalPoint(a.ControlPoint), WorldToLocalPoint(a.End));
						break;

					case InvisibleSegment i:
						drawPath.MoveTo(WorldToLocalPoint(i.Point));
						break;
				}

			_canvas.FillPath(drawPath);
		}

		if ((minPointLabelScale is null || minPointLabelScale <= WorldScale) && (maxPointLabelScale is null || maxPointLabelScale >= WorldScale))
			foreach (var p in path.Where(p => p.PointLabel is not null))
				DrawString(p.PointLabel!, p.Point, HorizontalAlignment.Center);

		if ((minNameLabelScale is null || minNameLabelScale <= WorldScale) && (maxNameLabelScale is null || maxNameLabelScale >= WorldScale) && !string.IsNullOrWhiteSpace(path.Name))
			DrawString(path.Name, path.Select(p => p.Point).Average(), HorizontalAlignment.Center);
	}

	public void SubtractFromClip(float x, float y, float width, float height) => throw new NotImplementedException();
	public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) => throw new NotImplementedException();
	public void ClipRectangle(float x, float y, float width, float height) => throw new NotImplementedException();
	public void DrawLine(float x1, float y1, float x2, float y2) => DrawLine(new(y1, x1), new(y2, x2));
	public void DrawLine(Coordinate start, Coordinate end) => _canvas.DrawLine(WorldToLocalPoint(start), WorldToLocalPoint(end));
	public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) => throw new NotImplementedException();
	public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) => throw new NotImplementedException();
	public void DrawRectangle(Coordinate corner, float width, float height) => DrawRectangle(corner.Longitude, corner.Latitude, width, height);
	public void DrawRectangle(float x, float y, float width, float height)
	{
		PointF corner = WorldToLocalPoint(new(y, x));
		_canvas.DrawRectangle(corner.X, corner.Y, DistanceToWidth(width), DistanceToHeight(height));
	}

	public void FillRectangle(Coordinate corner, float width, float height) => FillRectangle(corner.Longitude, corner.Latitude, width, height);
	public void FillRectangle(float x, float y, float width, float height)
	{
		PointF corner = WorldToLocalPoint(new(y, x));
		_canvas.FillRectangle(corner.X, corner.Y, DistanceToWidth(width), DistanceToHeight(height));
	}

	public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => throw new NotImplementedException();
	public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => throw new NotImplementedException();
	public void DrawEllipse(float x, float y, float width, float height) => DrawEllipse(new(y, x), width, height);
	public void DrawEllipse(Coordinate center, float width, float height)
	{
		PointF middle = WorldToLocalPoint(center);
		PointF top = WorldToLocalPoint(center.FixRadialDistance(000, height / 2)),
			  left = WorldToLocalPoint(center.FixRadialDistance(270, width / 2)),
			bottom = WorldToLocalPoint(center.FixRadialDistance(180, height / 2)),
			 right = WorldToLocalPoint(center.FixRadialDistance(090, width / 2));

		float lWidth = right.X - left.X,
			 lHeight = bottom.Y - top.Y;
		_canvas.DrawEllipse(middle.X - lWidth / 2, middle.Y - lHeight / 2, lWidth, lHeight);
	}

	public void DrawCircle(Coordinate center, float radius) => DrawEllipse(center, radius * 2, radius * 2);

	public void FillEllipse(float x, float y, float width, float height) => FillEllipse(new(y, x), width, height);
	public void FillEllipse(Coordinate topLeft, float width, float height)
	{
		PointF corner = WorldToLocalPoint(topLeft);
		_canvas.FillEllipse(corner.X, corner.Y, DistanceToWidth(width), DistanceToHeight(height));
	}
	public void FillCircle(Coordinate center, float radius) => FillEllipse(center - new Coordinate(-radius, radius), radius * 2, radius * 2);

	public void DrawString(string value, Coordinate point, HorizontalAlignment horizontalAlignment) =>
		DrawString(value, point.Longitude, point.Latitude, horizontalAlignment);
	public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment)
	{
		PointF corner = WorldToLocalPoint(new(y, x));
		_canvas.DrawString(value, corner.X, corner.Y, horizontalAlignment);
	}
	public void DrawString(string value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0)
	{
		PointF corner = WorldToLocalPoint(new(y, x));
		_canvas.DrawString(value, corner.X, corner.Y, DistanceToWidth(width), DistanceToHeight(height), horizontalAlignment, verticalAlignment, textFlow, lineSpacingAdjustment);
	}
	public void DrawText(IAttributedText value, float x, float y, float width, float height) => throw new NotImplementedException();
	public void Rotate(float degrees, float x, float y) => _canvas.Rotate(degrees, x, y);
	public void Rotate(float degrees) => _canvas.Rotate(degrees);
	public void Scale(float sx, float sy) => _canvas.Scale(sx, sy);
	public void Translate(float tx, float ty) => _canvas.Translate(tx, ty);
	public void ConcatenateTransform(Matrix3x2 transform) => _canvas.ConcatenateTransform(transform);
	public void SaveState() => _canvas.SaveState();
	public bool RestoreState() => _canvas.RestoreState();
	public void ResetState() => _canvas.ResetState();
	public void SetShadow(SizeF offset, float blur, Color color) => _canvas.SetShadow(WorldToLocalSize(offset), blur, color);
	public void SetFillPaint(Paint paint, RectF rectangle) => _canvas.SetFillPaint(paint, WorldToLocalArea(rectangle));
	public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) => throw new NotImplementedException();
	public SizeF GetStringSize(string value) { W2DCanvasState state = (W2DCanvasState)_canvas.GetType().GetProperty("CurrentState", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_canvas)!; return GetStringSize(value, state.Font ?? Microsoft.Maui.Graphics.Font.Default, state.FontSize); }
	public SizeF GetStringSize(string value, IFont font, float fontSize) { var data = _canvas.GetStringSize(value, font, fontSize); return new(WidthToDistance(data.Width), HeightToDistance(data.Height)); }
	public SizeF GetStringSize(string value, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) { W2DCanvasState state = (W2DCanvasState)_canvas.GetType().GetProperty("CurrentState", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_canvas)!; return GetStringSize(value, state.Font ?? Microsoft.Maui.Graphics.Font.Default, state.FontSize, horizontalAlignment, verticalAlignment); }
	public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) { var data = _canvas.GetStringSize(value, font, fontSize, horizontalAlignment, verticalAlignment); return new(WidthToDistance(data.Width), HeightToDistance(data.Height)); }
}
