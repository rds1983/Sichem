using System.Collections.Generic;

namespace Sichem
{
	public static unsafe class Operations
	{
		internal static Dictionary<long, Pointer> _pointers = new Dictionary<long, Pointer>();
		internal static long _allocatedTotal = 0;

		public static long AllocatedTotal
		{
			get { return _allocatedTotal; }
		}

		public static void* Malloc(long size)
		{
			var result = new ArrayPointer<byte>(size);
			_pointers[(long) result.Pointer] = result;

			return result.Pointer;
		}

		public static void Memcpy(void* a, void* b, long size)
		{
			var ap = (byte*) a;
			var bp = (byte*) b;
			for (long i = 0; i < size; ++i)
			{
				*ap++ = *bp++;
			}
		}

		public static void Free(void* a)
		{
			Pointer pointer;
			if (!_pointers.TryGetValue((long) a, out pointer))
			{
				return;
			}

			_pointers.Remove((long) pointer.Pointer);
			pointer.Dispose();

		}

		public static void* Realloc(void* a, long newSize)
		{
			Pointer pointer;
			if (!_pointers.TryGetValue((long) a, out pointer))
			{
				// Allocate new
				return Malloc(newSize);
			}

			if (newSize <= pointer.Size)
			{
				// Realloc not required
				return a;
			}

			var result = Malloc(newSize);
			Memcpy(result, a, pointer.Size);

			_pointers.Remove((long)pointer.Pointer);
			pointer.Dispose();

			return result;
		}
	}
}