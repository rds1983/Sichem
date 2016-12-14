using System;

namespace Sichem
{
	internal class CursorProcessResult
	{
		private readonly CursorInfo _info;

		public CursorInfo Info
		{
			get { return _info; }
		}

		public string Expression { get; set; }

		public CursorProcessResult(CursorInfo info)
		{
			if (info == null)
			{
				throw new ArgumentNullException("info");
			}

			_info = info;
		}
	}
}
