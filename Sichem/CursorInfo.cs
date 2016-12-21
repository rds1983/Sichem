using ClangSharp;

namespace Sichem
{
	internal class CursorInfo
	{
		private readonly CXCursor _cursor;
		private readonly CXCursorKind _kind;
		private readonly CXType _type;
		private readonly string _spelling;
		private readonly string _csType;

		public CXCursor Cursor
		{
			get { return _cursor; }
		}

		public CXCursorKind Kind
		{
			get { return _kind; }
		}

		public CXType Type
		{
			get { return _type; }
		}

		public string Spelling
		{
			get { return _spelling; }
		}

		public string CsType
		{
			get { return _csType; }
		}

		public bool IsRecord
		{
			get { return _type.IsRecord(); }
		}

		public bool IsPointer
		{
			get { return _type.IsPointer(); }
		}

		public bool IsPrimitiveNumericType
		{
			get { return _type.kind.IsPrimitiveNumericType(); }
		}

		public CursorInfo(CXCursor cursor)
		{
			_cursor = cursor;
			_kind = clang.getCursorKind(cursor);
			_type = clang.getCursorType(cursor).Desugar();
			_spelling = clang.getCursorSpelling(cursor).ToString();
			_csType = _type.ToCSharpTypeString();
		}
	}
}