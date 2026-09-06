using System;
using Tatelier.Score.Play.Chart;

namespace Tatelier.Mini.Play
{
	/// <summary>
	/// HBSCROLL(#HBSCROLL)譜面でIssue #18として報告された描画不具合への補正をまとめるクラス。
	/// </summary>
	/// <remarks>
	/// マイナスBPM・マイナス小節を交互させる「シェイク」ギミック譜面(例: MARENOL)では、
	/// HBSCROLL座標の区間ごとの積み上げ(Score.SetDrawHBScrollTime)が符号反転を挟む
	/// たびに打ち消し合い、実時間は数百ms離れているのに座標がほぼ一致してしまうことがある。
	/// これにより「連打の胴体が伸びて見えない」(<see cref="RescueRollEndX"/>で対処)、
	/// 「カメラ位置がシェイク区間の外側へ大きく外挿される」(<see cref="CanUseNoteOwnInterval"/>で対処)
	/// という2つの症状が生じる。OpenTatelier本体での調査・修正(Issue #18)をそのまま移植した。
	///
	/// HBScrollScoreRenderer.cs本体には呼び出し1行だけを追加し、実際の補正ロジックは
	/// このファイルにまとめている(HBSCROLL関連の追加修正だと分かりやすくし、
	/// 必要なときに切り分け・差し戻ししやすくするため)。
	/// </remarks>
	internal static class HBScrollIssue18Fix
	{
		/// <summary>
		/// この幅(px)未満の連打胴体は「不自然に潰れている」とみなし、救済処理の対象にする。
		/// </summary>
		public const float SuspiciouslyNarrowRollWidthPx = 5.0f;

		/// <summary>
		/// 開始・終端の実時間差がこの値(ms)未満の連打は救済の対象外にする。
		/// これほど短い連打は元々人間の目には一瞬にしか見えず実害が小さい一方、
		/// シェイク演出由来の「実時間差が数msしかない」区間は候補値自体が
		/// 不安定になりやすいため。
		/// </summary>
		public const double MinRollDurationForRescueMs = 50.0;

		/// <summary>
		/// 連打(Roll/RollBig)終端ノーツの画面X座標を、胴体幅が不自然に狭い場合だけ
		/// 頭(PrevNote)からの相対オフセットとして求め直す。
		/// </summary>
		/// <param name="prevX">連打の頭(PrevNote)の、その瞬間の画面X座標</param>
		/// <param name="x">通常計算による連打終端の画面X座標</param>
		/// <param name="prevItem">連打の頭が属するHBSCROLL区間</param>
		/// <param name="rollDurationMs">連打の頭から終端までの実時間差(ms)</param>
		/// <param name="prevScrollSpeedValue">連打の頭のスクロール速度係数</param>
		/// <param name="playOptionScrollSpeed">設定部のスクロールスピード</param>
		/// <returns>救済後の画面X座標(救済不要な場合はxをそのまま返す)</returns>
		public static float RescueRollEndX(
			float prevX,
			float x,
			HBScrollDrawDataItem prevItem,
			double rollDurationMs,
			double prevScrollSpeedValue,
			double playOptionScrollSpeed)
		{
			if (Math.Abs(x - prevX) >= SuspiciouslyNarrowRollWidthPx || prevItem == null)
			{
				return x;
			}

			double itemDurationMs = prevItem.FinishMillisec - prevItem.StartMillisec;
			if (Math.Abs(itemDurationMs) <= 0.001 || Math.Abs(rollDurationMs) < MinRollDurationForRescueMs)
			{
				return x;
			}

			double localPxPerMs = (prevItem.FinishPoint - prevItem.StartPoint) / itemDurationMs;
			float candidateX = prevX + (float)(localPxPerMs * rollDurationMs * prevScrollSpeedValue * playOptionScrollSpeed);

			return Math.Abs(candidateX - prevX) >= SuspiciouslyNarrowRollWidthPx ? candidateX : x;
		}

		/// <summary>
		/// 連打本体を描画してよいかどうかを判定する。
		/// </summary>
		/// <remarks>
		/// マイナスBPM/マイナス小節/マイナスDELAYを多用するような譜面だと、prevX・x
		/// のHBSCROLL位置計算が破綻して画面外はるか遠くの値になることがあり、
		/// その場合これまでは画面いっぱいに帯が引き伸ばされて描画されてしまって
		/// いた。通常のスクロールイン/アウトは妨げないよう、区間が描画範囲と
		/// 重なっているかで判定しつつ、明らかに異常な幅(描画範囲の4倍以上)に
		/// なっている場合だけ描画をスキップする。
		/// </remarks>
		/// <param name="prevX">連打の頭の画面X座標</param>
		/// <param name="x">連打の終端の画面X座標(RescueRollEndX適用後)</param>
		/// <param name="finishDrawPointX">描画範囲の終了座標</param>
		/// <param name="startDrawPointX">描画範囲の開始座標</param>
		/// <returns>描画してよければtrue</returns>
		public static bool ShouldDrawRollBody(float prevX, float x, float finishDrawPointX, float startDrawPointX)
		{
			float rollLeft = Math.Min(prevX, x);
			float rollRight = Math.Max(prevX, x);
			float maxSaneWidth = (startDrawPointX - finishDrawPointX) * 4;

			return finishDrawPointX < rollRight
				&& rollLeft < startDrawPointX
				&& (rollRight - rollLeft) < maxSaneWidth;
		}

		/// <summary>
		/// フレーム単位のカメラ基準区間(firstDrawDataItem)を、音符自身の区間
		/// (note.HBScrollDrawDataItem)へ差し替えてよいかどうかを判定する。
		/// </summary>
		/// <remarks>
		/// シェイク演出のように区間が極端に細かく(1ms未満まで)分割されている箇所では、
		/// 「音符の時刻がfirstDrawDataItemの範囲内」という条件だけで差し替えると、
		/// 差し替え先の区間(音符自身のごく短い区間)がnowTime自体を全く含んでいない
		/// ことがあり、その場合GetElapsedRate(nowTime)が[0,1]を大きく超えた値を返して
		/// カメラ位置が不自然に外挿される。差し替え先が本当にnowTimeを含む場合だけ許可する。
		/// </remarks>
		public static bool CanUseNoteOwnInterval(HBScrollDrawDataItem noteOwnItem, int nowTime)
		{
			return noteOwnItem != null && noteOwnItem.IsApplicable(nowTime);
		}
	}
}
