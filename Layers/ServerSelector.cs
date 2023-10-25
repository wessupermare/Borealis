using Borealis.Data;

using System.Collections.Immutable;
using System.Text.RegularExpressions;

using TrainingServer.Networking;

namespace Borealis.Layers;
public class ServerSelector : ILayer
{
	public event Action<(Colorscheme Primary, Colorscheme Labels)>? ColorsChanged;

	public event Action? OnInvalidating;

	internal NetworkConnection Connection { get; init; } = new();

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) { yield break; }
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = true;

	readonly Scope _scope;

	ImmutableArray<ServerInfo> _servers = new();

	public ServerSelector(Scope scope, string endpoint)
	{
		_scope = scope;
		_ = LoadServersAsync(endpoint);
	}

	int _selected = -1;

	public bool Interact(PointF point, Coordinate position, ILayer.ClickType type)
	{
		if (point.X < 50 || point.X > 50 + w || !type.HasFlag(ILayer.ClickType.Single | ILayer.ClickType.Left))
			return false;

		int hitBox = (int)((point.Y - 70) / (h + 5));

		if (hitBox < 0 || hitBox > _servers.Length)
			return false;

		_selected = hitBox;
		_ = Connection.SelectServerAsync(_servers.ToArray()[_selected].Guid);
		TopMost = false;
		OnInvalidating?.Invoke();
		return true;
	}

	int w = 0, h = 0;

	public void Draw(Transformer tr, ICanvas canvas)
	{
		tr.FillColor = new();
		tr.FillCanvas();

		float hp = _scope.LastBoundingRect?.Height / 100 ?? 10,
			  wp = _scope.LastBoundingRect?.Width / 100 ?? 20;

		canvas.FontSize = 20;
		canvas.FontColor = new(1f);

		SizeF bounds = _servers.Select(si => tr.WorldToLocalSize(tr.GetStringSize($"{si.ReadableName} ({si.Guid.ToString()[..8]})", HorizontalAlignment.Left, VerticalAlignment.Center))).MaxBy(b => b.Width);
		w = (int)bounds.Width + 10; h = (int)bounds.Height + 10;

		int row = 0;
		foreach (ServerInfo si in _servers)
		{
			canvas.StrokeColor = new(1f);
			canvas.FillColor = row == _selected ? new Color(255, 255, 255, 127) : new(0f);

			int x = 50, y = row * (h + 5) + 70;

			canvas.FillRectangle(x, y, w, h);
			canvas.DrawRectangle(x, y, w, h);
			canvas.DrawString($"{si.ReadableName} ({si.Guid.ToString()[..8]})", x, y, w, h, HorizontalAlignment.Left, VerticalAlignment.Center);

			row++;
		}
	}

	async Task LoadServersAsync(string endpoint)
	{
		Connection.SelectEndpoint(endpoint);

		while (TopMost)
		{
			if ((await Connection.ListServersAsync())?.ToArray() is ServerInfo[] servers)
			{
				_servers.Clear();

				List<ServerInfo> s = new();
				foreach (ServerInfo server in servers)
				{
					s.Add(server);
					s.Add(new(Guid.NewGuid(), server.ReadableName + " fake clone"));
				}

				_servers = s.ToImmutableArray();
				OnInvalidating?.Invoke();
			}

			await Task.Delay(TimeSpan.FromSeconds(5));
		}
	}
}