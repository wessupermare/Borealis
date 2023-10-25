using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Aircraft : ILayer
{
	public ImmutableHashSet<Data.Aircraft> TrackedAircraft { get; private set; }

	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public IEnumerable<Data.Aircraft> Selected => TrackedAircraft.Where(ac => _selected.Contains(ac.Callsign));


	public event Action? OnInvalidating;

	private readonly Colorscheme _color;
	private readonly HashSet<string> _selected = new();
	private readonly Scope _scope;

	public Aircraft(Colorscheme color, Whazzup whazzup, Scope scope)
	{
		whazzup.OnCacheUpdated += ac => { TrackedAircraft = ac.ToImmutableHashSet(); OnInvalidating?.Invoke(); };
		(TrackedAircraft, _color, _scope) = (Array.Empty<Data.Aircraft>().ToImmutableHashSet(), color, scope);

		if (!whazzup.IsMonitoring)
			whazzup.BeginMonitoring();
	}

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		TrackedAircraft.Where(ac => pattern.IsMatch(ac.Callsign)).Select(ac => (ac.Callsign, ac.Position));

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Left | ILayer.ClickType.Single) || _scope.LastTransform is not Transformer t)
			return false;

		if (TrackedAircraft.Select(ac => (ac, point.Distance(t.WorldToLocalPoint(ac.Position))))
				.Where(i => i.Item2 < 20)
				.OrderBy(i => i.Item2)
				.Select(i => i.ac)
				.FirstOrDefault() is not Data.Aircraft ac)
			return false;

		if (_selected.Contains(ac.Callsign))
			_selected.Remove(ac.Callsign);
		else
			_selected.Add(ac.Callsign);

		OnInvalidating?.Invoke();

		return true;
	}

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		const int vectorSpacing = 5;

		canvas.StrokeColor = _color.Aircraft;
		canvas.FontColor = _color.Aircraft;

		// Vectors
		canvas.FontSize = Math.Clamp(0.03f / canvas.WorldScale, 5, 15);

		Data.Aircraft[] visibleAircraft = TrackedAircraft.Where(ac => canvas.IsOnScreen(ac.Position)).ToArray();

		foreach (var aircraft in visibleAircraft)
		{
			if (_selected.Contains(aircraft.Callsign))
			{
				float vectorDistance = aircraft.GroundSpeed / 25f;
				canvas.DrawLine(aircraft.Position, aircraft.Position.FixRadialDistance(aircraft.Heading, vectorDistance));

				if (aircraft.GroundSpeed <= 0)
					continue;

				for (int marker = vectorSpacing; marker <= vectorDistance; marker += vectorSpacing)
				{
					Coordinate midpoint = aircraft.Position.FixRadialDistance(aircraft.Heading, marker);
					canvas.DrawPath(new Route("", new Coordinate[] {
					aircraft.Position.FixRadialDistance(aircraft.Heading + 10, marker),
					midpoint,
					aircraft.Position.FixRadialDistance(aircraft.Heading - 10, marker),
				}));

					canvas.DrawString(marker.ToString(), midpoint - new Coordinate(0.0025f, 0), HorizontalAlignment.Center);
				}

				Coordinate leftPoint = aircraft.Position.FixRadialDistance(aircraft.Heading, aircraft.GroundSpeed * 0.0028f);
				Coordinate rightPoint = leftPoint;
				float leftHeading = aircraft.Heading, rightHeading = aircraft.Heading;

				Route leftTurn = new("", leftPoint);
				Route rightTurn = new("", rightPoint);

				const int TURN_STEP = 5;
				float distanceStep = TURN_STEP / 60f / 60f * aircraft.GroundSpeed;
				for (int forwardTime = TURN_STEP; forwardTime <= 60; forwardTime += TURN_STEP)
				{
					rightHeading += TURN_STEP * 3; leftHeading -= TURN_STEP * 3;

					rightPoint = rightPoint.FixRadialDistance(rightHeading, distanceStep);
					leftPoint = leftPoint.FixRadialDistance(leftHeading, distanceStep);

					rightTurn.Add(rightPoint);
					leftTurn.Add(leftPoint);
				}

				canvas.DrawPath(rightTurn);
				canvas.DrawPath(leftTurn);
			}
		}

		// Aircraft
		canvas.FontSize = 12;

		if (canvas.WorldScale < 0.01f)
			canvas.FontSize = 12 + (float)Math.Abs(Math.Log(canvas.WorldScale));

		foreach (var aircraft in visibleAircraft)
			canvas.DrawString(aircraft.FlightRules, aircraft.Position, HorizontalAlignment.Center);
	}
}
