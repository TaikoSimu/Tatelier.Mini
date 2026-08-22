using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static DxLibDLL.DX;

namespace Tatelier.Mini.Play
{
	class LyricItem
	{
		public int Time;
		public string Text;
	}

	/// <summary>
	/// 歌詞表示クラス
	/// </summary>
	/// <remarks>
	/// OpenTatelierのLyricImageControlと違い、テーマ設定(フォント・色)や画像の
	/// 事前レンダリング・キャッシュは行わず、毎フレームDrawStringFで直接
	/// 描画するだけの簡易版。Tatelier Miniには設定画面・テーマ機構が無いため。
	/// </remarks>
	class Lyric
	{
		const uint TextColor = 0xFFFFFF;
		const uint EdgeColor = 0x000000;

		readonly LinkedList<LyricItem> list = new LinkedList<LyricItem>();

		LinkedListNode<LyricItem> currentItem;

		bool isVisible = false;

		public bool HasLyric => list.Count > 0 && list.First.Value.Time != int.MinValue;

		/// <param name="filePath">LRC歌詞ファイルパス</param>
		/// <param name="inlineLyricList">TJA譜面内の#LYRICで指定された歌詞リスト。1件以上あればLRCファイルより優先される</param>
		public Lyric(string filePath, IEnumerable<Score.Play.Chart.LyricItem> inlineLyricList = null)
		{
			// TJA譜面内に#LYRICが1つでもあれば、そちらを優先しLRCファイルは読まない
			// (TJAP3系シミュレータと同様、譜面埋め込みの歌詞を優先する)
			if (inlineLyricList != null && inlineLyricList.Any())
			{
				foreach (var item in inlineLyricList)
				{
					list.AddLast(new LyricItem()
					{
						Time = item.StartTime,
						Text = item.Text,
					});
				}
			}
			else
			{
				string[] lines;

				if (File.Exists(filePath))
				{
					lines = File.ReadAllLines(filePath, Score.Utility.GetEncodingFromFile(filePath) ?? Encoding.UTF8);
				}
				else
				{
					lines = new string[0];
				}

				var regex = new Regex(@"\[(\S+)\](.*)");

				foreach (var line in lines)
				{
					try
					{
						if (regex.IsMatch(line))
						{
							var groups = regex.Match(line).Groups;

							string[] split = groups[1].Value.Split(':', '.');

							int time = 0;

							if (split.Length == 2)
							{
								time += int.Parse(split[0]) * 60000;
								time += int.Parse(split[1]) * 1000;
							}
							else
							{
								// [分:秒.ミリ秒]形式
								time += int.Parse(split[0]) * 60000;
								time += int.Parse(split[1]) * 1000;
								time += int.Parse(split[2]) * 10;
							}

							list.AddLast(new LyricItem()
							{
								Time = time,
								Text = groups[2].Value,
							});
						}
					}
					catch
					{
						// 不正な行は読み飛ばす
					}
				}
			}

			if (list.Count == 0)
			{
				list.AddLast(new LyricItem()
				{
					Time = int.MinValue,
					Text = "",
				});
			}

			currentItem = list.First;
		}

		public void Update(int nowMillisec)
		{
			// 特訓モードのスクラブ(巻き戻し)にも対応できるよう、前方だけでなく後方にも移動できるようにする
			while (currentItem.Previous != null && currentItem.Value.Time > nowMillisec)
			{
				currentItem = currentItem.Previous;
			}

			while (currentItem.Next != null && currentItem.Next.Value.Time <= nowMillisec)
			{
				currentItem = currentItem.Next;
			}

			isVisible = currentItem.Value.Time <= nowMillisec;
		}

		/// <param name="centerX">歌詞テキストの中心X座標</param>
		/// <param name="y">歌詞テキストの上端Y座標</param>
		public void Draw(float centerX, float y)
		{
			if (!HasLyric || !isVisible)
			{
				return;
			}

			string text = currentItem.Value.Text;

			// #LYRIC(引数無し)による「歌詞を空欄にする」指定
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			float width = GetDrawStringWidth(text, text.Length);
			DrawStringF(centerX - width / 2, y, text, TextColor, EdgeColor);
		}
	}
}
