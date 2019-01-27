using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Sichem
{
	public static class ConstantsUtility
	{
		public static Dictionary<string, string> DeserializeFromXml(string path)
		{
			var result = new Dictionary<string, string>();

			if (!File.Exists(path))
			{
				return result;
			}

			var xml = new XmlDocument();
			xml.Load(path);

			var root = xml.DocumentElement;
			foreach (XmlElement child in root.ChildNodes)
			{
				result[child.GetAttribute("key")] = child.GetAttribute("value");
			}

			return result;
		}

		public static void SerializeToXml(this Dictionary<string, string> constants, string outputPath)
		{
			var xml = new XmlDocument();

			var root = xml.CreateElement("root");
			foreach (var pair in constants)
			{
				var el = xml.CreateElement("entry");
				el.SetAttribute("key", pair.Key);
				el.SetAttribute("value", pair.Value);

				root.AppendChild(el);
			}

			xml.AppendChild(root);
		}
	}
}
