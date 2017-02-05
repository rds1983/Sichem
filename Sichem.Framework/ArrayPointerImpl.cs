using System.Runtime.InteropServices;

namespace Sichem
{
	public class ArrayPointerImpl<T>: ArrayPointer
	{
		private readonly T[] _array;
		private readonly int _elementSize;

		public override long Size
		{
			get { return _array.Length * _elementSize; }
		}

		public T this[long index]
		{
			get { return _array[index]; }
			set { _array[index] = value; }
		}

		public ArrayPointerImpl(long size): base(new T[size])
		{
			_array = (T[]) Data;
			_elementSize = Marshal.SizeOf(typeof (T));
		}

		public ArrayPointerImpl(T[] array): base(array)
		{
			_array = array;
			_elementSize = Marshal.SizeOf(typeof(T));
		}
	}
}