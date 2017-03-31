using System;
using System.IO;
using ClangSharp;

namespace Sichem
{
	public abstract class BaseProcessor
	{
		protected readonly CXTranslationUnit _translationUnit;
		protected readonly TextWriter _writer;
		protected int _indentLevel = 2;

		protected BaseProcessor(CXTranslationUnit translationUnit, TextWriter writer)
		{
			if (writer == null)
			{
				throw new ArgumentNullException("writer");
			}

			_translationUnit = translationUnit;
			_writer = writer;
		}

		public abstract void Run();

		protected void WriteIndent()
		{
			for (var i = 0; i < _indentLevel; ++i)
			{
				_writer.Write("\t");
			}
		}

		protected void IndentedWriteLine(string line)
		{
			WriteIndent();
			_writer.WriteLine(line);
		}

		protected void IndentedWrite(string data)
		{
			WriteIndent();
			_writer.Write(data);
		}
	}
}
