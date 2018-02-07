namespace Sichem
{
	public class FunctionGenerationConfig: BaseConfig
	{
		public string ThisName { get; set; }
		public bool Static { get; set; }
		public int? ThisArgPosition { get; set; }
	}
}