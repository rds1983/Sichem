using System;
using System.IO;
using ClangSharp;
using SealangSharp;

namespace Sichem
{
	public abstract class BaseVisitor
	{
		protected readonly CXTranslationUnit _translationUnit;
		protected readonly TextWriter _writer;
		protected int _indentLevel = 2;

		protected BaseVisitor(CXTranslationUnit translationUnit, TextWriter writer)
		{
			if (writer == null)
			{
				throw new ArgumentNullException("writer");
			}

			_translationUnit = translationUnit;
			_writer = writer;
		}

		public abstract void Run();

		protected void WriteIndent()
		{
			for (var i = 0; i < _indentLevel; ++i)
			{
				_writer.Write("\t");
			}
		}

		protected void IndentedWriteLine(string line)
		{
			WriteIndent();
			_writer.WriteLine(line);
		}

		protected void IndentedWrite(string data)
		{
			WriteIndent();
			_writer.Write(data);
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
	}
}
