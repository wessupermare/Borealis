using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

using static CIFPReader.ControlledAirspace;

using CIFP = CIFPReader.CIFP;

namespace Borealis.Layers;
public class Airspace : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	private ImmutableHashSet<(Route Route, AirspaceClass ASClass)> _drawRoutes;
	private Colorscheme _strokeColors, _fillColors;

	readonly Dictionary<AirspaceClass, RectF> _filterControllers = new();
	readonly Dictionary<AirspaceClass, bool> _filtersEnabled = new();

	public Airspace(Colorscheme strokeColors, Colorscheme fillColors, Airports airports, CIFP cifp)
	{
		_drawRoutes = Array.Empty<(Route Route, AirspaceClass ASClass)>().ToImmutableHashSet();
		(_strokeColors, _fillColors) = (strokeColors, fillColors);

		void Load(Airports ap)
		{
			var regions = cifp.Airspaces.Where(airspace => ap.Known.Contains(airspace.Center)).SelectMany(airspace => airspace.Regions).ToArray();

			_drawRoutes = regions.AsParallel().AsUnordered().Select(i =>
			{
				var (boundaries, asClass, altitudes) = i;
				string floorLabel = altitudes.Floor switch {
					CIFPReader.AltitudeMSL msl => (msl.Feet / 100).ToString("000"),
					null => "SFC",
					CIFPReader.AltitudeAGL sfc when sfc.Feet == 0 => "SFC",
					CIFPReader.AltitudeAGL agl when agl.GroundElevation is null => $"SFC + {agl.Feet / 100:000}",
					CIFPReader.AltitudeAGL conv => (conv.ToMSL().Feet / 100).ToString("000"),
					_ => throw new NotImplementedException()
				};

				string altitudeBlock = $"{(altitudes.Ceiling?.Feet / 100)?.ToString("000") ?? "UNL"}\n{floorLabel}";

				Route drawRoute = new(altitudeBlock);
				CIFPReader.Coordinate? first = null, arcVertex = null, arcOrigin = null;
				bool clockwise = false;


				foreach (var seg in boundaries)
				{
					CIFPReader.Coordinate next = seg switch {
						BoundaryArc a => a.ArcVertex,
						BoundaryLine l => l.Vertex,
						BoundaryEuclidean e => e.Vertex,
						BoundaryCircle c => c.Centerpoint,
						_ => throw new NotImplementedException()
					};

					first ??= next;

					if (arcVertex is CIFPReader.Coordinate vertex && arcOrigin is CIFPReader.Coordinate origin)
						drawRoute.AddArc(vertex, next, origin, clockwise);
					else if (seg is BoundaryCircle c)
					{
						if (boundaries.Count() > 1)
							throw new ArgumentException("Made an airspace region with more than just a circle!");

						CIFPReader.Coordinate top = c.Centerpoint.FixRadialDistance(new CIFPReader.TrueCourse(000), c.Radius),
											 left = c.Centerpoint.FixRadialDistance(new CIFPReader.TrueCourse(270), c.Radius),
										   bottom = c.Centerpoint.FixRadialDistance(new CIFPReader.TrueCourse(180), c.Radius),
											right = c.Centerpoint.FixRadialDistance(new CIFPReader.TrueCourse(090), c.Radius);

						clockwise = true;
						drawRoute.Add(top);
						drawRoute.AddArc(top, right, c.Centerpoint, clockwise);
						drawRoute.AddArc(right, bottom, c.Centerpoint, clockwise);
						drawRoute.AddArc(bottom, left, c.Centerpoint, clockwise);
						drawRoute.AddArc(left, top, c.Centerpoint, clockwise);
						break;
					}
					else
						drawRoute.Add(next);

					if (seg is BoundaryArc arc)
					{
						clockwise = arc.BoundaryVia.HasFlag(BoundaryViaType.ClockwiseArc);
						arcOrigin = arc.ArcOrigin.GetCoordinate();
						arcVertex = arc.Vertex;
					}
					else
					{
						arcOrigin = null;
						arcVertex = null;
					}

					if (seg.BoundaryVia.HasFlag(BoundaryViaType.ReturnToOrigin))
					{
						if (arcVertex is CIFPReader.Coordinate retVertex && arcOrigin is CIFPReader.Coordinate retOrigin)
							drawRoute.AddArc(retVertex, first.Value, retOrigin, clockwise);
						else
							drawRoute.Add(first.Value);
					}
				}

				return (drawRoute, asClass);
			}).ToImmutableHashSet();

			OnInvalidating?.Invoke();
		}

		if (airports.Known.Any())
			Load(airports);
		else
			airports.OnAirportsChanged += Load;
	}

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) { yield break; }

	public bool Interact(PointF point, Coordinate _, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Single | ILayer.ClickType.Left))
			return false;

		if (_filterControllers.Where(f => f.Value.Contains(point)).Select(f => (AirspaceClass?)f.Key).SingleOrDefault() is not AirspaceClass asClass)
			return false;

		_filtersEnabled[asClass] = !_filtersEnabled[asClass];
		OnInvalidating?.Invoke();
		return true;
	}

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		AirspaceClass lastClass = AirspaceClass.A;

		static Color select(Colorscheme scheme, AirspaceClass asClass) => asClass switch {
			AirspaceClass.B => scheme.ClassB,
			AirspaceClass.C => scheme.ClassC,
			AirspaceClass.D => scheme.ClassD,
			AirspaceClass.E => scheme.ClassE,
			_ => throw new ArgumentOutOfRangeException(nameof(asClass))
		};

		ImmutableHashSet<(Route Route, AirspaceClass ASClass)> visibleRoutes = _drawRoutes.OrderBy(r => r.ASClass).Where(r => r.Route.Any(p => canvas.IsOnScreen(p.Point))).ToImmutableHashSet();

		foreach ((Route drawRoute, AirspaceClass asClass) in visibleRoutes.Where(r => !_filtersEnabled.ContainsKey(r.ASClass) || _filtersEnabled[r.ASClass]))
		{
			if (asClass != lastClass)
			{
				if (asClass is AirspaceClass.D or AirspaceClass.E)
					canvas.StrokeDashPattern = new float[] { 5, 2 };
				else
					canvas.ResetStroke();

				canvas.StrokeColor = select(_strokeColors, asClass);
				canvas.FillColor = select(_fillColors, asClass);
				canvas.FontColor = select(_strokeColors, asClass);
				canvas.StrokeSize = asClass switch {
					AirspaceClass.B or AirspaceClass.C => 2,
					AirspaceClass.D or AirspaceClass.E => 1,
					_ => throw new NotImplementedException()
				};

				lastClass = asClass;
			}

			canvas.DrawPath(drawRoute, null, null, 0.0001f, null);
			canvas.FillPath(drawRoute, null, null, 0.0001f, null);
		}

		var classes = visibleRoutes.Select(r => r.ASClass).Distinct().OrderDescending().ToArray();
		int block = 0, rightBound = (int)canvas.DistanceToWidth(canvas.Width), lowerBound = (int)canvas.DistanceToHeight(canvas.Height);

		foreach (var removeKey in _filterControllers.Keys.Where(fk => !classes.Contains(fk)))
			_filterControllers.Remove(removeKey);

		foreach (AirspaceClass asClass in classes)
		{
			RectF boundary = new(rightBound - 30, lowerBound - (30 * block) - 30, 25, 25);

			_filterControllers[asClass] = boundary;

			if (!_filtersEnabled.ContainsKey(asClass))
				_filtersEnabled[asClass] = true;

			if (asClass is AirspaceClass.D or AirspaceClass.E)
				canvas.StrokeDashPattern = new float[] { 5, 2 };
			else
				canvas.ResetStroke();

			originalCanvas.StrokeColor = select(_strokeColors, asClass);
			originalCanvas.FillColor = select(_fillColors, asClass).WithAlpha(_filtersEnabled[asClass] ? 0.5f : 0);
			originalCanvas.FontColor = select(_strokeColors, asClass);

			canvas.StrokeSize = 2;

			originalCanvas.FillRectangle(boundary);
			originalCanvas.DrawRectangle(boundary);
			originalCanvas.DrawString(Enum.GetName(asClass), boundary, HorizontalAlignment.Center, VerticalAlignment.Center);

			++block;
		}
	}
}
