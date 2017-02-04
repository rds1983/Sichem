using System;
using System.Runtime.InteropServices;

namespace Sichem
{
	public struct Pointer<T>
	{
		private static readonly Pointer<T> _null = new Pointer<T>(null);

		private bool _isValue;
		private byte[] _data;
		private T[] _data2;
		private long _position;
		private long _elementSize;

		public byte[] Data
		{
			get { return _data; }
		}

		public long Position
		{
			get { return _position; }
			set { _position = value; }
		}

		public long Size
		{
			get
			{
				if (_isValue)
				{
					return _data != null ? _data.Length/_elementSize : 0;
				}

				return _data2 != null ? _data2.Length : 0;
			}
		}

		public long ElementSize
		{
			get { return _elementSize; }
		}

		public bool IsNull
		{
			get
			{
				if (_isValue)
				{
					return _data == null || _data.Length == 0;
				}

				return _data2 == null || _data2.Length == 0;
			}
		}

		public static Pointer<T> Null
		{
			get { return _null; }
		}

		public T this[int index]
		{
			get { return GetValue(index); }
			set { SetValue(index, value); }
		}

		public T this[uint index]
		{
			get { return GetValue(index); }
			set { SetValue(index, value); }
		}

		public T this[long index]
		{
			get { return GetValue(index); }
			set { SetValue(index, value); }
		}

		public T this[ulong index]
		{
			get { return GetValue((long) index); }
			set { SetValue((long) index, value); }
		}

		public T CurrentValue
		{
			get { return this[0]; }
			set { this[0] = value; }
		}

		public Pointer(long size)
		{
			var type = typeof (T);

			if (type.IsValueType && !type.IsGenericType)
			{
				_isValue = true;
				_elementSize = Marshal.SizeOf(typeof (T));
				_data = new byte[size*_elementSize];
				_data2 = null;
			}
			else
			{
				_isValue = false;
				_elementSize = 0;
				_data = null;
				_data2 = new T[size];
			}

			_position = 0;
		}

		public Pointer(T[] data) : this(data != null ? data.Length : 0)
		{
			if (data != null)
			{
				for (long i = 0; i < data.Length; ++i)
				{
					SetValue(i, data[i]);
				}
			}
		}

		private T GetValue(long index)
		{
			if (_isValue)
			{
				unsafe
				{
					fixed (byte* p = &_data[_position + index*_elementSize])
					{
						var result = (T)Marshal.PtrToStructure(new IntPtr(p), typeof(T));

						return result;
					}
				}
			}

			return _data2[_position + index];
		}

		private void SetValue(long index, T value)
		{
			if (_isValue)
			{
				unsafe
				{
					fixed (byte* p = &_data[_position + index*_elementSize])
					{
						Marshal.StructureToPtr(value, new IntPtr(p), false);
					}
					return;
				}
			}

			_data2[_position + index] = value;
		}

		public T GetAndMove()
		{
			var result = CurrentValue;
			Move();

			return result;
		}

		public void SetAndMove(T value)
		{
			CurrentValue = value;
			Move();
		}

		public void Move()
		{
			_position += _elementSize;
		}

		public void PlusAssign(long index)
		{
			_position += index*_elementSize;
		}

		public void MinusAssign(long index)
		{
			_position -= index*_elementSize;
		}

		public void Reset()
		{
			if (_isValue)
			{
				_data = null;
			}
			else
			{
				_data2 = null;
			}
			_position = 0;
		}

		public void Rewind()
		{
			_position = 0;
		}

		public void Realloc(long newSize)
		{
			if (_isValue)
			{
				if (_data == null || newSize <= _data.Length)
				{
					return;
				}

				var oldData = _data;
				_data = new byte[newSize*_elementSize];
				Array.Copy(oldData, _data, oldData.Length);

				return;
			}

			if (_data2 == null || newSize <= _data2.Length)
			{
				return;
			}

			var oldData2 = _data2;
			_data2 = new T[newSize];
			Array.Copy(oldData2, _data2, oldData2.Length);

		}

		public void Realloc(ulong newSize)
		{
			Realloc((long) newSize);
		}

		public override bool Equals(object b)
		{
			if (b == null || b.ToString() == "0")
			{
				return IsNull;
			}

			if (!b.GetType().IsValueType)
			{
				return false;
			}

			var asp = (Pointer<T>) b;

			return _data == asp._data &&
			       _data2 == asp._data2 &&
			       _position == asp._position;
		}

		public Pointer<T> Plus(long length)
		{
			var result = this;

			result._position += length*_elementSize;
			return result;
		}

		public Pointer<T> Minus(long length)
		{
			var result = this;

			result._position -= length*_elementSize;
			return result;
		}

		public long Minus(Pointer<T> b)
		{
			return (_position - b.Position)/_elementSize;
		}

		public bool Lesser(Pointer<T> b)
		{
			return _position < b.Position;
		}

		public bool Greater(Pointer<T> b)
		{
			return _position > b.Position;
		}

		public bool LesserEqual(Pointer<T> b)
		{
			return _position <= b.Position;
		}

		public bool GreaterEqual(Pointer<T> b)
		{
			return _position >= b.Position;
		}

		public bool Lesser(long i)
		{
			return !IsNull;
		}

		public bool Greater(long i)
		{
			throw new NotImplementedException();
		}

		public Pointer<T2> Cast<T2>()
		{
			var result = new Pointer<T2>
			{
				_data = _data,
				_elementSize = Marshal.SizeOf(typeof (T2)),
				_isValue = _isValue,
				_position = _position
			};

			return result;
		}
	}
}