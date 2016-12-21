using System.IO;

namespace Sichem
{
	public class ConversionParameters
	{
		public string InputPath { get; set; }
		public TextWriter Output { get; set; }
		public string[] Defines { get; set; }
		public string Namespace { get; set; }
		public string Class { get; set; }
		public bool IsPartial { get; set; }
		public string[] SkipStructs { get; set; }
		public string[] SkipGlobalVariables { get; set; }
		public string[] SkipFunctions { get; set; }

		public ConversionParameters()
		{
			IsPartial = true;
		}
	}
}
