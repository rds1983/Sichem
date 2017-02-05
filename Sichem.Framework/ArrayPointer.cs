using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sichem
{
	public abstract unsafe class ArrayPointer
	{
		private static readonly Dictionary<long, ArrayPointer> _allocates = new Dictionary<long, ArrayPointer>();

		private readonly object _data;
		private readonly GCHandle _handle;
		private readonly void* _ptr;

		public GCHandle Handle
		{
			get { return _handle; }
		}

		public void* Pointer
		{
			get { return _ptr; }
		}

		public object Data
		{
			get { return _data; }
		}

		public abstract long Size { get; }

		protected ArrayPointer(object data)
		{
			_data = data;

			if (data != null)
			{
				_handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				var addr = _handle.AddrOfPinnedObject();
				_ptr = addr.ToPointer();
			}
		}

		public static void* Allocate<T>(long size)
		{
			var ptr = new ArrayPointerImpl<T>(size);
			_allocates[(long)ptr.Pointer] = ptr;

			return ptr.Pointer;
		}


		public static void* Allocate<T>(T[] data)
		{
			var ptr = new ArrayPointerImpl<T>(data);
			_allocates[(long) ptr.Pointer] = ptr;

			return ptr.Pointer;
		}

		public static void Free(void* ptr)
		{
			_allocates.Remove((long) ptr);
		}

		public static sbyte* Allocatesbyte(long size)
		{
			return (sbyte*)Allocate<sbyte>(size);
		}

		public static sbyte* Allocatesbyte(sbyte[] data)
		{
			return (sbyte*)Allocate(data);
		}

		public static byte* Allocatebyte(long size)
		{
			return (byte*)Allocate<byte>(size);
		}

		public static byte* Allocatebyte(byte[] data)
		{
			return (byte*)Allocate(data);
		}

		public static short* Allocateshort(long size)
		{
			return (short*)Allocate<short>(size);
		}

		public static short* Allocateshort(short[] data)
		{
			return (short*)Allocate(data);
		}

		public static ushort* Allocateushort(long size)
		{
			return (ushort*)Allocate<ushort>(size);
		}

		public static ushort* Allocateushort(ushort[] data)
		{
			return (ushort*)Allocate(data);
		}

		public static int* Allocateint(long size)
		{
			return (int*)Allocate<int>(size);
		}

		public static int* Allocateint(int[] data)
		{
			return (int*)Allocate(data);
		}

		public static uint* Allocateuint(long size)
		{
			return (uint*)Allocate<uint>(size);
		}

		public static uint* Allocateuint(uint[] data)
		{
			return (uint*)Allocate(data);
		}

		public static void* Realloc(void* ptr, long newSize)
		{
			ArrayPointer ap;
			if (!_allocates.TryGetValue((long) ptr, out ap))
			{
				// New allocate
				return Allocate<byte>(newSize);
			}

			if (ap.Size >= newSize)
			{
				// Realloc not required
				return ap.Pointer;
			}

			var result = Allocate<byte>(newSize);
			Memcpy(result, ptr, ap.Size);

			// Remove old data
			Free(ptr);

			return result;
		}

		public static void Memcpy(void* a, void* b, long size)
		{
			byte* ap = (byte*)a;
			byte* bp = (byte*)b;
			for (long i = 0; i < size; ++i)
			{
				*ap++ = *bp++;
			}
		}
	}
}
