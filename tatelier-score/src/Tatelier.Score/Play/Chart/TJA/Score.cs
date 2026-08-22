using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tatelier.Score.Component;
using Tatelier.Score.Component.NoteSystem;

namespace Tatelier.Score.Play.Chart.TJA
{
    using ScoreParserFuncMap = Dictionary<string, Func<NotePivotInfo, string[], int>>;

    /// <summary>
    /// 譜面情報
    /// </summary>
    public class ScoreInfo
	{
		public const string VERSION_TATELIER_V1 = "Tatelier.v1";

		/// <summary>
		/// コース名
		/// </summary>
		public string CourseName;

		/// <summary>
		/// 初期BPM
		/// </summary>
		public double StartBPM;

		/// <summary>
		/// 音源オフセット
		/// </summary>
		public double OffsetMillisec;

		/// <summary>
		/// HBSCROLL譜面かどうか
		/// </summary>
		public bool HasHBScroll = false;

		/// <summary>
		/// 譜面バージョン
		/// </summary>
		public string Version = null;

		/// <summary>
		/// 風船打数リスト
		/// </summary>
		public int[] BalloonCountList;


		public bool IsNoteRandom = false;

		public bool IsNoteInverse = false;

		public int NoteRandomRatio = 30;

		/// <summary>
		/// ランダム時のシード値
		/// nullのときは自動
		/// </summary>
		public int? NoteRandomSeed;
	}

	[DebuggerDisplay("CourseName : {CourseName}")]
	public class Score
	{
		const int SUCCESS = 0;

		/// <summary>
		/// 初期BPM
		/// </summary>
		public double StartBPM = 200;

		/// <summary>
		/// 音源再生Offset
		/// </summary>
		public int OffsetMillisec = 0;

		/// <summary>
		/// コース名
		/// </summary>
		public string CourseName = "";

		/// <summary>
		/// スコア初期値
		/// </summary>
		public int ScoreInitPoint = 100;

		/// <summary>
		/// スコア加算値
		/// </summary>
		public int ScoreDiffPoint = 100;

		/// <summary>
		/// 風船打数リスト
		/// </summary>
		public int[] BalloonCountList;

		/// <summary>
		/// すべての音符
		/// </summary>
		public List<INote> Notes = new List<INote>();

		public IEnumerable<INote> BalloonNotes
        {
            get
			{
				return Notes.Where(v => v.NoteType == NoteType.Balloon);
            }
        }

		/// <summary>
		/// すべての小節
		/// </summary>
		public List<IMeasureLine> Measures = new List<IMeasureLine>();

		/// <summary>
		/// 分岐毎の譜面管理
		/// </summary>
		public BranchScoreControl BranchScoreControl = new BranchScoreControl();

		/// <summary>
		/// 分岐演奏情報リスト
		/// </summary>
		public List<BranchPlayInfo> BranchPlayInfoList = new List<BranchPlayInfo>();

		/// <summary>
		/// 時間ごと章リスト
		/// </summary>
		public List<int> SectionList = new List<int>();

		/// <summary>
		/// GOGOリスト
		/// </summary>
		public LinkedList<GogoItem> GogoList = new LinkedList<GogoItem>();

		/// <summary>
		/// 分岐有無
		/// </summary>
		public bool HasBranch = false;

		/// <summary>
		/// 譜面種別
		/// </summary>
		public ScoreType ScoreType = ScoreType.Normal;

		/// <summary>
		/// 音符の最大数を取得する。
		/// ※ドンとカツのみで計算
		/// </summary>
		public int MaxNoteCount
		{
			get
			{
				var array = new Dictionary<int, BranchScore>();

				var normal = BranchScoreControl.NormalScoreList;
				var expert = BranchScoreControl.ExpertScoreList;
				var master = BranchScoreControl.MasterScoreList;
				var common = BranchScoreControl.CommonScoreList;

				int sum = common.Select(v => v.Value.GetDonKatSum()).Sum();

				foreach (var item in normal)
				{
					array[item.Value.StartMillisec] = item.Value;
				}
				foreach (var item in expert)
				{
					if (!array.TryGetValue(item.Value.StartMillisec, out var v)
						|| v.GetDonKatSum() < item.Value.GetDonKatSum())
					{
						array[item.Value.StartMillisec] = item.Value;
					}
				}

				foreach (var item in master)
				{
					if (!array.TryGetValue(item.Value.StartMillisec, out var v)
						|| v.GetDonKatSum() < item.Value.GetDonKatSum())
					{
						array[item.Value.StartMillisec] = item.Value;
					}
				}

				sum += array.Sum(v => v.Value.GetDonKatSum());

				return sum;
			}
		}

