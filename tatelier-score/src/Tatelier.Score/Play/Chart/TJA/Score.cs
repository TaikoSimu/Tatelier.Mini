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
		/// BMSCROLL譜面かどうか
		/// </summary>
		public bool HasBMScroll = false;

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
		/// #LYRICで指定された歌詞リスト
		/// </summary>
		public LinkedList<LyricItem> LyricList = new LinkedList<LyricItem>();

		/// <summary>
		/// 分岐有無
		/// </summary>
		public bool HasBranch = false;

		/// <summary>
		/// 譜面種別
		/// </summary>
		public ScoreType ScoreType = ScoreType.Normal;

		/// <summary>
		/// #BMSCROLL譜面かどうか(HBSCROLLとして描画し、#SCROLLは無視する)
		/// </summary>
		public bool IsBMScroll = false;

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
				StartTime = info.NoteMillisec
			});
			return SUCCESS;
		}
		int SetGOGOEND(NotePivotInfo info, string[] args)
		{
			GogoList.AddLast(new GogoItem()
			{
				Gogo = false,
				StartTime = info.NoteMillisec
			});
			return SUCCESS;
		}
		#endregion

		#region SENOTECHANGE
		/// <summary>
		/// #SENOTECHANGEに渡された番号を、この譜面ライブラリが扱える音符文字種別へ変換する
		/// </summary>
		/// <remarks>
		/// TJAP3系シミュレータの仕様(1始まり)に合わせている:
		/// 1:ドン, 2:ド, 3:コ, 4:カッ, 5:カ, 6:ドン(大), 7:カッ(大), 8:連打, 11:連打(大), 12:ふうせん
		/// このプロジェクトの音符文字画像には「大」専用のグラフィックが無いため、
		/// 6と7はそれぞれ通常のドン/カッ表記へ縮退させる。
		/// 9(ー) / 10(ーっ!!)に相当する文字種は用意されていないため未対応。
		/// </remarks>
		static NoteTextType? GetSenoteChangeNoteTextType(int value)
		{
			switch (value)
			{
				case 1: return NoteTextType.Don;
				case 2: return NoteTextType.Do;
				case 3: return NoteTextType.Ko;
				case 4: return NoteTextType.Katt;
				case 5: return NoteTextType.Kat;
				case 6: return NoteTextType.Don;
				case 7: return NoteTextType.Katt;
				case 8: return NoteTextType.Renda;
				case 11: return NoteTextType.Renda;
				case 12: return NoteTextType.GekiRenda;
				default: return null;
			}
		}

		int SetSENOTECHANGE(NotePivotInfo info, string[] args)
		{
			const int ERROR_ARGS = -1;
			const int ERROR_PARSE = -2;
			const int ERROR_RANGE = -3;

			if (args.Length == 0)
			{
				return ERROR_ARGS;
			}

			if (!int.TryParse(args[0], out var value))
			{
				return ERROR_PARSE;
			}

			var noteTextType = GetSenoteChangeNoteTextType(value);
			if (noteTextType == null)
			{
				return ERROR_RANGE;
			}

			// 直後に生成される1音符(ドン/カッ)にのみ適用される予約値として保持する
			info.PendingNoteTextTypeOverride = noteTextType;

			return SUCCESS;
		}
		#endregion

		#region LYRIC
		/// <summary>
		/// #LYRIC &lt;歌詞テキスト&gt;
		/// TJAP3系シミュレータと同様、その時点(PivotMillisec)から表示する歌詞テキストを登録する。
		/// 引数無し(#LYRICのみ)の場合は、歌詞表示を空欄にする命令として扱う(歌詞の削除・切り替わり目に使われる)
		/// </summary>
		int SetLYRIC(NotePivotInfo info, string[] args)
		{
			// 引数がスペースを含む歌詞の場合に備え、分割された引数を再結合する
			// (args.Length == 0の場合はstring.Joinが空文字列になり、歌詞を空欄にする)
			LyricList.AddLast(new LyricItem()
			{
				StartTime = (int)info.PivotMillisec,
				Text = string.Join(" ", args),
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

				int ret = SetBPMCHANGE(info, bpm);

				// HBSCROLL用の時刻(太鼓さん次郎と同じ方法)
				int hbScrollMillisec = info.Tj.OnBPMChange();
				info.BPMInfo.HBScrollStartMillisec = hbScrollMillisec;
				info.CurrentBranchScore.BPMList.Last().HBScrollStartMillisec = hbScrollMillisec;

				return ret;
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

				// HBSCROLL用の時刻(太鼓さん次郎と同じ方法)
				info.Tj.OnDelay(sec, info.BPMInfo.Value, info.CurrentBranchScore.HBScrollDelayList);

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
			info.BranchPivot.Tj = info.Tj.Clone();

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
			info.Tj = info.BranchPivot.Tj.Clone();

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

				// 音符文字
				{ "SENOTECHANGE", SetSENOTECHANGE },

				// 歌詞
				{ "LYRIC", SetLYRIC },

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

			// #BMSCROLLは#SCROLLを無視するHBSCROLLとして扱う(太鼓さん次郎の仕様)
			ScoreType = (info.HasHBScroll || info.HasBMScroll) ? ScoreType.HBScroll : ScoreType.Normal;
			IsBMScroll = !info.HasHBScroll && info.HasBMScroll;

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
			notePivotInfo.UseTaikojiroTime = ScoreType == ScoreType.HBScroll;
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
		/// <remarks>
		/// 太鼓さん次郎(ver2.92)の実装に合わせ、BPM区間を譜面全体で1本のリストとして構築する。
		/// 譜面分岐がある場合、太鼓さん次郎は共通部分と最初の分岐(通常は#N)のBPMCHANGEだけを
		/// BPMリストに登録し、他の分岐はそれを共用するため、同じく分岐ごとに1つだけ採用する。
		/// 時刻はHBSCROLL用の時刻(TaikojiroTime参照)を使う(HBSCROLL譜面では音符の判定時刻も同じ)。
		/// 座標計算の詳細はHBScrollDrawDataControlを参照。
		/// </remarks>
		/// <param name="areaWidth">音符描画領域の幅</param>
		public void SetDrawHBScrollTime(float areaWidth)
		{
			// 採用するセクションの並び(セクションの開始時刻順、同時刻は共通部分を先にする)
			var sections = new List<(int Key, int Order, BranchScore Score)>();
			foreach (var item in BranchScoreControl.CommonScoreList)
			{
				sections.Add((item.Key, 0, item.Value));
			}
			var branchKeys = BranchScoreControl.NormalScoreList.Keys
				.Union(BranchScoreControl.ExpertScoreList.Keys)
				.Union(BranchScoreControl.MasterScoreList.Keys);
			foreach (var key in branchKeys)
			{
				if (BranchScoreControl.NormalScoreList.TryGetValue(key, out var score)
					|| BranchScoreControl.ExpertScoreList.TryGetValue(key, out score)
					|| BranchScoreControl.MasterScoreList.TryGetValue(key, out score))
				{
					sections.Add((key, 1, score));
				}
			}
			var orderedSections = sections.OrderBy(v => v.Key).ThenBy(v => v.Order).Select(v => v.Score).ToList();

			var control = new HBScrollDrawDataControl()
			{
				IgnoreScrollSpeed = IsBMScroll,
				AreaWidth = areaWidth,
			};

			// BPM区間
			// ・各セクションの先頭の区間は、セクション開始時に直前のBPMを複製したもの(太鼓さん次郎には
			//   存在しない)なので、譜面の最初のセクション以外では除く。
			// ・#DELAYは「BPM=0の区間」と「元のBPMで再開する区間」の2つとしてBPMListに入っているが、
			//   太鼓さん次郎ではDELAYはBPMCHANGEとは別に管理されているため除く(DELAYは別途リストで持つ)。
			for (int sectionIndex = 0; sectionIndex < orderedSections.Count; sectionIndex++)
			{
				var bpmList = orderedSections[sectionIndex].BPMList;
				for (int i = sectionIndex == 0 ? 0 : 1; i < bpmList.Count; i++)
				{
					var bpmInfo = bpmList[i];

					if (bpmInfo.IsDelay)
					{
						i++;
						continue;
					}

					var prev = control.ItemList.LastOrDefault();
					var dataItem = new HBScrollDrawDataItem()
					{
						Index = control.ItemList.Count,
						StartMillisec = bpmInfo.HBScrollStartMillisec,
						BPM = bpmInfo.Value,
						PointPerMillisec = bpmInfo.GetDivision(areaWidth),
					};

					if (prev != null)
					{
						// 区間の長さは「次の区間の開始時刻 - この区間の開始時刻」(マイナスBPMでは負になり得る)
						prev.FinishMillisec = dataItem.StartMillisec;
						prev.FinishPoint = prev.GetPointAt(dataItem.StartMillisec);
						dataItem.StartPoint = prev.FinishPoint;
					}

					control.Add(dataItem);
				}
			}

			var last = control.ItemList.LastOrDefault();
			if (last != null)
			{
				last.FinishMillisec = int.MaxValue;
				last.FinishPoint = last.GetPointAt(last.FinishMillisec);
			}

			// #DELAY(長さが正のもののみ停止・差し引きの対象)
			foreach (var (startMillisec, duration) in orderedSections.SelectMany(v => v.HBScrollDelayList))
			{
				if (duration <= 0)
				{
					continue;
				}

				// 太鼓さん次郎と同じく、DELAYの間にBPMCHANGEがある場合はDELAYの開始時刻をその時刻へずらし、
				// DELAYの開始時刻以前で最後のBPMCHANGEのBPMを使う
				int start = startMillisec;
				double bpm = control.ItemList.FirstOrDefault()?.BPM ?? 0;
				foreach (var item in control.ItemList)
				{
					if (start <= item.StartMillisec && item.StartMillisec <= start + duration)
					{
						start = item.StartMillisec;
					}
					if (item.StartMillisec <= start)
					{
						bpm = item.BPM;
					}
				}

				control.DelayList.Add(new HBScrollDelay()
				{
					StartMillisec = start,
					Duration = duration,
					BPM = bpm,
				});
			}

			foreach (var branchScore in BranchScoreControl.GetAllBranchScoreList().Select(v => v.BranchScore))
			{
				branchScore.HBScrollDrawDataControl = control;

				foreach (var note in branchScore.Notes)
				{
					var item = control.GetItem(note.HBScrollMillisec);
					if (item == null)
					{
						continue;
					}
					note.HBScrollDrawDataItem = item;
					note.HBScrollStartPointX = item.GetPointAt(note.HBScrollMillisec);
				}

				foreach (var measure in branchScore.Measures)
				{
					var item = control.GetItem(measure.HBScrollMillisec);
					if (item == null)
					{
						continue;
					}
					measure.HBScrollDrawDataItem = item;
					measure.HBScrollStartPointX = item.GetPointAt(measure.HBScrollMillisec);
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

			notePivotInfo.Tj.BeginMeasure(noteNum);

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
				var measureMicrosec = notePivotInfo.MeasureInfo.GetCalc(Const.OneMinuteInMicrosec) / new decimal(notePivotInfo.BPMInfo.Value);
				notePivotInfo.PivotMicrosec += measureMicrosec;
				notePivotInfo.Tj.AdvanceEmptyMeasure(measureMicrosec);
				notePivotInfo.Tj.EndMeasure(GetTaikojiroMeasureRatio(notePivotInfo.MeasureInfo));
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
							var stepMicrosec = notePivotInfo.MeasureInfo.GetCalc(Const.OneMinuteInMicrosec) / (new decimal(notePivotInfo.BPMInfo.Value) * new decimal(noteNum));
							notePivotInfo.PivotMicrosec += stepMicrosec;
							notePivotInfo.Tj.AdvanceChar(stepMicrosec);
						}
						break;
				}
			}

			notePivotInfo.Tj.EndMeasure(GetTaikojiroMeasureRatio(notePivotInfo.MeasureInfo));
		}

		/// <summary>
		/// 太鼓さん次郎での小節の拍子(分子/分母)を取得する
		/// (太鼓さん次郎は分母を符号なし整数として読むため、分母が負だとほぼ0になる)
		/// </summary>
		static double GetTaikojiroMeasureRatio(Measure measure)
		{
			double lower = measure.Lower;
			if (lower < 0)
			{
				lower += 4294967296.0;
			}
			return measure.Upper / lower;
		}
	}
}
