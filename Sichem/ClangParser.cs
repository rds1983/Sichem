using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClangSharp;

namespace Sichem
{
	public class ClangParser
	{
		public BaseProcessor Processor { get; private set; }

		public string StringResult
		{
			get; set;
		}

		public Dictionary<string, string> Constants
		{
			get; set;
		}

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
			Processor = cw;
			Processor.Run();
/*			using (var tw = new StreamWriter(Path.Combine(parameters.OutputPath, "dump.txt")))
			{
				Processor = new DumpProcessor(tu, tw);
				Processor.Run();
			}*/

			if (cw.StringWriter != null)
			{
				StringResult = cw.StringWriter.ToString();
			}
			Constants = cw.Constants;

			clang.disposeTranslationUnit(tu);
			clang.disposeIndex(createIndex);

		}
	}
}