		#region プライベートメソッド

		#region 小節線
		int SetBARLINEOFF(NotePivotInfo info, string[] args)
		{
			info.BarLineState = 0;
			return SUCCESS;
		}
		int SetBARLINEON(NotePivotInfo info, string[] args)
		{
			info.BarLineState = 1;
			return SUCCESS;
		}
		#endregion

		#region GOGO
		int SetGOGOSTART(NotePivotInfo info, string[] args)
		{
			GogoList.AddLast(new GogoItem()
			{
				Gogo = true,
				StartTime = (int)info.PivotMillisec
			});
			return SUCCESS;
		}
		int SetGOGOEND(NotePivotInfo info, string[] args)
		{
			GogoList.AddLast(new GogoItem()
			{
				Gogo = false,
				StartTime = (int)info.PivotMillisec
			});
			return SUCCESS;
		}
		#endregion

		int SetBPMCHANGE(NotePivotInfo info, string[] args)
		{
			const int ERROR_ARGS = -1;
			const int ERROR_PARSE = -2;

			if (args.Length > 0)
			{
				if (!double.TryParse(args[0], out var bpm))
				{
					return ERROR_PARSE;
				}

				return SetBPMCHANGE(info, bpm);
			}
			else
			{
				return ERROR_ARGS;
			}
		}
		int SetBPMCHANGE(NotePivotInfo info, double bpm)
		{
			if (info.BPMInfo.StartMillisec == -1000
				&& info.PivotMillisec == 0)
			{
				info.BPMInfo.Set(info.PivotMillisec, bpm);
				info.CurrentBranchScore.BPMList.First().Set(info.PivotMillisec, bpm);
			}
			else
			{
				info.BPMInfo = new BPM(info.PivotMillisec, bpm);
				info.CurrentBranchScore.BPMList.LastOrDefault()?.SetEndMillisec(info.PivotMillisec);
				info.CurrentBranchScore.BPMList.Add(info.BPMInfo);
			}

			return SUCCESS;
		}

		int SetMEASURE(NotePivotInfo info, string[] args)
		{
			const int ERROR_ARGS = -1;
			const int ERROR_PARSE_MEASURE_UPPER = -3;
			const int ERROR_PARSE_MEASURE_LOWER = -4;

			info.PivotMicrosec = (long)(info.PivotMicrosec / 1000) * 1000;

			if (args.Length > 0)
			{
				var split = args[0].Split('/');

				if(!double.TryParse(split[0], out var upper))
				{
					return ERROR_PARSE_MEASURE_UPPER;
				}

				if (!double.TryParse(split[1], out var lower))
				{
					return ERROR_PARSE_MEASURE_LOWER;
				}


				info.MeasureInfo = new Measure(info.PivotMillisec, upper, lower);
				info.CurrentBranchScore.MeasureList.LastOrDefault()?.SetEndMillisec(info.PivotMillisec);
				info.CurrentBranchScore.MeasureList.Add(info.MeasureInfo);

				return SUCCESS;
			}
			else
			{
				return ERROR_ARGS;
			}
		}
		int SetSCROLL(NotePivotInfo info, string[] args)
		{
			const int ERROR_ARGS = -1;
			const int ERROR_PARSE = -2;

			if (args.Length > 0)
			{
				if (!double.TryParse(args[0], out var scrollSpeed))
				{
					return ERROR_PARSE;
				}

				info.ScrollSpeedInfo = new ScrollSpeed(info.PivotMillisec, scrollSpeed);
				info.CurrentBranchScore.ScrollSpeedList.LastOrDefault()?.SetEndMillisec(info.PivotMillisec);
				info.CurrentBranchScore.ScrollSpeedList.Add(info.ScrollSpeedInfo);

				return SUCCESS;
			}
			else
			{
				return ERROR_ARGS;
			}

		}

