using System;
using System.Diagnostics;
using Tatelier.Score.Component.NoteSystem;

namespace Tatelier.Score.Play.Chart
{
    [DebuggerDisplay("time:[{StartMillisec} to {FinishMillisec}]ms, point:[{StartPoint} to {FinishPoint}]px")]
	public class HBScrollDrawDataItem
	{
		/// <summary>
		/// 開始時間(ms)
		/// </summary>
		public int StartMillisec;

		/// <summary>
		/// 開始座標
		/// </summary>
		public double StartPoint;

		/// <summary>
		/// 終了時間
		/// </summary>
		public int FinishMillisec;

		/// <summary>
		/// 終了座標
		/// </summary>
		public double FinishPoint;

		public bool IsDelay = false;

		/// <summary>
		/// 指定した時間範囲がこの区間に収まるかどうか
		/// </summary>
		/// <remarks>
		/// マイナスBPMによる演出(HBSCROLL座標が一時的に巻き戻る)では、この区間自体の
		/// StartMillisecがFinishMillisecより後になることがある。前後関係を決め打ちせず
		/// min/maxで正規化して判定することで、そのような区間でも(完全ではないが)
		/// 検索が破綻しない=どの区間にも一致せず処理落ちする、という状態を避ける。
		/// </remarks>
		public bool IsApplicable(int startMillisec, int finishMillisec)
		{
			int lo = Math.Min(StartMillisec, FinishMillisec);
			int hi = Math.Max(StartMillisec, FinishMillisec);
			return lo <= startMillisec && finishMillisec < hi;
		}

		public bool IsApplicable(int millisec)
		{
			return IsApplicable(millisec, millisec);
		}
		public bool IsApplicable(INote note)
		{
			return IsApplicable(note.StartMillisec, note.FinishMillisec);
		}

		public bool IsApplicable(IMeasureLine line)
		{
			return IsApplicable(line.StartMillisec, line.FinishMillisec);
		}

		public double GetHBScrollPivotX(double per)
        {
			return StartPoint + (FinishPoint - StartPoint) * per;
		}

		/// <summary>
		/// 区間内の経過割合を求める
		/// </summary>
		public double GetElapsedRate(int nowMillisec)
        {
			int duration = FinishMillisec - StartMillisec;

			// マイナスBPM・マイナス小節・マイナスDELAYを組み合わせた譜面では、
			// StartMillisecとFinishMillisecが一致する幅ゼロの区間が生じることがある。
			// そのまま除算するとInfinity/NaNになり、これがノーツ・連打のHBSCROLL座標
			// 計算に伝播して、画面いっぱいに引き伸ばされて描画されたり(Infinity)、
			// 座標比較が常にfalseになって描画されなくなったり(NaN)する不具合の原因になる。
			// 区間の先頭にいるものとして扱うことでこれを回避する。
			if (duration == 0)
			{
				return 0.0;
			}

			return (double)(nowMillisec - StartMillisec) / duration;
		}

		public HBScrollDrawDataItem()
		{

		}
	}
}
