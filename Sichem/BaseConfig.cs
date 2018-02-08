namespace Sichem
{
	public enum StructType
	{
		Struct,
		Class,
		StaticClass
	}

	public class BaseConfig
	{
		public SourceInfo Source { get; set; }
		public string Name { get; set; }
	}
}