		int SetDELAY(NotePivotInfo info, string[] args)
		{
			if (args.Length > 0)
			{
				if (!double.TryParse(args[0], out var sec))
				{
					return -2;
				}
                if (sec > 0)
                {
					var lastBpm = info.CurrentBranchScore.BPMList.LastOrDefault()?.Value ?? 0;

					var pivotMicrosec = info.PivotMicrosec;

					info.PivotMicrosec = info.PrevPivotMicrosec;

					SetBPMCHANGE(info, 0);
					info.CurrentBranchScore.BPMList.LastOrDefault().IsDelay = true;

					var diff = new decimal(sec) * 1000000m;

					info.PivotMicrosec += diff;

					SetBPMCHANGE(info, lastBpm);

					info.PivotMicrosec = pivotMicrosec + diff;
				}
				else
				{
					info.PivotMicrosec += new decimal(sec) * 1000000m;
				}
			}
			else
			{
				return -1;
			}
			return SUCCESS;
		}

		#region 分岐情報
		int SetBRANCHSTART(NotePivotInfo info, string[] args)
		{
			const int ERROR_EXPERT_VALUE = -1;
			const int ERROR_MASTER_VALUE = -2;

			var playInfo = new BranchPlayInfo(info.PivotMillisec, info.BranchPlayInfo);

			string line = string.Join(",", args);

			switch (line[0])
			{
				case 'p':
				case 'r':
					playInfo.Type = line[0];
					break;
				default:
					playInfo.Type = 'p';
					break;
			}

			string valText = line.Substring(2);
			var valSplit = valText.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

			if(!float.TryParse(valSplit[0], out var expertValue))
			{
				return ERROR_EXPERT_VALUE;
			}
			playInfo.ExpertValue = expertValue;

			if(!float.TryParse(valSplit[1], out var masterValue))
			{
				return ERROR_MASTER_VALUE;
			}
			playInfo.MasterValue = masterValue;

			BranchPlayInfoList.Add(playInfo);
			info.BranchPivot = info.ShallowCopy();

			BranchScoreControl.OneBeforeMeasureTime.Add(info.CurrentBranchScore.Measures.Reverse<IMeasureLine>().FirstOrDefault()?.StartMillisec ?? 0);

			HasBranch = true;

			BranchScoreControl.BranchStartTimeList.Add((int)info.PivotMillisec);
			BranchScoreControl.BranchTypeList[(int)info.PivotMillisec] = BranchType.Normal;

			return 0;
		}
		int SetBRANCHEND(NotePivotInfo info, string[] args)
		{
			info.BranchType = BranchType.Common;
			info.CurrentBranchScore = new BranchScore(info);
			BranchScoreControl.CommonScoreList[(int)info.PivotMillisec] = info.CurrentBranchScore;

			return 0;
		}
		int SetSECTION(NotePivotInfo info, string[] args)
		{
			SectionList.Add((int)info.PivotMillisec);

			return 0;
		}

		int SetBranchScore(NotePivotInfo info, string[] args, BranchType type)
		{
			if(info.BranchPivot == null)
            {
				throw new TJAParseException("#BRANCHSTART\nを宣言する前に\n#N, #E, #M\nを宣言しないでください。\n\n");
            }
			info.PivotMicrosec = info.BranchPivot.PivotMicrosec;

			info.PrevNote = info.BranchPivot.PrevNote;
			info.PrevMeasureLine = info.BranchPivot.PrevMeasureLine;

			info.BPMInfo = info.BranchPivot.BPMInfo;
			info.MeasureInfo = info.BranchPivot.MeasureInfo;
			info.BranchType = type;
			info.BarLineState = info.BranchPivot.BarLineState;
			info.CurrentBranchScore = new BranchScore(info);

			switch (type)
			{
				case BranchType.Normal:
					BranchScoreControl.NormalScoreList[(int)info.PivotMillisec] = info.CurrentBranchScore;
					break;
				case BranchType.Expert:
					BranchScoreControl.ExpertScoreList[(int)info.PivotMillisec] = info.CurrentBranchScore;
					break;
				case BranchType.Master:
					BranchScoreControl.MasterScoreList[(int)info.PivotMillisec] = info.CurrentBranchScore;
					break;
				default:
					// ERROR:
					//Logger.Singleton.Trace("ERROR");
					break;
			}

			return 0;
		}
		int SetLEVELHOLD(NotePivotInfo info, string[] args)
		{
			BranchScoreControl.LevelHoldList.Add((info.BranchType, (int)info.PivotMillisec));

			return 0;
		}
		int SetN(NotePivotInfo info, string[] args)
		{
			return SetBranchScore(info, args, BranchType.Normal);
		}
		int SetE(NotePivotInfo info, string[] args)
		{
			return SetBranchScore(info, args, BranchType.Expert);
		}
		int SetM(NotePivotInfo info, string[] args)
		{
			return SetBranchScore(info, args, BranchType.Master);
		}
		#endregion

