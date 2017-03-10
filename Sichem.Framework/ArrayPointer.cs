using System;
using System.Runtime.InteropServices;

namespace Sichem
{
	public unsafe interface Pointer : IDisposable
	{
		long Size { get; }
		void* Pointer { get; }
	}

	public unsafe class ArrayPointer<T> : Pointer
	{
		private GCHandle _handle;
		private bool _disposed;

		public GCHandle Handle
		{
			get { return _handle; }
		}

		public void* Pointer { get; private set; }

		public T[] Data { get; private set; }

		public T this[long index]
		{
			get { return Data[index]; }
			set { Data[index] = value; }
		}

		public long Size { get; private set; }
		public long ElementSize { get; private set; }

		public ArrayPointer(long size)
			: this(new T[size])
		{
		}

		public ArrayPointer(T[] data)
		{
			Data = data;

			Pointer = null;
			if (data != null)
			{
				_handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				var addr = _handle.AddrOfPinnedObject();
				Pointer = addr.ToPointer();
				ElementSize = Marshal.SizeOf(typeof (T));
				Size = ElementSize*data.Length;
			}
			else
			{
				ElementSize = 0;
				Size = 0;
			}

			lock (Operations._lock)
			{
				Operations._allocatedTotal += Size;
			}
		}

		~ArrayPointer()
		{
			Dispose(false);
		}

		public void *GetAddress(long index)
		{
			return (byte *)Pointer + index*ElementSize;
		}

		public void Dispose()
		{
			Dispose(true);
			
			// This object will be cleaned up by the Dispose method.
			// Therefore, you should call GC.SupressFinalize to
			// take this object off the finalization queue
			// and prevent finalization code for this object
			// from executing a second time.
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			lock (Operations._lock)
			{
				Operations._allocatedTotal -= Size;
			}

			if (Data != null)
			{
				_handle.Free();
				Pointer = null;
				Data = null;
				Size = 0;
			}

			_disposed = true;
		}
	}
}