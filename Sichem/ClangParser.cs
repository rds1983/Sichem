using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClangSharp;

namespace Sichem
{
	public class ClangParser
	{
		private TextWriter _output;

		public void Process(ConversionParameters parameters)
		{
			if (parameters == null)
			{
				throw new ArgumentNullException("parameters");
			}

			var arr = new List<string>();

			foreach (var d in parameters.Defines)
			{
				arr.Add("-D" + d);
			}

			var createIndex = clang.createIndex(0, 0);
			CXUnsavedFile unsavedFile;

			CXTranslationUnit tu;
			var res = clang.parseTranslationUnit2(createIndex,
				parameters.InputPath,
				arr.ToArray(),
				arr.Count,
				out unsavedFile,
				0,
				0,
				out tu);

			if (res != CXErrorCode.CXError_Success)
			{
				var sb = new StringBuilder();

				sb.AppendLine(res.ToString());

				var numDiagnostics = clang.getNumDiagnostics(tu);
				for (uint i = 0; i < numDiagnostics; ++i)
				{
					var diag = clang.getDiagnostic(tu, i);
					sb.AppendLine(clang.getDiagnosticSpelling(diag).ToString());
					clang.disposeDiagnostic(diag);
				}

				throw new Exception(sb.ToString());
			}

			_output = parameters.Output;
			_output.WriteLine("using System;");
			_output.WriteLine("using Sichem;");
			_output.WriteLine();

			if (!string.IsNullOrEmpty(parameters.Namespace))
			{
				_output.Write("namespace {0}\n{{\n\t", parameters.Namespace);
			}

			_output.Write("public static {0} class {1}\n\t{{\n",
				parameters.IsPartial ? "partial" : string.Empty,
				parameters.Class);

			// Structs
			var structsVisitor = new StructVisitor(parameters, tu, _output);
			structsVisitor.Run();

			var functionVisitor = new FunctionVisitor(parameters, tu, _output);
			functionVisitor.Run();

			_output.Write("\t}");

			if (!string.IsNullOrEmpty(parameters.Namespace))
			{
				_output.Write("\n}\n");
			}

/*			using (_writer = new StreamWriter(outputPath + "2"))
			{
				var data = new CXClientData((IntPtr) 0);
				clang.visitChildren(clang.getTranslationUnitCursor(tu), Visit, data);
			}*/

			clang.disposeTranslationUnit(tu);
			clang.disposeIndex(createIndex);
		}

		private string getCursorKindName(CXCursorKind cursorKind)
		{
			var kindName = clang.getCursorKindSpelling(cursorKind);
			var result = kindName.ToString();

			clang.disposeString(kindName);
			return result;
		}

		private string getCursorSpelling(CXCursor cursor)
		{
			var cursorSpelling = clang.getCursorSpelling(cursor);
			var result = cursorSpelling.ToString();

			clang.disposeString(cursorSpelling);
			return result;
		}

		private CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			var location = clang.getCursorLocation(cursor);
			if (clang.Location_isFromMainFile(location) == 0)
				return CXChildVisitResult.CXChildVisit_Continue;

			var cursorKind = clang.getCursorKind(cursor);

			var curLevel = (uint) data;
			var nextLevel = curLevel + 1;

			_output.WriteLine("{0}- {1} {2})\n", curLevel, getCursorKindName(cursorKind), getCursorSpelling(cursor));

			clang.visitChildren(cursor,
				Visit,
				new CXClientData((IntPtr) nextLevel));

			return CXChildVisitResult.CXChildVisit_Continue;
		}
	}
}