		#endregion

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="scoreDataText">譜面データテキスト</param>
		/// <param name="info">コンストラクタ情報</param>
		public Score(StringBuilder scoreDataText, ScoreInfo info)
		{
			#region メソッドマップ
			var sharpMethodMap = new ScoreParserFuncMap()
			{
				{ "MEASURE", SetMEASURE },
				{ "BPMCHANGE", SetBPMCHANGE },
				{ "SCROLL", SetSCROLL },
				{ "DELAY", SetDELAY },
				{ "BARLINEOFF", SetBARLINEOFF },
				{ "BARLINEON", SetBARLINEON },

				// ゴーゴー関連
				{ "GOGOSTART", SetGOGOSTART },
				{ "GOGOEND", SetGOGOEND },

				// 分岐関係
				{ "BRANCHSTART",  SetBRANCHSTART },
				{ "BRANCHEND",  SetBRANCHEND  },
				{ "LEVELHOLD", SetLEVELHOLD },
				{ "SECTION", SetSECTION },
				{ "N",  SetN  },
				{ "E",  SetE  },
				{ "M",  SetM  },
				//{ "SECTION", NoteInfo.TryBARLINEON },
				//{ "LEVELHOLD", NoteInfo.TryBARLINEON }
			};
			#endregion

			BalloonCountList = info.BalloonCountList.ToArray();

			OffsetMillisec = (int)(info.OffsetMillisec * 1000);

			ScoreType = info.HasHBScroll ? ScoreType.HBScroll : ScoreType.Normal;

			CourseName = info.CourseName;

			var notePivotInfo = new NotePivotInfo();

			notePivotInfo.IsNoteRandom = info.IsNoteRandom;
			if (notePivotInfo.IsNoteRandom)
			{
				notePivotInfo.Random = new Random((int)info.NoteRandomSeed);
			}
			notePivotInfo.RandomRatio = info.NoteRandomRatio;
			notePivotInfo.IsInverse = info.IsNoteInverse;

			notePivotInfo.PivotMicrosec = -1000000;
			notePivotInfo.BPMInfo = new BPM(notePivotInfo.PivotMillisec, info.StartBPM);
			notePivotInfo.BalloonValueList = new List<int>(info.BalloonCountList);

			notePivotInfo.CurrentBranchScore = new BranchScore(notePivotInfo);
			notePivotInfo.CurrentBranchScore.BPMList[0] = notePivotInfo.BPMInfo;
			notePivotInfo.CurrentBranchScore.MeasureList[0] = notePivotInfo.MeasureInfo;
			notePivotInfo.CurrentBranchScore.ScrollSpeedList[0] = notePivotInfo.ScrollSpeedInfo;

			notePivotInfo.PivotMicrosec = 0;
			BranchScoreControl.CommonScoreList[0] = notePivotInfo.CurrentBranchScore;

			bool isIgnore = false;
			bool isSharpLine = false;

			// 1小節文の文字列
			var measureSB = new StringBuilder();

			for (int i = 0; i < scoreDataText.Length; i++)
			{
				char c = scoreDataText[i];

				if (isIgnore)
				{
					if (c == '\n')
					{
						isIgnore = false;
						isSharpLine = false;
					}
					continue;
				}


				switch(c)
				{
					case '\r': // 不必要な文字のため無視
					case '\t': // 不必要な文字のため無視
						break;
					case ' ':
						if (isSharpLine)
						{
							measureSB.Append(c);
						}
						break;
					case '\n':
						{
							// 各フラグを折る
							isIgnore = false;
							isSharpLine = false;
							measureSB.Append(c);
						}
						break;
					case '/':
						{
							// "//"以降はコメントとして無視する
							if (i + 1 < scoreDataText.Length
								&& scoreDataText[i + 1] == '/')
							{
								isIgnore = true;
								i++;
							}
							else
							{
								measureSB.Append(c);
							}
						}
						break;
					case '#':
						{
							// #
							isSharpLine = true;
							measureSB.Append(c);
						}
						break;
					case ',':
						// 1小節取得完了
						{
							if (isSharpLine)
							{
								measureSB.Append(c);
							}
							else
							{
								CreateMeasure(notePivotInfo, sharpMethodMap, measureSB);
								measureSB.Clear();
							}
						}
						break;
					default:
						{
							measureSB.Append(c);
						}
						break;
				}
			}

			CreateMeasure(notePivotInfo, sharpMethodMap, measureSB);

            switch (notePivotInfo.PrevNote.NoteType)
            {
				case NoteType.Roll:
				case NoteType.RollBig:
				case NoteType.Balloon:
					{
						var note = new Note(NoteType.End, notePivotInfo);

						notePivotInfo.PrevNote = note;
						notePivotInfo.CurrentBranchScore.AddNote(note);
					}
					break;
            }

			BalloonCountList = notePivotInfo.BalloonValueList.ToArray();
			BranchScoreControl.Build();
			Notes = new List<INote>(BranchScoreControl.EnumratesAllNote());
			Measures = new List<IMeasureLine>(BranchScoreControl.EnumratesAllMeasureLine());
		}

