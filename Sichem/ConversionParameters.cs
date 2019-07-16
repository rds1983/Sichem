using System;
using System.Collections.Generic;
using System.IO;

namespace Sichem
{
	public enum FunctionArgumentType
	{
		Default,
		Pointer,
		Ref
	}

	public enum ConversionMode
	{
		SingleString,
		MultipleFiles
	}

	[Flags]
	public enum SkipFlags
	{
		None = 0,
		Enums = 1,
		GlobalVariables = 2,
	}

	public class ConversionParameters
	{
		public string InputPath { get; set; }
		public string[] Defines { get; set; }
		public string Namespace { get; set; }
		public string Class { get; set; }

		public ConversionMode ConversionMode
		{
			get; set;
		}

		public string OutputPath
		{
			get; set;
		}

		public SkipFlags SkipFlags
		{
			get; set;
		}

		public bool IsPartial { get; set; }

		public bool AddGeneratedBySichem { get; set; }

		public string[] SkipStructs { get; set; }
		public string[] SkipGlobalVariables { get; set; }
		public string[] SkipFunctions { get; set; }
		public string[] Classes { get; set; }
		public string[] GlobalArrays { get; set; }

		public Action<CursorProcessResult> CustomGlobalVariableProcessor { get; set; }
		public Action<string, string[]> FunctionHeaderProcessed { get; set; }
		public Action BeforeLastClosingBracket { get; set; }
		public Func<string, bool> TreatGlobalPointerAsArray { get; set; }
		public bool GenerateSafeCode { get; set; }
		public Func<string, string> TypeNameReplacer { get; set; }
		public Func<string, string, bool> TreatStructFieldClassPointerAsArray { get; set; }
		public Func<string, string, FunctionArgumentType> TreatFunctionArgClassPointerAsArray { get; set; }
		public Func<string, string, bool> TreatLocalVariableClassPointerAsArray { get; set; }

		public ConversionParameters()
		{
			IsPartial = true;
			Defines = new string[0];
			SkipStructs = new string[0];
			SkipGlobalVariables = new string[0];
			SkipFunctions = new string[0];
			Classes = new string[0];
			GlobalArrays = new string[0];
		}
	}
}