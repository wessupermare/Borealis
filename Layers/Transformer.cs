using Borealis.Data;

using Microsoft.Maui.Graphics.Text;
using Microsoft.UI.Input;

using System.Numerics;
using System.Reflection;

namespace Borealis.Layers;

internal static class Extensions
{
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

internal class Transformer : ICanvas
{
	readonly ICanvas _canvas;
	readonly RectF _dirtyRect;

	readonly Func<float, float> HeightToLatitude;
	readonly Func<float, float> HeightToDistance;
	readonly Func<float, float> WidthToLongitude;
	readonly Func<float, float> WidthToDistance;

	readonly Func<float, float> LatitudeToHeight;
	readonly Func<float, float> DistanceToHeight;
	readonly Func<float, float> LongitudeToWidth;
	readonly Func<float, float> DistanceToWidth;

	public Transformer(ICanvas canvas, RectF dirtyRect, Coordinate worldCenter, float scale)
	{
		const double DEG_TO_RAD = Math.PI / 180.0;
		double latAdjustment = Math.Cos(DEG_TO_RAD * worldCenter.Latitude);

		_canvas = canvas;
		_dirtyRect = dirtyRect;

		HeightToLatitude = height => -HeightToDistance(height - dirtyRect.Center.Y) + worldCenter.Latitude;
		HeightToDistance = height => height * scale;
		WidthToLongitude = width => WidthToDistance(width - dirtyRect.Center.X) + worldCenter.Latitude;
		WidthToDistance = width => (float)(width * scale * latAdjustment);

		LatitudeToHeight = lat => DistanceToHeight(-(lat - worldCenter.Latitude)) + dirtyRect.Center.Y;
		DistanceToHeight = dist => dist / scale;
		LongitudeToWidth = lon => DistanceToWidth(lon - worldCenter.Latitude) + dirtyRect.Center.X;
		DistanceToWidth = dist => (float)(dist / latAdjustment / scale);
	}

	public Coordinate LocalToWorldPoint(PointF local) => new(HeightToLatitude(local.Y), WidthToLongitude(local.X));
	public PointF WorldToLocalPoint(Coordinate world) => new(LongitudeToWidth(world.Longitude), LatitudeToHeight(world.Latitude));

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

	public void FillCanvas() => _canvas.FillRectangle(_dirtyRect);

	public void DrawPath(PathF path) => throw new NotImplementedException();
	public void FillPath(PathF path, WindingMode windingMode) => throw new NotImplementedException();
	public void SubtractFromClip(float x, float y, float width, float height) => throw new NotImplementedException();
	public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) => throw new NotImplementedException();
	public void ClipRectangle(float x, float y, float width, float height) => throw new NotImplementedException();
	public void DrawLine(float x1, float y1, float x2, float y2) => DrawLine(new(y1, x1), new(y2, x2));
	public void DrawLine(Coordinate start, Coordinate end) => _canvas.DrawLine(WorldToLocalPoint(start), WorldToLocalPoint(end));
	public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) => throw new NotImplementedException();
	public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) => throw new NotImplementedException();
	public void DrawRectangle(float x, float y, float width, float height) =>
		_canvas.DrawRectangle(LongitudeToWidth(x), LatitudeToHeight(y), DistanceToWidth(width), DistanceToHeight(height));

	public void FillRectangle(float x, float y, float width, float height) =>
		_canvas.FillRectangle(LongitudeToWidth(x), LatitudeToHeight(y), DistanceToWidth(width), DistanceToHeight(height));

	public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => throw new NotImplementedException();
	public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => throw new NotImplementedException();
	public void DrawEllipse(float x, float y, float width, float height) => _canvas.DrawEllipse(LongitudeToWidth(x), LatitudeToHeight(y), DistanceToWidth(width), DistanceToHeight(height));
	public void FillEllipse(float x, float y, float width, float height) => _canvas.FillEllipse(LongitudeToWidth(x), LatitudeToHeight(y), DistanceToWidth(width), DistanceToHeight(height));
	public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) => _canvas.DrawString(value, LongitudeToWidth(x), LatitudeToHeight(y), horizontalAlignment);
	public void DrawString(string value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) =>
		_canvas.DrawString(value, LongitudeToWidth(x), LatitudeToHeight(y), DistanceToWidth(width), DistanceToHeight(height), horizontalAlignment, verticalAlignment, textFlow, lineSpacingAdjustment);
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
	public SizeF GetStringSize(string value, IFont font, float fontSize) => throw new NotImplementedException();
	public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) => throw new NotImplementedException();
}
