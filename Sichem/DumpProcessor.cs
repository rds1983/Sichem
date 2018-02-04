using System;
using System.Collections.Generic;
using System.IO;
using ClangSharp;
using SealangSharp;

namespace Sichem
{
	public class DumpProcessor: BaseProcessor
	{
		private string _currentSource;
		private readonly Dictionary<string, StringWriter> _writers = new Dictionary<string, StringWriter>();

		public override Dictionary<string, StringWriter> Outputs
		{
			get { return _writers; }
		}

		protected override TextWriter Writer
		{
			get
			{
				if (string.IsNullOrEmpty(_currentSource))
				{
					return null;
				}

				StringWriter sw;
				if (!_writers.TryGetValue(_currentSource, out sw))
				{
					sw = new StringWriter();
					_writers[_currentSource] = sw;
				}

				return sw;
			}
		}

		public DumpProcessor(CXTranslationUnit translationUnit)
			: base(translationUnit)
		{
		}

		private CXChildVisitResult DumpCursor(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			DumpCursor(cursor);

			return CXChildVisitResult.CXChildVisit_Continue;
		}

		protected void DumpCursor(CXCursor cursor)
		{
			var cursorKind = clang.getCursorKind(cursor);

			var line = string.Format("// {0}- {1} - {2}", clang.getCursorKindSpelling(cursorKind),
				clang.getCursorSpelling(cursor),
				clang.getTypeSpelling(clang.getCursorType(cursor)));

			var addition = string.Empty;

			switch (cursorKind)
			{
				case CXCursorKind.CXCursor_UnaryExpr:
				case CXCursorKind.CXCursor_UnaryOperator:
					addition = string.Format("Unary Operator: {0} ({1})",
						sealang.cursor_getUnaryOpcode(cursor),
						sealang.cursor_getOperatorString(cursor));
					break;
				case CXCursorKind.CXCursor_BinaryOperator:
					addition = string.Format("Binary Operator: {0} ({1})",
						sealang.cursor_getBinaryOpcode(cursor),
						sealang.cursor_getOperatorString(cursor));
					break;
				case CXCursorKind.CXCursor_IntegerLiteral:
				case CXCursorKind.CXCursor_FloatingLiteral:
				case CXCursorKind.CXCursor_CharacterLiteral:
				case CXCursorKind.CXCursor_StringLiteral:
					addition = string.Format("Literal: {0}",
						sealang.cursor_getLiteralString(cursor));
					break;
			}

			if (!string.IsNullOrEmpty(addition))
			{
				line += " [" + addition + "]";
			}

			IndentedWriteLine(line);

			_indentLevel++;
			clang.visitChildren(cursor, DumpCursor, new CXClientData(IntPtr.Zero));
			_indentLevel--;
		}

		public override void Run()
		{
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), DumpCursor, new CXClientData(IntPtr.Zero));
		}
	}
}
