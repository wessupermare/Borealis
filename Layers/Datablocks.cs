using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

using AC = TrainingServer.Extensibility.Aircraft;

namespace Borealis.Layers;
public class Datablocks : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	readonly Aircraft _aircraft;
	readonly Cursor _cursor;
	readonly CIFPReader.CIFP _cifp;
	readonly HashSet<string> _expandedBlocks = new();
	readonly Dictionary<string, int> _clearanceAids = new();
	ImmutableDictionary<RectF, string> _blockBounds = new Dictionary<RectF, string>().ToImmutableDictionary();

	public Datablocks(Aircraft aircraft, CIFPReader.CIFP cifp, Cursor cursor) => (_aircraft, _cifp, _cursor) = (aircraft, cifp, cursor);

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

		_expandedBlocks.RemoveWhere(ac => !_aircraft.Selected.Any(a => a.Metadata.Callsign == ac));

		foreach (string key in _clearanceAids.Keys.Where(ac => !_aircraft.Selected.Any(a => a.Metadata.Callsign == ac)))
			_clearanceAids.Remove(key);

		Dictionary<RectF, string> newBlockBounds = new();

		foreach (AC ac in _aircraft.Selected)
		{
			string content;
			if (_cursor.Coordinate.DistanceTo(ac.Position.Position) > 10)
			{
				// Abbreviate the datablock if out of cursor range.
				content = ac.Metadata.Callsign;
			}
			else
			{
				// Full datablock in range.
				if (_clearanceAids.TryGetValue(ac.Metadata.Callsign, out int assignedSquawk))
				{
					content = $"{ac.Metadata.Callsign} is cleared to the {ac.Metadata.Destination} airport via\n";

					IEnumerable<string> route = ac.Metadata.Route.Split().Where(i => i != "DCT").SelectMany(a => a.Split('.', StringSplitOptions.RemoveEmptyEntries)).ToArray();

					if (route.FirstOrDefault() == ac.Metadata.Origin)
						route = route.Skip(1);

					if (route.Count() >= 2)
					{
						if (_cifp.Procedures.TryGetValue(route.First(), out var procs)
						 && procs.FirstOrDefault(p => p.Airport == ac.Metadata.Origin) is CIFPReader.SID proc
						 && proc.EnumerateTransitions().Any(t => t.Outbound == route.Skip(1).First()))
							content += $"the {proc.Name} departure, {route.Skip(1).First()} transition, then as filed.\nOn departure, climb via SID.\nExpect {(ac.Position.Altitude >= 18000 ? $"FL {ac.Position.Altitude / 100:000}" : $"{ac.Position.Altitude} feet")} five minutes after departure.\n";
						else
							content += $"radar vectors {route.First()}, then as filed. On departure, fly runway heading.\nClimb and maintain {(ac.Position.Altitude >= 18000 ? $"FL {ac.Position.Altitude / 100:000}" : $"{ac.Position.Altitude} feet")}.\n";

						content += $"Departure frequency is my frequency <###.###>.\nSquawk {assignedSquawk}.";
					}
					else
						content = "No route filed.";
				}
				else if (_expandedBlocks.Contains(ac.Metadata.Callsign))
				{
					content = $"Route: {ac.Metadata.Origin}→{ac.Metadata.Destination}\n";
					content += string.Join(Environment.NewLine, ac.Metadata.Route.Split().SelectMany(i => i.Split('.')).Where(l => l != "DCT" && !string.IsNullOrWhiteSpace(l)));
				}
				else
					content = $"{ac.Metadata.Callsign}\n{(ac.Position.Altitude >= 18000 ? "F" : "A")}{ac.Position.Altitude / 100:000}\n{ac.Movement.Speed}kts\n{ac.Metadata.Origin} → {ac.Metadata.Destination}";
			}

			SizeF rectSize = canvas.WorldToLocalSize(canvas.GetStringSize(content, HorizontalAlignment.Center, VerticalAlignment.Top));

			rectSize.Height += 20;
			rectSize.Width += 15;

			PointF acPos = canvas.WorldToLocalPoint(ac.Position.Position);

			RectF bounds = new(acPos, rectSize);
			newBlockBounds[bounds] = ac.Metadata.Callsign;

			originalCanvas.FillRectangle(bounds);
			originalCanvas.DrawRoundedRectangle(bounds, 10);
			originalCanvas.DrawString(content, bounds with { Left = bounds.Left + 5, Top = bounds.Top + 5 }, HorizontalAlignment.Left, VerticalAlignment.Top);
		}

		_blockBounds = newBlockBounds.ToImmutableDictionary();
	}
}
