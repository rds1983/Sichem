using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClangSharp;

namespace Sichem
{
	internal class StructVisitor : BaseVisitor
	{
		private readonly ConversionParameters _parameters;

		public ConversionParameters Parameters
		{
			get { return _parameters; }
		}

		private readonly HashSet<string> _visitedStructs = new HashSet<string>();

		private int fieldPosition;

		public StructVisitor(ConversionParameters parameters, CXTranslationUnit translationUnit, TextWriter writer)
			: base(translationUnit, writer)
		{
			if (parameters == null)
			{
				throw new ArgumentNullException("parameters");
			}

			_parameters = parameters;
		}

		private CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			if (cursor.IsInSystemHeader())
			{
				return CXChildVisitResult.CXChildVisit_Continue;
			}

			CXCursorKind curKind = clang.getCursorKind(cursor);
			if (curKind == CXCursorKind.CXCursor_StructDecl)
			{
				fieldPosition = 0;
				var structName = clang.getCursorSpelling(cursor).ToString();

				// struct names can be empty, and so we visit its sibling to find the name
				if (string.IsNullOrEmpty(structName))
				{
					var forwardDeclaringVisitor = new ForwardDeclarationVisitor(cursor);
					clang.visitChildren(clang.getCursorSemanticParent(cursor), forwardDeclaringVisitor.Visit,
						new CXClientData(IntPtr.Zero));
					structName = clang.getCursorSpelling(forwardDeclaringVisitor.ForwardDeclarationCursor).ToString();

					if (string.IsNullOrEmpty(structName))
					{
						structName = "_";
					}
				}

				if (!_visitedStructs.Contains(structName) && !Parameters.SkipStructs.Contains(structName))
				{
					IndentedWriteLine("private class " + structName);
					IndentedWriteLine("{");

					_indentLevel++;
					clang.visitChildren(cursor, Visit, new CXClientData(IntPtr.Zero));
					_indentLevel--;

					IndentedWriteLine("}");
					_writer.WriteLine();

					_visitedStructs.Add(structName);
				}

				return CXChildVisitResult.CXChildVisit_Continue;
			}

			if (curKind == CXCursorKind.CXCursor_FieldDecl)
			{
				var fieldName = clang.getCursorSpelling(cursor).ToString();
				if (string.IsNullOrEmpty(fieldName))
				{
					fieldName = "field" + fieldPosition; // what if they have fields called field*? :)
				}

				fieldPosition++;

				IndentedWrite("public ");

				var canonical = clang.getCanonicalType(clang.getCursorType(cursor));
				_writer.Write(canonical.ToCSharpTypeString());
				_writer.Write(" ");

				fieldName = fieldName.FixSpecialWords();

				_writer.Write(fieldName);
				_writer.Write(";\n");

				return CXChildVisitResult.CXChildVisit_Continue;
			}

			return CXChildVisitResult.CXChildVisit_Recurse;
		}

		public override void Run()
		{
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), Visit, new CXClientData(IntPtr.Zero));
		}
	}
}