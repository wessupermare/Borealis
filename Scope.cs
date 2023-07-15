using Borealis.Layers;

using System.Collections;

namespace Borealis;
internal class Scope : IDrawable, IList<ILayer>
{
	readonly List<ILayer> _layers = new();

	public Scope() { }
	public Scope(IEnumerable<ILayer> layers) : this() => _layers = new(layers);

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		Transformer transformer = new(canvas, dirtyRect, new(0, 0), 0.05f);

		foreach (ILayer layer in _layers)
			layer.Draw(transformer);
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
