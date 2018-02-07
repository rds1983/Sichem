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
		public string Name { get; set; }
		public string Source { get; set; }
		public string Class { get; set; }
		public StructType StructType { get; set; }
		public bool Skip { get; set; }
	}
}
