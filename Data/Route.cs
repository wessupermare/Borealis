using Simplify.NET;

using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borealis.Data;

[JsonConverter(typeof(RouteJsonConverter))]
public class Route : IEnumerable<Route.RouteSegment>
{
	public string Name { get; init; }

	public IEnumerable<Coordinate> Points => _segments.Select(rs => rs.Point);
	public IEnumerable<(Coordinate Point, string? PointLabel)> LabelledPoints => _segments.Select(rs => (rs.Point, rs.PointLabel));

	readonly List<RouteSegment> _segments = new();

	public Route(string name) => Name = name;

	public Route(string name, params (Coordinate start, string? pointLabel)[] points)
	{
		Name = name;
		_segments = new(points.Select(p => new StraightLineSegment(p.start, p.pointLabel)));
	}

	public Route(string name, params RouteSegment[] segments)
	{
		Name = name;
		_segments = new(segments);
	}

	public Route(string name, params Coordinate[] points)
	{
		Name = name;

		foreach (Coordinate i in points)
			_segments.Add(new StraightLineSegment(i, null));
	}

	public void Add(Coordinate point, string? pointLabel = null) => _segments.Add(new StraightLineSegment(point, pointLabel));
	public void AddArc(Coordinate controlPoint, Coordinate end, string? pointLabel = null) =>
		_segments.Add(new ArcSegment(controlPoint, end, pointLabel));

	public void AddArc(Coordinate from, Coordinate to, Coordinate origin, bool clockwise)
	{
		void arcTo(CIFPReader.Coordinate vertex, CIFPReader.Coordinate next, CIFPReader.Coordinate origin)
		{
#pragma warning disable IDE0042 // Variable declaration can be deconstructed
			var startData = origin.GetBearingDistance(vertex);
			var endData = origin.GetBearingDistance(next);
#pragma warning restore IDE0042 // Variable declaration can be deconstructed
			var startBearing = startData.bearing ?? new(vertex.Latitude > origin.Latitude ? 0 : 180);
			var endBearing = endData.bearing ?? new(next.Latitude > origin.Latitude ? 0 : 180);

			var guessBearing = startBearing.Angle(endBearing);
			CIFPReader.Course realBearing = new CIFPReader.TrueCourse(guessBearing / 2 + startBearing.Degrees);

			if ((clockwise && guessBearing < 0) || (!clockwise && guessBearing > 0))
				realBearing += 180;

			var midPoint = origin.FixRadialDistance(realBearing, startData.distance);

			if (midPoint != vertex && midPoint != next && Math.Abs(startBearing.Angle(realBearing) + realBearing.Angle(endBearing)) > 45)
			{
				arcTo(vertex, midPoint, origin);
				arcTo(midPoint, next, origin);
				return;
			}

			var controlLat = 2 * midPoint.Latitude - vertex.Latitude / 2 - next.Latitude / 2;
			var controlLon = 2 * midPoint.Longitude - vertex.Longitude / 2 - next.Longitude / 2;

			AddArc(new((float)controlLat, (float)controlLon), next);
		}

		arcTo(from, to, origin);
	}

	public void Jump(Coordinate point) => _segments.Add(new InvisibleSegment(point));

	public Coordinate Average() => _segments.Select(p => p.Point).Average();

	public string ToSpaceSeparated() => string.Join(" ", _segments.Select(p => $"{p.Point.Latitude:00.00000} {p.Point.Longitude:00.00000}"));

	public Route WithName(string name) => new(name, _segments.ToArray());

	public Route WithoutLabels() => new(Name, _segments.Select(s => s.Point).ToArray());

	public Route Simplified(double precision)
	{
		if (_segments.Any(s => s is not StraightLineSegment sls || !string.IsNullOrWhiteSpace(sls.PointLabel)))
			throw new Exception("Cannot simplify a curved, discontinuous, or labelled path.");

		var newPoints = SimplifyNet.Simplify(_segments.Select(seg => new Simplify.NET.Point(seg.Point.Longitude, seg.Point.Latitude)).ToList(), precision, true).Select(p => new Coordinate((float)p.Y, (float)p.X)).ToList();

		if (_segments.Last().Point == _segments.First().Point && newPoints.Last() != newPoints.First())
			newPoints.Add(newPoints.First());

		return new(Name, newPoints.ToArray());
	}

	public static Route operator +(Route first, Route second) => new(first.Name, first._segments.Concat(second._segments).ToArray());

	public override int GetHashCode() => _segments.Aggregate(0, (s, i) => HashCode.Combine(s, i));

	public IEnumerator<RouteSegment> GetEnumerator() => ((IEnumerable<RouteSegment>)_segments).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_segments).GetEnumerator();

	public override string ToString() => Name;

	public abstract record RouteSegment(Coordinate Point, string? PointLabel) { }

	public record StraightLineSegment(Coordinate Point, string? PointLabel) : RouteSegment(Point, PointLabel) { }
	public record ArcSegment(Coordinate ControlPoint, Coordinate End, string? PointLabel) : RouteSegment(End, PointLabel) { }
	public record InvisibleSegment(Coordinate Point) : RouteSegment(Point, null) { }

	internal class RouteJsonConverter : JsonConverter<Route>
	{
		public override Route? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject
			 || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString()?.ToLowerInvariant() is not string prop1name || (prop1name != "name" && prop1name != "segments")
			 || !reader.Read())
				throw new JsonException();

			string name = "";
			List<StraightLineSegment> segments = new();

			if (prop1name == "name")
			{
				name = reader.GetString() ?? throw new JsonException();
				if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString()?.ToLowerInvariant() != "segments"
				 || !reader.Read() || reader.TokenType != JsonTokenType.StartArray)
					throw new JsonException();
			}
			else if (reader.TokenType != JsonTokenType.StartArray)
				throw new JsonException();

			while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				segments.Add(JsonSerializer.Deserialize<StraightLineSegment>(ref reader, options) ?? throw new JsonException());

			if (reader.TokenType != JsonTokenType.EndArray)
				throw new JsonException();

			if (prop1name != "name")
			{
				if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString()?.ToLowerInvariant() != "segments"
				 || !reader.Read() || reader.TokenType != JsonTokenType.String)
					throw new JsonException();

				name = reader.GetString() ?? throw new JsonException();
			}

			if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
				throw new JsonException();

			return new(name, segments.ToArray());
		}

		public override void Write(Utf8JsonWriter writer, Route value, JsonSerializerOptions options)
		{
			if (value._segments.Any(s => s is not StraightLineSegment))
				throw new NotImplementedException();

			writer.WriteStartObject();
			writer.WriteString("name", value.Name);
			writer.WritePropertyName("segments");
			writer.WriteStartArray();

			foreach (StraightLineSegment segment in value._segments.Cast<StraightLineSegment>())
				JsonSerializer.Serialize(writer, segment, options);

			writer.WriteEndArray();
			writer.WriteEndObject();
		}
	}
}
