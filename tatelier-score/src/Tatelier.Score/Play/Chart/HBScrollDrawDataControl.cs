using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tatelier.Score.Play.Chart
{
	public class HBScrollDrawDataControl
	{
		public List<HBScrollDrawDataItem> ItemList = new List<HBScrollDrawDataItem>();

		public void Add(HBScrollDrawDataItem item)
		{
			ItemList.Add(item);
		}

		public void Clear()
		{
			ItemList.Clear();
		}

		/// <summary>
		/// 指定した時刻を含む区間の中から、最も実時間の幅が狭い(=最も限定的な)区間を返す。
		/// 該当する区間が1つも無ければnullを返す。
		/// </summary>
		/// <remarks>
		/// マイナスBPM/マイナス小節を伴う演出(いわゆる「シェイク」ギミック等)では、
		/// 譜面上は離れた場所にある区間の実時間範囲が、たまたま同じ時刻を含んで
		/// しまうことがある。単純に`ItemList.LastOrDefault(v =&gt; v.IsApplicable(t))`
		/// (リスト内で最後に見つかった区間)を使うと、そのような無関係な(たいてい
		/// 実時間の幅が極端に広い)区間が選ばれてしまい、その時刻における描画位置の
		/// 基準(カメラ位置)が本来と全く違う場所になってしまう(例: 曲の再生開始
		/// 直後なのに、はるか後方の区間が選ばれてしまい、まだ流れてくるはずの
		/// ノーツが最初から判定枠付近に見えてしまう)。
		/// 該当する区間の中から幅が最も狭いものを選ぶことで、この問題を避けつつ、
		/// 区間の重複が無い通常のケースでは従来通り正しい区間が選ばれる。
		/// </remarks>
		public HBScrollDrawDataItem GetNarrowestApplicable(int millisec)
		{
			HBScrollDrawDataItem narrowest = null;
			long narrowestWidth = long.MaxValue;

			foreach (var item in ItemList)
			{
				if (!item.IsApplicable(millisec))
				{
					continue;
				}

				long width = Math.Abs((long)item.FinishMillisec - (long)item.StartMillisec);
				if (width < narrowestWidth)
				{
					narrowestWidth = width;
					narrowest = item;
				}
			}

			return narrowest;
		}

		public HBScrollDrawDataControl()
		{

		}
	}
}
