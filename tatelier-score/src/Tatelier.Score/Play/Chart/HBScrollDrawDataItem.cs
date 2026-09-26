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
		/// HBScrollDrawDataControl.ItemList内でのインデックス
		/// </summary>
		public int Index;

		/// <summary>
		/// この区間のBPM
		/// </summary>
		public double BPM;

		/// <summary>
		/// この区間で1ms進むごとに進む座標(BPM×音符描画領域の幅/240000)
		/// </summary>
		public double PointPerMillisec;

		/// <summary>
		/// 指定時刻における、この区間を基準としたHBSCROLL座標
		/// </summary>
		public double GetPointAt(double millisec)
		{
			return StartPoint + (millisec - StartMillisec) * PointPerMillisec;
		}

		public bool IsApplicable(int startMillisec, int finishMillisec)
		{
			return (StartMillisec <= startMillisec && finishMillisec < FinishMillisec);
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

		public double GetElapsedRate(int nowMillisec)
        {
			return (double)(nowMillisec - StartMillisec) / (FinishMillisec - StartMillisec);
		}

		public HBScrollDrawDataItem()
		{

		}
	}
}