		/// <summary>
		/// HBSCROLLの描画情報を設定
		/// </summary>
		/// <param name="areaWidth">音符描画領域の幅</param>
		public void SetDrawHBScrollTime(float areaWidth)
		{
			foreach (var branchScore in BranchScoreControl.GetAllBranchScoreList().Select(v => v.BranchScore))
			{
				branchScore.HBScrollDrawDataControl.Clear();

				// 1周目: 全BPM区間分のdataItemを先に構築する(この時点ではまだ
				// ノーツ・小節線への割り当ては行わない。2周目で全区間が出揃った
				// 状態から検索できるようにするため)。
				var pending = new List<(BPM bpmInfo, HBScrollDrawDataItem dataItem)>();
				foreach (var bpmInfo in branchScore.BPMList)
				{
					var dataItem = new HBScrollDrawDataItem()
					{
						StartMillisec = bpmInfo.StartMillisec,
						FinishMillisec = bpmInfo.FinishMillisec,
						IsDelay = bpmInfo.IsDelay,
					};

					dataItem.StartPoint = branchScore.HBScrollDrawDataControl.ItemList?.LastOrDefault()?.FinishPoint ?? bpmInfo.GetDivision(dataItem.StartMillisec) * areaWidth;
					dataItem.FinishPoint = dataItem.StartPoint + bpmInfo.GetDivision(dataItem.FinishMillisec - dataItem.StartMillisec) * areaWidth;

					branchScore.HBScrollDrawDataControl.Add(dataItem);
					pending.Add((bpmInfo, dataItem));
				}

				// 2周目: 各ノーツ・小節線の座標を計算する。
				// マイナスBPM/マイナス小節が絡む譜面では、音符が本来属するBPM区間
				// (dataItem)の時間範囲に、その音符自身のStartMillisecが実際には
				// 含まれていないケースがある。そのまま計算すると0～1の範囲を外れた
				// perが0/1にクランプされ、本来離れているはずの音符同士が同じ座標に
				// 潰れてしまう(例: 連打の開始・終了ノーツが同座標になり胴体が
				// 伸びなくなる)。IsApplicable()（マイナスBPM区間でも安全に判定できる、
				// 描画側で既に使われているのと同じ判定）で実際に該当する区間を
				// 探し直すことでこれを回避する。見つからない場合のみ、従来通り
				// 割り当て区間をそのまま使う(クランプはフォールバックとして残す)。
				foreach (var (bpmInfo, dataItem) in pending)
				{
					foreach (var note in bpmInfo.NoteList)
					{
						var actualItem = dataItem.IsApplicable(note.StartMillisec)
							? dataItem
							: branchScore.HBScrollDrawDataControl.ItemList.LastOrDefault(v => v.IsApplicable(note.StartMillisec)) ?? dataItem;

						double per = actualItem.GetElapsedRate(note.StartMillisec);
						per = Math.Max(0, Math.Min(1, per));
						note.HBScrollStartPointX = actualItem.GetHBScrollPivotX(per);
						note.HBScrollDrawDataItem = actualItem;
					}

					foreach (var measure in bpmInfo.MeasureLineList)
					{
						var actualItem = dataItem.IsApplicable(measure.StartMillisec)
							? dataItem
							: branchScore.HBScrollDrawDataControl.ItemList.LastOrDefault(v => v.IsApplicable(measure.StartMillisec)) ?? dataItem;

						double per = actualItem.GetElapsedRate(measure.StartMillisec);
						per = Math.Max(0, Math.Min(1, per));
						measure.HBScrollStartPointX = actualItem.GetHBScrollPivotX(per);
						measure.HBScrollDrawDataItem = actualItem;
					}
				}
			}
		}

