using System;
using System.Runtime.InteropServices;

namespace Sichem
{
	public unsafe interface Pointer : IDisposable
	{
		long Size { get; }
		void *Pointer { get; }
	}

	public unsafe class ArrayPointer<T>: Pointer
	{
		private T[] _data;
		private GCHandle _handle;
		private readonly long _elementSize;
		private bool _disposed;

		public GCHandle Handle
		{
			get { return _handle; }
		}

		public void* Pointer { get; private set; }

		public T[] Data
		{
			get { return _data; }
		}

		public T this[long index]
		{
			get { return _data[index]; }
			set { _data[index] = value; }
		}

		public long Size {
			get { return _data != null? _data.LongLength * _elementSize:0; }
		}

		public ArrayPointer(long size): this(new T[size])
		{
		}

		public ArrayPointer(T[] data)
		{
			_elementSize = Marshal.SizeOf(typeof (T));
			_data = data;

			Pointer = null;
			if (data != null)
			{
				_handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				var addr = _handle.AddrOfPinnedObject();
				Pointer = addr.ToPointer();
			}

			Operations._allocatedTotal += Size;
		}

		~ArrayPointer()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			Operations._allocatedTotal -= Size;

			if (Operations._allocatedTotal < 0)
			{
				var k = 5;
			}

			if (_data != null)
			{
				_handle.Free();
				Pointer = null;
				_data = null;
			}

			_disposed = true;
		}
	}
}
