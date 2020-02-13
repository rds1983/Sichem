using System;
using System.Collections.Generic;
using System.Text;
using ClangSharp;

namespace Sichem
{
	public class ClangParser
	{
		public ConversionResult Process(ConversionParameters parameters)
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

//			arr.Add("-I" + @"D:\Develop\Microsoft Visual Studio 12.0\VC\include");

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

			var numDiagnostics = clang.getNumDiagnostics(tu);
			for (uint i = 0; i < numDiagnostics; ++i)
			{
				var diag = clang.getDiagnostic(tu, i);
				var str =
					clang.formatDiagnostic(diag,
						(uint)
							(CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation |
							 CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceRanges)).ToString();
				Logger.LogLine(str);
				clang.disposeDiagnostic(diag);
			}

			if (res != CXErrorCode.CXError_Success)
			{
				var sb = new StringBuilder();

				sb.AppendLine(res.ToString());

				numDiagnostics = clang.getNumDiagnostics(tu);
				for (uint i = 0; i < numDiagnostics; ++i)
				{
					var diag = clang.getDiagnostic(tu, i);
					sb.AppendLine(clang.getDiagnosticSpelling(diag).ToString());
					clang.disposeDiagnostic(diag);
				}

				throw new Exception(sb.ToString());
			}

			// Process
			var cw = new ConversionProcessor(parameters, tu);
			var result = cw.Run();
/*			using (var tw = new StreamWriter(Path.Combine(parameters.OutputPath, "dump.txt")))
			{
				Processor = new DumpProcessor(tu, tw);
				Processor.Run();
			}*/

			clang.disposeTranslationUnit(tu);
			clang.disposeIndex(createIndex);

			return result;
		}
	}
}