		/// <summary>
		/// 譜面描画用データを構築する
		/// </summary>
		/// <param name="oneMeasureWidth">4/4拍子の1小節分を描画するために必要な幅</param>
		/// <param name="startDrawPointX">描画開始座標X</param>
		/// <param name="finishDrawPointX">描画終了座標X</param>
		/// <param name="playOptionScrollSpeed">設定部のスクロールスピード</param>
		/// <param name="hbScrollDensityScale">
		/// HBSCROLL専用の表示密度倍率。通常スクロール(MovementPerMillisec)や
		/// HBSCROLLの他の計算には一切影響しない。1.0で従来通りの見た目。
		/// </param>
		public void BuildScoreRendererData(float oneMeasureWidth, float startDrawPointX, float finishDrawPointX, float playOptionScrollSpeed, float hbScrollDensityScale = 1.0f)
		{
			// 音符の設定
			foreach (var note in Notes)
			{
				note.BuildScoreRendererData(oneMeasureWidth, startDrawPointX, finishDrawPointX, playOptionScrollSpeed);
			}

			// 小節線の設定
			foreach (var measure in Measures)
			{
				measure.BuildScoreRendererData(oneMeasureWidth, startDrawPointX, finishDrawPointX, playOptionScrollSpeed);
			}

			switch (ScoreType)
			{
				case ScoreType.HBScroll:
					{
						SetDrawHBScrollTime(oneMeasureWidth * hbScrollDensityScale);
					}
					break;
			}
		}

		int GetNoteNum(StringBuilder measureSB)
		{
			int result = 0;

			bool isSharpLine = false;

			for(int i = 0; i < measureSB.Length; i++)
			{
				if (!isSharpLine)
				{
					if (measureSB[i] == '#')
					{
						isSharpLine = true;
					}
					else
					{
						if ('0' <= measureSB[i]
							&& measureSB[i] <= '9')
						{
							result++;
						}
					}
				}
				else
				{
					if (measureSB[i] == '\n')
					{
						isSharpLine = false;
					}
					else
					{

					}
				}
			}

			return result;
		}

		void DoSharpMethod(ScoreParserFuncMap funcMap, NotePivotInfo info, StringBuilder name, string[] args)
		{
			if (name.Length > 0)
			{
				if (funcMap.TryGetValue($"{name}", out var func))
				{
					int ret = func(info, args);
					if (ret != 0)
					{
						//Trace($"{name}, ret: {ret}");
					}
				}
				else
				{
					//Logger.Singleton.Trace($"{name} is not undefined.");
				}
			}
		}

