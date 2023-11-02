using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class TrackHistory : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex _) { yield break; }

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	private readonly Queue<ImmutableHashSet<Coordinate>> _histories = new();
	private readonly Color _color;

	public TrackHistory(NetworkConnection connection)
	{
		_color = new(0, 0xCC, 0xFF);

		_ = LoadHistoriesAsync(connection);
	}

	async Task LoadHistoriesAsync(NetworkConnection connection)
	{
		while (true)
		{
			if (connection.GetAircraft().Any())
				_histories.Enqueue(connection.GetAircraft().Select(a => (Coordinate)a.Position.Position).ToImmutableHashSet());

			if (_histories.Count > 6)
				_histories.Dequeue();

			await Task.Delay(TimeSpan.FromSeconds(5));
		}
	}

	public void Draw(Transformer canvas, ICanvas originalCanvas)
	{
		if (_histories.Count < 2)
			return;

		canvas.FillColor = _color;

		int startPoint = Math.Max(7, _histories.Count);
		float scalar = (float)Math.Abs(Math.Log(canvas.WorldScale)) * 0.25f;

		foreach ((int distance, Coordinate dot) in Enumerable.Range(1, _histories.Count - 1).SelectMany(i => _histories.SkipLast(i).Last().Select(p => (i, p))).Where(h => canvas.IsOnScreen(h.p)))
			canvas.FillCircle(dot, canvas.WidthToDistance(Math.Clamp((float)Math.Log((startPoint - distance) * scalar), 1f, 6f)));
	}
}
