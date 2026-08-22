using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tatelier.Score.Play.Chart
{
	/// <summary>
	/// #LYRIC命令で指定された歌詞情報
	/// </summary>
	public class LyricItem
	{
		/// <summary>
		/// 表示開始時間(ミリ秒)
		/// </summary>
		public int StartTime = 0;

		/// <summary>
		/// 歌詞テキスト
		/// </summary>
		public string Text = "";
	}
}
