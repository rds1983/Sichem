using System.Collections.Generic;

namespace Sichem
{
	public static unsafe class Operations
	{
		internal static Dictionary<long, Pointer> _pointers = new Dictionary<long, Pointer>();
		internal static long _allocatedTotal;
		internal static object _lock = new object();

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

		public static void MemMove(void* a, void* b, long size)
		{
			using (var temp = new ArrayPointer<byte>(size))
			{
				Memcpy(temp.Pointer, b, size);
				Memcpy(a, temp.Pointer, size);
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

		public static int Memcmp(void* a, void* b, long size)
		{
			var result = 0;
			var ap = (byte*)a;
			var bp = (byte*)b;
			for (long i = 0; i < size; ++i)
			{
				if (*ap != *bp)
				{
					result += 1;
				}
				ap++;
				bp++;
			}

			return result;
		}
	}
}