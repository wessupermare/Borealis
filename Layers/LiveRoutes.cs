using Borealis.Data;

using System.Text.RegularExpressions;

using AC = TrainingServer.Extensibility.Aircraft;

namespace Borealis.Layers;
public class LiveRoutes : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	readonly Aircraft _aircraft;
	readonly CIFPReader.CIFP _cifp;
	readonly Colorscheme _strokeColors, _labelColors;

	public LiveRoutes(Colorscheme strokeColors, Colorscheme labelColors, Aircraft aircraft, CIFPReader.CIFP cifp) =>
		(_strokeColors, _labelColors, _aircraft, _cifp) = (strokeColors, labelColors, aircraft, cifp);

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) { yield break; }

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	public void Draw(Transformer canvas, ICanvas screen)
	{
		canvas.StrokeColor = _strokeColors.Route;
		canvas.FontColor = _labelColors.Route;

		foreach (AC ac in _aircraft.Selected)
		{
			string routeName = $"{ac.Metadata.Origin}→{ac.Metadata.Destination}";
			Queue<string> segments = new();

			segments.Enqueue(ac.Metadata.Origin);

			foreach (string step in ac.Metadata.Route.ToUpperInvariant().Split().SelectMany(s => s.Trim().Split('.')))
				segments.Enqueue(step);

			segments.Enqueue(ac.Metadata.Destination);

			if (_cifp.Aerodromes.TryGetValue(ac.Metadata.Origin, out var orA) && _cifp.Aerodromes.TryGetValue(ac.Metadata.Destination, out var arA) && orA.Location.Longitude > arA.Location.Longitude)
				routeName = $"{ac.Metadata.Destination}←{ac.Metadata.Origin}";

			Route drawRoute = new($"{routeName} ({ac.Metadata.Callsign})");
			string lastName = ac.Metadata.Origin;
			Coordinate lastPoint = _cifp.Aerodromes.TryGetValue(ac.Metadata.Origin, out var originAp) ? originAp.Location.GetCoordinate() : new();
			CIFPReader.Procedure? queuedProc = null;
			CIFPReader.Airway? queuedAirway = null;

			while (segments.TryDequeue(out string? point))
			{
				if (queuedProc is CIFPReader.Procedure proc)
				{
					var availableTransitions = proc is CIFPReader.SID s ? s.EnumerateTransitions() : ((CIFPReader.STAR)proc).EnumerateTransitions();

					if (availableTransitions.Any(at => at.Outbound == point))
						availableTransitions = availableTransitions.Where(at => at.Outbound == point);

					if (availableTransitions.Any(at => at.Inbound == lastName))
						availableTransitions = availableTransitions.Where(at => at.Inbound == lastName);

					foreach (var transition in availableTransitions.Select(at => proc.SelectRoute(at.Inbound, at.Outbound)))
					{
						bool first = true;

						foreach (var instruction in transition.Where(i => i.Endpoint is CIFPReader.ICoordinate))
						{
							(Coordinate pt, string? ptL) = instruction.Endpoint switch {
								CIFPReader.NamedCoordinate nc => (nc.GetCoordinate(), nc.Name),
								CIFPReader.Coordinate c => (c, null),
								_ => throw new NotImplementedException()
							};

							if (first)
								drawRoute.Jump(pt);

							drawRoute.Add(pt, ptL);
							lastPoint = pt;
							lastName = ptL ?? lastName;
							first = false;
						}
					}

					queuedProc = null;
				}
				else if (queuedAirway is CIFPReader.Airway airway)
				{
					IEnumerable<(Coordinate Point, string? Label)> points = airway.Select(p => ((Coordinate)p.Point.GetCoordinate(), p.Name));

					int startIdx = points.TakeWhile(p => p.Label != lastName).Count(),
						endIdx = points.TakeWhile(p => p.Label != point).Count();

					if (startIdx > endIdx)
					{
						points = points.Reverse();
						startIdx = points.Count() - 1 - startIdx;
						endIdx = points.Count() - 1 - endIdx;
					}

					foreach (var p in points.Skip(startIdx).Take(endIdx - startIdx))
						drawRoute.Add(p.Point, p.Label + $" ({airway.Identifier})");

					queuedAirway = null;
				}

				if (_cifp.Aerodromes.TryGetValue(point, out var origin))
				{
					lastPoint = origin.Location.GetCoordinate();
					lastName = origin.Identifier;
					drawRoute.Add(lastPoint, lastName);
				}
				else if (_cifp.Airways.TryGetValue(point, out var airways))
				{
					var viableAirways = airways.Where(aw => aw.Any(p => p.Name == lastName || (p.Point is CIFPReader.NamedCoordinate nc && nc.Name == lastName)));

					if (!viableAirways.Any())
						viableAirways = airways;

					queuedAirway = viableAirways.MinBy(aw => aw.Select(p => lastPoint.DistanceTo(p.Point.GetCoordinate())).Average());
				}
				else if (_cifp.Fixes.TryGetValue(point, out var fixes))
				{
					var rawCoord = fixes.MinBy(f => lastPoint.DistanceTo(f.GetCoordinate()));
					var nextPoint = rawCoord?.GetCoordinate() ?? lastPoint;
					lastName = rawCoord is CIFPReader.NamedCoordinate nc ? nc.Name : point;

					if ((lastPoint.Longitude < -90 && nextPoint.Longitude > 90) || (lastPoint.Longitude > 90 && nextPoint.Longitude < -90))
						drawRoute.Jump(nextPoint);

					drawRoute.Add(nextPoint, lastName);
					lastPoint = nextPoint;
				}
				else if (_cifp.Procedures.TryGetValue(point, out var procedures))
					queuedProc =
						procedures.MinBy(p =>
							p.SelectAllRoutes(_cifp.Fixes)
								.Where(i => i?.Endpoint is CIFPReader.ICoordinate)
								.Average(i => lastPoint.DistanceTo(((CIFPReader.ICoordinate)i!.Endpoint!).GetCoordinate()))) switch {
									CIFPReader.SID sid => sid,
									CIFPReader.STAR star => star,
									_ => throw new NotImplementedException(),
								};
			}

			canvas.DrawPath(drawRoute);
		}
	}
}