		void CreateMeasure(NotePivotInfo notePivotInfo
			, ScoreParserFuncMap shareFuncMap
			, StringBuilder measureSB
			)
		{
			if (measureSB.Length == 0)
			{
				return;
			}

			double pivotStartMillisec = notePivotInfo.PivotMillisec;

			int noteNum = GetNoteNum(measureSB);

			bool isSharpLine = false;
			var sharpLine = new StringBuilder();


			int i;
			for (i = 0; i < measureSB.Length; i++)
			{
				bool isFinish = false;
				switch (measureSB[i])
				{
					case '#':
						{
							isSharpLine = true;
						}
						break;
					case '\n':
						{
							isSharpLine = false;

							{
								var sbSharpOnly = new StringBuilder();
								string[] args = new string[0];
								int sharpIdx;
								for (sharpIdx = 0; sharpIdx < sharpLine.Length; sharpIdx++)
								{
									// 大文字は命令文として取得
									if (char.IsUpper(sharpLine[sharpIdx]))
									{
										sbSharpOnly.Append(sharpLine[sharpIdx]);
									}
									else
									{
										break;
									}
								}
								if (sharpIdx < sharpLine.Length)
								{
									args = sharpLine.ToString(sharpIdx, sharpLine.Length - sharpIdx).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
								}

								DoSharpMethod(shareFuncMap, notePivotInfo, sbSharpOnly, args);
								sharpLine.Clear();
							}
						}
						break;
					default:
						{
							if (isSharpLine)
							{
								sharpLine.Append(measureSB[i]);
								continue;
							}
							else
							{
								isFinish = true;
							}
						}
						break;
				}

				if (isFinish)
				{
					break;
				}
			}

			var measure = new MeasureLine(notePivotInfo);
			notePivotInfo.CurrentBranchScore.AddMeasure(measure);

            if (i == measureSB.Length)
            {
				notePivotInfo.PivotMicrosec += notePivotInfo.MeasureInfo.GetCalc(Const.OneMinuteInMicrosec) / new decimal(notePivotInfo.BPMInfo.Value);
				return;
            }

			for (; i < measureSB.Length; i++)
			{
				switch(measureSB[i])
				{
					case '#':
						{
							isSharpLine = true;
						}
						break;
					case '\n':
						{
							isSharpLine = false;
							{
								var sbSharpOnly = new StringBuilder();
								string[] args = new string[0];
								int sharpIdx;
								for (sharpIdx = 0; sharpIdx < sharpLine.Length; sharpIdx++)
								{
									// 大文字は命令文として取得
									if (char.IsUpper(sharpLine[sharpIdx]))
									{
										sbSharpOnly.Append(sharpLine[sharpIdx]);
									}
									else
									{
										break;
									}
								}
								if (sharpIdx < sharpLine.Length)
								{
									args = sharpLine.ToString(sharpIdx, sharpLine.Length - sharpIdx).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
								}

								DoSharpMethod(shareFuncMap, notePivotInfo, sbSharpOnly, args);
								sharpLine.Clear();
							}
						}
						break;
					default:
						{
							if (isSharpLine)
							{
								sharpLine.Append(measureSB[i]);
								continue;
							}

							if (!NoteTypeChar.IsNoteType(measureSB[i]))
							{
								continue;
							}

							switch (measureSB[i])
							{
								case NoteTypeChar.None:
									{

									}
									break;
								case NoteTypeChar.Don:
								case NoteTypeChar.Kat:
								case NoteTypeChar.DonBig:
								case NoteTypeChar.KatBig:
									{
										var note = new Note(NoteTypeChar.GetNoteType(measureSB[i]), notePivotInfo);

										notePivotInfo.PrevNote = note;
										notePivotInfo.CurrentBranchScore.AddNote(note);
									}
									break;
								case NoteTypeChar.Roll:
								case NoteTypeChar.RollBig:
								case NoteTypeChar.Balloon:
									{
										var noteType = (NoteType)measureSB[i];

										if (notePivotInfo.PrevNote?.NoteType != noteType)
										{
											var note = new Note(noteType, notePivotInfo);

											notePivotInfo.PrevNote = note;
											notePivotInfo.CurrentBranchScore.AddNote(note);

											if (note.NoteType == NoteType.Balloon)
											{
												int cnt = 5;
												if (notePivotInfo.NowBalloonIndex < notePivotInfo.BalloonValueList.Count)
												{
													cnt = notePivotInfo.BalloonValueList[notePivotInfo.NowBalloonIndex];
												}
                                                else
                                                {
													notePivotInfo.BalloonValueList.Add(cnt);
                                                }
												note.SpecialData = new BalloonData()
												{
													Count = cnt,
													Index = notePivotInfo.NowBalloonIndex
												};
												notePivotInfo.NowBalloonIndex++;
											}
										}

									}
									break;
								case NoteTypeChar.End:
									{
										var note = new Note((NoteType)measureSB[i], notePivotInfo);

										notePivotInfo.PrevNote = note;
										notePivotInfo.CurrentBranchScore.AddNote(note);
									}
									break;
							}
							notePivotInfo.PivotMicrosec += notePivotInfo.MeasureInfo.GetCalc(Const.OneMinuteInMicrosec) / (new decimal(notePivotInfo.BPMInfo.Value) * new decimal(noteNum));
						}
						break;
				}
			}
		}
	}
}
