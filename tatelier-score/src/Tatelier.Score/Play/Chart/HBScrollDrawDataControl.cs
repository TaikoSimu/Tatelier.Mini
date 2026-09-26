using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tatelier.Score.Play.Chart
{
	/// <summary>
	/// HBSCROLLの座標計算時点での「現在時刻」の情報
	/// </summary>
	public struct HBScrollCamera
	{
		/// <summary>
		/// 現在時刻が属するBPM区間のインデックス
		/// </summary>
		public int Index;

		/// <summary>
		/// 座標計算に使う現在時刻(ms)。#DELAY中はDELAY開始時刻で止まる。
		/// </summary>
		public double Millisec;
	}

	/// <summary>
	/// HBSCROLL(#HBSCROLL/#BMSCROLL)の座標計算
	/// </summary>
	/// <remarks>
	/// 太鼓さん次郎(ver2.92)の実装に合わせている。
	/// ・BPM区間はBPMCHANGEの記述順に並べた1本のリストとして扱う
	///   (譜面分岐がある場合は共通部分+最初の分岐のBPMCHANGEのみ)。
	/// ・ある時刻が属する区間は「開始時刻がその時刻以下である区間のうち、リスト上で最後のもの」
	///   (マイナスBPMで時刻が逆行していても、区間の終了時刻は見ない)。
	/// ・音符の位置 = (現在の区間の残り時間×現在の区間のBPM + 間の各区間の長さ×BPM
	///   + 音符の区間の経過時間×音符の区間のBPM) × 音符の#SCROLL。
	///   ただし「音符の区間が先頭」「現在の区間が最後」「次の区間の開始時刻が音符の時刻より後」
	///   のいずれかの場合は、(音符の時刻-現在時刻)×音符の区間のBPM×#SCROLL で求める。
	/// ・#DELAYはBPM区間とは別に管理する。DELAY中は現在時刻をDELAY開始時刻で止め(譜面が停止する)、
	///   現在時刻から音符の時刻までの間にあるDELAYの分(DELAYの長さ×その時点のBPM×#SCROLL)を差し引く。
	///   負の#DELAYは停止・差し引きの対象にしない。
	/// ・#BMSCROLLの場合は#SCROLLを無視する(常に1倍)。
	/// ・座標は区間ごとに太鼓さん次郎の画面上(1小節=420px)で整数へ切り捨ててから足し合わせる
	///   (32bit整数の範囲外は画面外扱い)。
	/// </remarks>
	/// <summary>
	/// HBSCROLL用の#DELAYの情報
	/// </summary>
	public class HBScrollDelay
	{
		/// <summary>
		/// 開始時間(ms)
		/// </summary>
		public int StartMillisec;

		/// <summary>
		/// 長さ(ms)
		/// </summary>
		public int Duration;

		/// <summary>
		/// DELAY開始時点のBPM
		/// </summary>
		public double BPM;
	}

	public class HBScrollDrawDataControl
	{
		public List<HBScrollDrawDataItem> ItemList = new List<HBScrollDrawDataItem>();

		/// <summary>
		/// #DELAYのリスト(長さが正のもののみ、記述順)
		/// </summary>
		public List<HBScrollDelay> DelayList = new List<HBScrollDelay>();

		/// <summary>
		/// #SCROLLを無視するかどうか(#BMSCROLL)
		/// </summary>
		public bool IgnoreScrollSpeed = false;

		public void Add(HBScrollDrawDataItem item)
		{
			ItemList.Add(item);
			prefixPointCache.Clear();
		}

		public void Clear()
		{
			ItemList.Clear();
			DelayList.Clear();
			prefixPointCache.Clear();
		}

		/// <summary>
		/// 指定時刻が属する区間のインデックスを取得する
		/// (開始時刻が指定時刻以下である区間のうち、リスト上で最後のもの。無ければ0)
		/// </summary>
		public int GetIndex(double millisec)
		{
			for (int i = ItemList.Count - 1; i >= 0; i--)
			{
				if (ItemList[i].StartMillisec <= millisec)
				{
					return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// 指定時刻が属する区間を取得する
		/// </summary>
		public HBScrollDrawDataItem GetItem(double millisec)
		{
			return ItemList.Count > 0 ? ItemList[GetIndex(millisec)] : null;
		}

		/// <summary>
		/// 現在時刻の情報を取得する
		/// </summary>
		public HBScrollCamera GetCamera(int nowMillisec)
		{
			// #DELAY中はDELAY開始時刻で止める
			// (HBSCROLL譜面では演奏時刻そのものがHBSCROLL用の時刻(TaikojiroTime参照)に揃っている)
			int now = nowMillisec;
			foreach (var delay in DelayList)
			{
				if (delay.StartMillisec <= now
					&& now <= delay.StartMillisec + delay.Duration)
				{
					now = delay.StartMillisec;
				}
			}

			return new HBScrollCamera()
			{
				Index = GetIndex(now),
				Millisec = now,
			};
		}

		/// <summary>
		/// 太鼓さん次郎の1小節の幅(px)。座標の切り捨てはこの幅の画面上で行う。
		/// </summary>
		public const double TaikojiroMeasureWidth = 420;

		/// <summary>
		/// 音符描画領域の幅(Tatelierの1小節の幅)
		/// </summary>
		public double AreaWidth = TaikojiroMeasureWidth;

		/// <summary>
		/// 区間ごとの座標(太鼓さん次郎の画面上で切り捨て済み)の累積。#SCROLL×スクロールスピードの値ごとに作る。
		/// </summary>
		class PrefixPoint
		{
			/// <summary>
			/// 座標の累積
			/// </summary>
			public long[] Point;

			/// <summary>
			/// 座標が32bit整数の範囲外になった区間数の累積
			/// </summary>
			public int[] OverflowCount;
		}

		readonly Dictionary<double, PrefixPoint> prefixPointCache = new Dictionary<double, PrefixPoint>();

		/// <summary>
		/// 画面外を表す座標(太鼓さん次郎で座標が範囲外になった場合の値)
		/// </summary>
		public const int OutOfRangePoint = int.MinValue;

		/// <summary>
		/// 区間の(時間×BPM)による座標を、太鼓さん次郎の画面上の座標(1小節=420px)で求め、
		/// 32bit整数へ0方向に切り捨てる(太鼓さん次郎のCVTTSD2SI命令と同じ)。
		/// 範囲外の場合はnullを返す(太鼓さん次郎では桁あふれするが、ここでは画面外扱いにする)。
		/// </summary>
		/// <remarks>
		/// 区間が数千個ある譜面では、この切り捨て誤差が積み重なって音符の位置が大きく変わるため、
		/// 太鼓さん次郎と同じ見た目にするには同じ画面サイズで切り捨てる必要がある。
		/// </remarks>
		static long? GetTerm(double millisec, double bpm, double scale)
		{
			double value = millisec * bpm / 240000 * TaikojiroMeasureWidth * scale;
			if (double.IsNaN(value)
				|| value >= 2147483648.0
				|| value <= -2147483649.0)
			{
				return null;
			}
			return (long)value;
		}

		PrefixPoint GetPrefixPoint(double scale)
		{
			if (!prefixPointCache.TryGetValue(scale, out var prefix))
			{
				prefix = new PrefixPoint()
				{
					Point = new long[ItemList.Count],
					OverflowCount = new int[ItemList.Count],
				};
				for (int i = 1; i < ItemList.Count; i++)
				{
					var prev = ItemList[i - 1];
					var term = GetTerm(ItemList[i].StartMillisec - prev.StartMillisec, prev.BPM, scale);
					prefix.Point[i] = prefix.Point[i - 1] + (term ?? 0);
					prefix.OverflowCount[i] = prefix.OverflowCount[i - 1] + (term.HasValue ? 0 : 1);
				}
				prefixPointCache[scale] = prefix;
			}
			return prefix;
		}

		/// <summary>
		/// 判定枠から見た、指定時刻の音符等の座標(px)を取得する
		/// </summary>
		/// <remarks>
		/// 太鼓さん次郎と同じく、区間ごとに#SCROLL等を掛けて太鼓さん次郎の画面上で切り捨ててから
		/// 足し合わせ、最後にTatelierの画面の大きさ(AreaWidth)へ拡大する。
		/// 極端な#SCROLL等で座標が32bit整数の範囲外になった場合は、太鼓さん次郎では整数の
		/// 桁あふれが起きるが、ここでは常に画面外(OutOfRangePoint)として扱う。
		/// </remarks>
		/// <param name="millisec">音符等の時刻(ms)</param>
		/// <param name="item">音符等が属する区間(nullの場合は時刻から求める)</param>
		/// <param name="camera">現在時刻の情報</param>
		/// <param name="scale">#SCROLL×スクロールスピード(GetScrollSpeed適用後の値)</param>
		public int GetDistance(int millisec, HBScrollDrawDataItem item, HBScrollCamera camera, double scale)
		{
			if (ItemList.Count == 0)
			{
				return 0;
			}

			if (item == null)
			{
				item = ItemList[GetIndex(millisec)];
			}

			long? result;
			int nextIndex = camera.Index + 1;
			if (item.Index != 0
				&& nextIndex < ItemList.Count
				&& ItemList[nextIndex].StartMillisec <= millisec)
			{
				var prefix = GetPrefixPoint(scale);
				var first = GetTerm(ItemList[nextIndex].StartMillisec - camera.Millisec, ItemList[camera.Index].BPM, scale);
				var last = GetTerm(millisec - item.StartMillisec, item.BPM, scale);

				if (first == null
					|| last == null
					|| prefix.OverflowCount[item.Index] != prefix.OverflowCount[nextIndex])
				{
					result = null;
				}
				else
				{
					result = first + (prefix.Point[item.Index] - prefix.Point[nextIndex]) + last;
				}
			}
			else
			{
				result = GetTerm(millisec - camera.Millisec, item.BPM, scale);
			}

			// 現在時刻から音符の時刻までの間にあるDELAYの分を差し引く
			foreach (var delay in DelayList)
			{
				if (result == null)
				{
					break;
				}
				if (camera.Millisec <= delay.StartMillisec
					&& delay.StartMillisec <= millisec)
				{
					result -= GetTerm(delay.Duration, delay.BPM, scale);
				}
			}

			if (result == null
				|| result.Value < int.MinValue
				|| int.MaxValue < result.Value)
			{
				return OutOfRangePoint;
			}

			double point = result.Value * AreaWidth / TaikojiroMeasureWidth;
			if (point < int.MinValue || int.MaxValue < point)
			{
				return OutOfRangePoint;
			}
			return (int)point;
		}

		/// <summary>
		/// 音符等に適用する#SCROLLの値を取得する
		/// </summary>
		public double GetScrollSpeed(double scrollSpeed)
		{
			return IgnoreScrollSpeed ? 1.0 : scrollSpeed;
		}

		public HBScrollDrawDataControl()
		{

		}
	}
}
