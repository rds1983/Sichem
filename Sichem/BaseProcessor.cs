using System.Collections.Generic;
using System.IO;
using ClangSharp;

namespace Sichem
{
	public abstract class BaseProcessor
	{
		protected readonly CXTranslationUnit _translationUnit;
		protected int _indentLevel = 2;

		protected abstract TextWriter Writer { get; }
		public abstract Dictionary<string, StringWriter> Outputs { get; }

		protected BaseProcessor(CXTranslationUnit translationUnit)
		{
			_translationUnit = translationUnit;
		}

		public abstract void Run();

		public void WriteIndent()
		{
			if (Writer == null)
			{
				return;
			}

			for (var i = 0; i < _indentLevel; ++i)
			{
				Writer.Write("\t");
			}
		}

		public void IndentedWriteLine(string line)
		{
			if (Writer == null)
			{
				return;
			}

			WriteIndent();
			Writer.WriteLine(line);
		}

		public void IndentedWrite(string data)
		{
			if (Writer == null)
			{
				return;
			}

			WriteIndent();
			Writer.Write(data);
		}

		public void WriteLine()
		{
			if (Writer == null)
			{
				return;
			}

			Writer.WriteLine();
		}

		public void WriteLine(string s)
		{
			if (Writer == null)
			{
				return;
			}

			Writer.WriteLine(s);
		}

		public void Write(string s)
		{
			if (Writer == null)
			{
				return;
			}

			Writer.Write(s);
		}
	}
}