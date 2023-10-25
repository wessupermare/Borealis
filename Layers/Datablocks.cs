using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class Datablocks : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	readonly Aircraft _aircraft;
	readonly CIFPReader.CIFP _cifp;
	readonly HashSet<string> _expandedBlocks = new();
	readonly Dictionary<string, int> _clearanceAids = new();
	ImmutableDictionary<RectF, string> _blockBounds = new Dictionary<RectF, string>().ToImmutableDictionary();

	public Datablocks(Aircraft aircraft, CIFPReader.CIFP cifp) => (_aircraft, _cifp) = (aircraft, cifp);

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) { yield break; }

	public bool Interact(PointF point, Coordinate _2, ILayer.ClickType type)
	{
		if (type != (ILayer.ClickType.Right | ILayer.ClickType.Single))
			return false;

		var selectedBlock = _blockBounds.Where(kvp => kvp.Key.Contains(point)).Select(kvp => kvp.Value).FirstOrDefault();

		if (selectedBlock is null)
			return false;

		if (_expandedBlocks.Contains(selectedBlock))
		{
			_expandedBlocks.Remove(selectedBlock);
			_clearanceAids.Add(selectedBlock, Random.Shared.Next(7201, 7277));
		}
		else if (_clearanceAids.ContainsKey(selectedBlock))
			_clearanceAids.Remove(selectedBlock);
		else
			_expandedBlocks.Add(selectedBlock);

		OnInvalidating?.Invoke();
		return true;
	}

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		canvas.FontSize = 14;
		canvas.FontColor = new(1);
		canvas.StrokeColor = new(1);
		canvas.FillColor = new(0);

		_expandedBlocks.RemoveWhere(ac => !_aircraft.Selected.Any(a => a.Callsign == ac));

		foreach (string key in _clearanceAids.Keys.Where(ac => !_aircraft.Selected.Any(a => a.Callsign == ac)))
			_clearanceAids.Remove(key);

		Dictionary<RectF, string> newBlockBounds = new();

		foreach (Data.Aircraft ac in _aircraft.Selected)
		{
			string content;
			if (_clearanceAids.ContainsKey(ac.Callsign))
			{
				content = $"{ac.Callsign} is cleared to the {ac.Route.Destination} airport via\n";

				IEnumerable<string> route = ac.Route.Route.Split().Where(i => i != "DCT").SelectMany(a => a.Split('.', StringSplitOptions.RemoveEmptyEntries)).ToArray();

				if (route.FirstOrDefault() == ac.Route.Origin)
					route = route.Skip(1);

				if (route.Count() >= 2)
				{
					if (_cifp.Procedures.TryGetValue(route.First(), out var procs)
					 && procs.FirstOrDefault(p => p.Airport == ac.Route.Origin) is CIFPReader.SID proc
					 && proc.EnumerateTransitions().Any(t => t.Outbound == route.Skip(1).First()))
						content += $"the {proc.Name} departure, {route.Skip(1).First()} transition, then as filed.\nOn departure, climb via SID.\nExpect {(ac.Route.Altitude >= 18000 ? $"FL {ac.Route.Altitude / 100:000}" : $"{ac.Route.Altitude} feet")} five minutes after departure.\n";
					else
						content += $"radar vectors {route.First()}, then as filed. On departure, fly runway heading.\nClimb and maintain {(ac.Route.Altitude >= 18000 ? $"FL {ac.Route.Altitude / 100:000}" : $"{ac.Route.Altitude} feet")}.\n";

					content += $"Departure frequency is my frequency <###.###>.\nSquawk {_clearanceAids[ac.Callsign]}.";
				}
				else
					content = "No route filed.";
			}
			else if (_expandedBlocks.Contains(ac.Callsign))
			{
				content = $"Pilot: {ac.Pilot.Vid}\nRoute: {ac.Route.Origin}→{ac.Route.Destination}\n";
				content += string.Join(Environment.NewLine, ac.Route.Route.Split().SelectMany(i => i.Split('.')).Where(l => l != "DCT" && !string.IsNullOrWhiteSpace(l)));
			}
			else
				content = $"{ac.Callsign}\n{(ac.Altitude >= 18000 ? "F" : "A")}{ac.Altitude / 100:000}\n{ac.GroundSpeed}kts\n{ac.Route.Origin} → {ac.Route.Destination}";

			SizeF rectSize = canvas.WorldToLocalSize(canvas.GetStringSize(content, HorizontalAlignment.Left, VerticalAlignment.Center));
			rectSize.Height *= 1.1f;
			rectSize.Width *= 1.025f;
			PointF acPos = canvas.WorldToLocalPoint(ac.Position);

			RectF bounds = new(acPos, rectSize);
			newBlockBounds[bounds] = ac.Callsign;

			originalCanvas.FillRectangle(bounds);
			originalCanvas.DrawRectangle(bounds);
			originalCanvas.DrawString(content, bounds, HorizontalAlignment.Left, VerticalAlignment.Center);
		}

		_blockBounds = newBlockBounds.ToImmutableDictionary();
	}
}
