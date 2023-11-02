using Borealis.Data;

using System.Text.RegularExpressions;

namespace Borealis.Layers;
public class FirBoundary : ILayer
{
	public bool Active { get; set; } = true;

	public bool TopMost { get; protected set; } = false;

	public event Action? OnInvalidating;

	public IEnumerable<(string Name, Coordinate Centerpoint)> Find(Regex pattern) =>
		_borders.SelectMany(b => b).GroupBy(g => g.PointLabel ?? "").Where(kvp => pattern.IsMatch(kvp.Key)).Select(kvp => (kvp.Key, kvp.Select(i => i.Point).Average()));

	public bool Interact(PointF _1, Coordinate _2, ILayer.ClickType _3) => false;

	readonly Colorscheme _color;
	readonly List<Route> _borders;

	public FirBoundary(Colorscheme color, Route[] borders)
	{
		(_color, _borders) = (color, []);

		foreach (Route border in borders)
		{
			if (border.LabelledPoints.Count(lp => !string.IsNullOrEmpty(lp.PointLabel)) < 2)
			{
				_borders.Add(border);
				continue;
			}

			Route simplifiedBorder = new(border.Name);

			HashSet<Coordinate> otherLabels = [];

			var pts = border.LabelledPoints.ToArray();
			for (int ptIdx = 0; ptIdx < pts.Length; ++ptIdx)
			{
				if (pts[ptIdx].PointLabel is not string thisLabel)
				{
					simplifiedBorder.Add(pts[ptIdx].Point);
					continue;
				}

				int idxExplorer = ptIdx - 1;
				if (idxExplorer < 0)
					idxExplorer = pts.Length - 1;

				while (pts[idxExplorer].PointLabel is null)
					if (--idxExplorer < 0)
						idxExplorer = pts.Length - 1;

				string lastLabel = pts[idxExplorer].PointLabel!;

				idxExplorer = ptIdx + 1;
				if (idxExplorer >= pts.Length)
					idxExplorer = 0;

				while (pts[idxExplorer].PointLabel is null)
					if (++idxExplorer >= pts.Length)
						idxExplorer = 0;

				string nextLabel = pts[idxExplorer].PointLabel!;

				if (!otherLabels.Any(ol => pts[ptIdx].Point.DistanceTo(ol) < 10f) && (lastLabel != thisLabel || thisLabel != nextLabel || !otherLabels.Any(ol => pts[ptIdx].Point.DistanceTo(ol) < 50f)))
				{
					simplifiedBorder.Add(pts[ptIdx].Point, thisLabel);
					otherLabels.Add(pts[ptIdx].Point);
				}
				else
					simplifiedBorder.Add(pts[ptIdx].Point);
			}

			_borders.Add(simplifiedBorder);
		}
	}

	public void Draw(Transformer canvas, ICanvas _)
	{
		canvas.StrokeSize = 1;
		canvas.FontSize = 16;
		canvas.StrokeColor = _color.FirBoundary;
		canvas.FontColor = _color.FirBoundary;

		foreach (Route border in _borders)
			canvas.DrawPath(border);
	}
}
