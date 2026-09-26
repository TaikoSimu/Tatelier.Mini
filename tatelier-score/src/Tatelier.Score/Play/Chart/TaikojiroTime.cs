using System;
using System.Collections.Generic;

namespace Tatelier.Score.Play.Chart
{
	/// <summary>
	/// HBSCROLLの座標計算用に、太鼓さん次郎(ver2.92)と同じ方法で求めた時刻を計算する。
	/// </summary>
	/// <remarks>
	/// 太鼓さん次郎は、音符・BPMCHANGE・小節の時刻を「基準点からの経過時間を整数msへ切り捨てて足す」
	/// ことを繰り返して求めるため、BPMCHANGEが非常に多い譜面では正確な時刻から数十msずれていく。
	/// HBSCROLLの座標はこの時刻を使って区間を選ぶため、遠くの音符(ギミック用の飾り等)の位置が
	/// 大きく変わる。判定に使う時刻(StartMillisec)は変えず、HBSCROLLの座標計算にだけ使う。
	///
	/// 基準点の規則(太鼓さん次郎のパーサーより):
	/// ・音符/BPMCHANGEの時刻 = 基準点の時刻 + 切り捨て(基準点からの正確な経過時間) + 基準点以降のDELAY
	///   基準点は「同じ小節内の直前の、小節の途中にあるBPMCHANGE」、無ければ「小節の開始」。
	/// ・小節の終わりの時刻 = 直前のBPMCHANGEの時刻 + 切り捨て(そのBPMCHANGEの小節の終わりまで)
	///   + 切り捨て(それ以降の小節の長さの合計) + 直前のBPMCHANGE以降のDELAY
	/// ・時刻が負になる場合は0にする。
	/// ・#DELAYの長さは整数msへ切り捨てる。
	/// </remarks>
	public class TaikojiroTime
	{
		/// <summary>
		/// DELAYを含まない正確な経過時間(μs)
		/// </summary>
		public decimal ExactMicrosec = 0;

		int measureStart = 0;
		decimal measureStartExact = 0;

		bool hasMidEntry = false;
		int anchor = 0;
		decimal anchorExact = 0;

		/// <summary>
		/// 基準点(小節開始 or 小節途中のBPMCHANGE)以降の小節途中のDELAY(ms)
		/// </summary>
		int delaySinceAnchor = 0;

		/// <summary>
		/// 直前のBPMCHANGE以降のDELAY(ms)
		/// </summary>
		int delaySinceEntry = 0;

		int lastEntry = 0;
		decimal lastEntryExact = 0;
		bool lastEntryInMeasure = true;
		int lastEntryRemainder = 0;
		decimal lastEntryMeasureEndExact = 0;

		int charPos = 0;
		int currentMeasureChars = 1;
		int prevMeasureChars = 1;

		/// <summary>
		/// この小節で開始時刻が未確定の#DELAY
		/// </summary>
		List<PendingDelay> pendingDelays = new List<PendingDelay>();

		class PendingDelay
		{
			public bool IsMeasureStart;
			public int BaseMillisec;
			public int Duration;
			public double BPM;
			public int Chars;
			public List<(int StartMillisec, int Duration)> Output;
		}

		static int Truncate(decimal microsec)
		{
			// 1/3等の割り切れない値の計算誤差(1499.99999…等)で1ms小さくならないよう、
			// 切り捨ての前に十分小さい桁で丸める
			return (int)decimal.Truncate(decimal.Round(microsec / 1000m, 6));
		}

		/// <summary>
		/// 現在位置の時刻(ms)
		/// </summary>
		public int Now
		{
			get
			{
				int t = hasMidEntry
					? anchor + Truncate(ExactMicrosec - anchorExact)
					: measureStart + Truncate(ExactMicrosec - measureStartExact);
				t += delaySinceAnchor;
				return Math.Max(0, t);
			}
		}

