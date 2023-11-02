using Borealis.Layers;

using CIFPReader;

using System.Collections;

using CIFP = CIFPReader.CIFP;
using Coordinate = Borealis.Data.Coordinate;

namespace Borealis;
public class Scope : IDrawable, IList<ILayer>
{
	public CIFP Cifp { get; set; }
	public bool MagVarLocked { get; set; } = true;

	readonly List<ILayer> _layers = [];

	public Scope() => Cifp = CIFP.Load(FileSystem.CacheDirectory);
	public Scope(IEnumerable<ILayer> layers) : this() => _layers = new(layers);

	public Transformer? LastTransform;
	public RectF? LastBoundingRect;

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		LastTransform = new(canvas, dirtyRect, Centerpoint, _scale, _rotation);
		LastBoundingRect = dirtyRect;

		foreach (ILayer layer in _layers.Where(l => l.Active && !l.TopMost))
		{
			layer.Draw(LastTransform, canvas);

			LastTransform.ResetState();
		}

		foreach (ILayer layer in _layers.Where(l => l.Active && l.TopMost))
		{
			layer.Draw(LastTransform, canvas);

			LastTransform.ResetState();
		}
	}

	private float _scale = 0.05f;
	public void SetScale(float scale) => _scale = scale;

	public Coordinate Centerpoint { get; protected set; } = new();
	private float _rotation = 0;

	public void Teleport(Coordinate newCenterpoint)
	{
		Centerpoint = newCenterpoint;
		SetMagVar();
	}

	public void Drag(SizeF movement)
	{
		const double DEG_TO_RAD = Math.PI / 180;

		float dLat = movement.Height * _scale;
		float dLon = -(float)(movement.Width * _scale * Math.Cos(Centerpoint.Latitude * DEG_TO_RAD));

		Centerpoint += new Coordinate(dLat, dLon);

		if (!MagVarLocked)
			SetMagVar();
	}

	private void SetMagVar()
	{
		var (_, variation) = Cifp.Navaids.GetLocalMagneticVariation(Centerpoint);
		_rotation = (float)variation;
	}

	public ILayer this[int index] { get => ((IList<ILayer>)_layers)[index]; set => ((IList<ILayer>)_layers)[index] = value; }

	public int Count => ((ICollection<ILayer>)_layers).Count;

	public bool IsReadOnly => ((ICollection<ILayer>)_layers).IsReadOnly;

	public void Add(ILayer item) => ((ICollection<ILayer>)_layers).Add(item);
	public void Clear() => ((ICollection<ILayer>)_layers).Clear();
	public bool Contains(ILayer item) => ((ICollection<ILayer>)_layers).Contains(item);
	public void CopyTo(ILayer[] array, int arrayIndex) => ((ICollection<ILayer>)_layers).CopyTo(array, arrayIndex);

	public IEnumerator<ILayer> GetEnumerator() => ((IEnumerable<ILayer>)_layers).GetEnumerator();
	public int IndexOf(ILayer item) => ((IList<ILayer>)_layers).IndexOf(item);
	public void Insert(int index, ILayer item) => ((IList<ILayer>)_layers).Insert(index, item);
	public bool Remove(ILayer item) => ((ICollection<ILayer>)_layers).Remove(item);
	public void RemoveAt(int index) => ((IList<ILayer>)_layers).RemoveAt(index);
	IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_layers).GetEnumerator();
}
