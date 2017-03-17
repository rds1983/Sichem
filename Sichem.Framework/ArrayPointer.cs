using System;
using System.Runtime.InteropServices;

namespace Sichem
{
	public abstract unsafe class Pointer : IDisposable
	{
		public abstract long Size { get; }
		public abstract void* Ptr { get; }

		public abstract void Dispose();

		public static implicit operator void*(Pointer ptr)
		{
			return ptr.Ptr;
		}

		public static implicit operator byte*(Pointer ptr)
		{
			return (byte *)ptr.Ptr;
		}

		public static void* operator +(Pointer ptr, long index)
		{
			return ptr.GetAddress(index);
		}

		public static void* operator +(Pointer ptr, ulong index)
		{
			return ptr.GetAddress((long)index);
		}


		public abstract void* GetAddress(long index);
	}

	public unsafe class ArrayPointer<T> : Pointer
	{
		private GCHandle _handle;
		private bool _disposed;
		private void* _ptr;
		private long _size;

		public GCHandle Handle
		{
			get { return _handle; }
		}

		public override void* Ptr
		{
			get { return _ptr; }
		}

		public T[] Data { get; private set; }

		public T this[long index]
		{
			get { return Data[index]; }
			set { Data[index] = value; }
		}

		public long Count { get; private set; }

		public override long Size
		{
			get { return _size; }
		}

		public long ElementSize { get; private set; }

		public ArrayPointer(long size)
			: this(new T[size])
		{
		}

		public ArrayPointer(T[] data)
		{
			Data = data;

			_ptr = null;
			if (data != null)
			{
				_handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				var addr = _handle.AddrOfPinnedObject();
				_ptr = addr.ToPointer();
				ElementSize = Marshal.SizeOf(typeof (T));
				Count = data.Length;
				_size = ElementSize*data.Length;
			}
			else
			{
				ElementSize = 0;
				Count = 0;
				_size = 0;
			}

			lock (Operations._lock)
			{
				Operations._allocatedTotal += _size;
			}
		}

		~ArrayPointer()
		{
			Dispose(false);
		}

		public override void* GetAddress(long index)
		{
			return (byte*) Ptr + index*ElementSize;
		}

		public override void Dispose()
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
				_ptr = null;
				Data = null;
				_size = 0;
			}

			_disposed = true;
		}
	}
}