		/// <summary>
		/// 小節の開始
		/// </summary>
		/// <param name="chars">小節内の音符文字数</param>
		public void BeginMeasure(int chars)
		{
			measureStartExact = ExactMicrosec;
			charPos = 0;
			hasMidEntry = false;
			currentMeasureChars = Math.Max(1, chars);
		}

		/// <summary>
		/// 音符文字1つ分進める
		/// </summary>
		public void AdvanceChar(decimal microsec)
		{
			ExactMicrosec += microsec;
			charPos++;
		}

		/// <summary>
		/// 音符文字の無い小節を1小節分進める
		/// </summary>
		public void AdvanceEmptyMeasure(decimal microsec)
		{
			ExactMicrosec += microsec;
		}

		/// <summary>
		/// BPMCHANGE
		/// </summary>
		/// <returns>BPMCHANGEの時刻(ms)</returns>
		public int OnBPMChange()
		{
			int t = Now;
			if (charPos != 0)
			{
				hasMidEntry = true;
				anchor = t;
				anchorExact = ExactMicrosec;
			}
			lastEntry = t;
			lastEntryExact = ExactMicrosec;
			lastEntryInMeasure = true;
			delaySinceAnchor = 0;
			delaySinceEntry = 0;
			return t;
		}

		/// <summary>
		/// #DELAY
		/// </summary>
		/// <param name="sec">DELAYの秒数</param>
		/// <param name="bpm">現在のBPM</param>
		/// <param name="output">開始時刻が確定したDELAYの追加先</param>
		public void OnDelay(double sec, double bpm, List<(int StartMillisec, int Duration)> output)
		{
			int duration = (int)(sec * 1000);
			if (duration == 0)
			{
				return;
			}

			// 開始時刻は小節の拍子が確定してから求める(太鼓さん次郎は小節全体を読んでから処理するため)
			pendingDelays.Add(new PendingDelay()
			{
				IsMeasureStart = charPos == 0,
				BaseMillisec = charPos == 0 ? measureStart : Now,
				Duration = duration,
				BPM = bpm,
				Chars = charPos == 0 ? prevMeasureChars : currentMeasureChars,
				Output = output,
			});

			if (charPos == 0)
			{
				// 小節の頭のDELAYは小節の開始時刻そのものをずらす
				measureStart += duration;
			}
			else
			{
				delaySinceAnchor += duration;
			}
			delaySinceEntry += duration;
		}

		/// <summary>
		/// 小節の終わり
		/// </summary>
		/// <param name="measureRatio">小節の拍子(分子/分母)</param>
		public void EndMeasure(double measureRatio)
		{
			// #DELAYの開始時刻: 太鼓さん次郎では「現在位置 + 5 - (拍子×4÷文字数×1拍の長さ)」
			// (小節の頭のDELAYでは文字数に前の小節のものを使う)
			foreach (var delay in pendingDelays)
			{
				int oneBeat = (int)(60.0 / delay.BPM * 1000.0);
				int step = (int)(measureRatio * 4 / delay.Chars * oneBeat);
				delay.Output?.Add((delay.BaseMillisec + 5 - step, delay.Duration));
			}
			pendingDelays.Clear();

			int end;
			if (lastEntryInMeasure)
			{
				lastEntryRemainder = Truncate(ExactMicrosec - lastEntryExact);
				lastEntryMeasureEndExact = ExactMicrosec;
				end = lastEntry + lastEntryRemainder + delaySinceEntry;
			}
			else
			{
				end = lastEntry + lastEntryRemainder + Truncate(ExactMicrosec - lastEntryMeasureEndExact) + delaySinceEntry;
			}

			measureStart = Math.Max(0, end);
			delaySinceAnchor = 0;
			lastEntryInMeasure = false;
			prevMeasureChars = currentMeasureChars;
		}

		/// <summary>
		/// 複製(譜面分岐の開始時点の状態を保存するため)
		/// </summary>
		public TaikojiroTime Clone()
		{
			var clone = (TaikojiroTime)MemberwiseClone();
			clone.pendingDelays = new List<PendingDelay>(pendingDelays);
			return clone;
		}
	}
}
