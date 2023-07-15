using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Borealis.Layers;
internal interface ILayer
{
	public abstract void Draw(Transformer canvas);
}

internal class Background : ILayer
{
	readonly Color _fill;

	public Background(Color fillColor) => _fill = fillColor;

	public void Draw(Transformer canvas)
	{
		canvas.FillColor = _fill;
		canvas.FillCanvas();
	}
}
