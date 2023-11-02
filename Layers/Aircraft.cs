using Borealis.Data;

using System.Text.RegularExpressions;

using AC = TrainingServer.Extensibility.Aircraft;

namespace Borealis.Layers;
public class Aircraft : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public IEnumerable<AC> Selected => _network.GetAircraft().Where(ac => _selected.Contains(ac.Metadata.Callsign));

	public event Action? OnInvalidating;

	private readonly Colorscheme _color;
	private readonly HashSet<string> _selected = [];
	private readonly Scope _scope;
	private readonly NetworkConnection _network;

	public Aircraft(Colorscheme color, NetworkConnection connection, Scope scope)
	{
		(_network, _color, _scope) = (connection, color, scope);
	}

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_network.GetAircraft().Where(ac => pattern.IsMatch(ac.Metadata.Callsign)).Select(ac => (ac.Metadata.Callsign, (Coordinate)ac.Position.Position));

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Left | ILayer.ClickType.Single) || _scope.LastTransform is not Transformer t)
			return false;

		if (_network.GetAircraft().Select(ac => (ac, point.Distance(t.WorldToLocalPoint(ac.Position.Position))))
				.Where(i => i.Item2 < 20)
				.OrderBy(i => i.Item2)
				.Select(i => i.ac)
				.FirstOrDefault() is not AC ac)
			return false;

		if (_selected.Contains(ac.Metadata.Callsign))
			_selected.Remove(ac.Metadata.Callsign);
		else
			_selected.Add(ac.Metadata.Callsign);

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

		AC[] visibleAircraft = _network.GetAircraft().Where(ac => canvas.IsOnScreen(ac.Position.Position)).ToArray();

		foreach (var aircraft in visibleAircraft)
		{
			if (_selected.Contains(aircraft.Metadata.Callsign))
			{
				float vectorDistance = aircraft.Movement.Speed / 25f;
				canvas.DrawLine(aircraft.Position.Position, aircraft.Position.Position.FixRadialDistance(aircraft.Position.Heading, vectorDistance));

				if (aircraft.Movement.Speed <= 0)
					continue;

				for (int marker = vectorSpacing; marker <= vectorDistance; marker += vectorSpacing)
				{
					Coordinate midpoint = aircraft.Position.Position.FixRadialDistance(aircraft.Position.Heading, marker);
					canvas.DrawPath(new Route("", new Coordinate[] {
					aircraft.Position.Position.FixRadialDistance(aircraft.Position.Heading + 10, marker),
					midpoint,
					aircraft.Position.Position.FixRadialDistance(aircraft.Position.Heading - 10, marker),
				}));

					canvas.DrawString(marker.ToString(), midpoint - new Coordinate(0.0025f, 0), HorizontalAlignment.Center);
				}

				Coordinate leftPoint = aircraft.Position.Position.FixRadialDistance(aircraft.Position.Heading, aircraft.Movement.Speed * 0.0028f);
				Coordinate rightPoint = leftPoint;
				float leftHeading = aircraft.Position.Heading, rightHeading = aircraft.Position.Heading;

				Route leftTurn = new("", leftPoint);
				Route rightTurn = new("", rightPoint);

				const int TURN_STEP = 5;
				float distanceStep = TURN_STEP / 60f / 60f * aircraft.Movement.Speed;
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
			canvas.DrawString(Enum.GetName(aircraft.Metadata.Rules) ?? "?", aircraft.Position.Position, HorizontalAlignment.Center);
	}
}
