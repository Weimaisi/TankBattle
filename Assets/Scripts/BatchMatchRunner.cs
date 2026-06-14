using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Main;
using UnityEngine;
using UnityEngine.SceneManagement;

// ================================================================
// BatchMatchRunner - 连续自动对局脚本
// 功能：
//   1. 自动连续运行配置好的AI对局
//   2. 每场对局结束后自动记录结果（比分、SuperStar获得者）
//   3. 自动重载场景开始下一场
//   4. 输出 CSV + JSON 结果文件到项目根目录的 MatchResults 文件夹
//
// 使用步骤：
//   1. 将此脚本挂到 BattleField 场景中的任意 GameObject 上
//   2. 在 Inspector 中配置 Matchups 列表（AI脚本全名）
//   3. 设置 RepeatCount（每个对决重复次数）
//   4. 运行场景，等待所有对局完成
//   5. 结果文件保存在 项目根目录/MatchResults/ 下
//
// 注意：Script Execution Order 建议设为 500
// ================================================================

/// <summary>
/// 单场对局配置
/// </summary>
[Serializable]
public class MatchupConfig
{
    [Tooltip("A队坦克AI脚本全名，例如 BT.MyTank 或 FSM.MyTank")]
    public string TeamAScript;

    [Tooltip("B队坦克AI脚本全名")]
    public string TeamBScript;
}

/// <summary>
/// 单场对局结果记录
/// </summary>
[Serializable]
public class MatchResult
{
    public int MatchNumber;
    public string TeamAScript;
    public string TeamBScript;
    public string TeamAName;
    public string TeamBName;
    public int TeamAScore;
    public int TeamBScore;
    public string Winner;
    public string SuperStarTaker;
    public float Duration;      // 实际对局时长(秒)
    public string Timestamp;    // 记录时间
}

/// <summary>
/// 批量对局静态状态（跨场景重载持久化）
/// </summary>
public static class BatchMatchState
{
    public static List<MatchupConfig> Matchups = new List<MatchupConfig>();
    public static int RepeatCount = 1;
    public static float MatchTimeOverride = 0f;
    public static bool EnableTimeAccel = false;
    public static string OutputDir;
    public static string OutputPathCSV;
    public static string OutputPathJSON;
    public static int MatchupIndex = 0;
    public static int RepeatIndex = 0;
    public static bool IsComplete = false;
    public static bool IsInitialized = false;
    public static List<MatchResult> Results = new List<MatchResult>();
    public static int TotalMatchCount => Matchups.Count * RepeatCount;

    public static void Reset()
    {
        Matchups.Clear();
        RepeatCount = 1;
        MatchTimeOverride = 0f;
        EnableTimeAccel = false;
        MatchupIndex = 0;
        RepeatIndex = 0;
        IsComplete = false;
        IsInitialized = false;
        Results.Clear();
    }
}

/// <summary>
/// 批量对局运行器
/// 放在 BattleField 场景中即可自动运行连续对局
/// Script Execution Order 建议设为 500
/// </summary>
[DefaultExecutionOrder(500)]
public class BatchMatchRunner : MonoBehaviour
{
    [Header("========== 对局配置 ==========")]
    [Tooltip("AI对决列表，每项为一场对局的双方")]
    public List<MatchupConfig> Matchups = new List<MatchupConfig>();

    [Tooltip("每个对决重复进行的次数")]
    [Min(1)]
    public int RepeatCount = 1;

    [Tooltip("每场对局时长(秒)，0表示使用场景默认值(180秒)")]
    [Min(0)]
    public float MatchTimeOverride = 0f;

    [Tooltip("每场对局结束后等待时间(秒)，用于展示结果")]
    [Min(0.5f)]
    public float MatchEndDelay = 3f;

    [Header("========== 加速设置 ==========")]
    [Tooltip("开启5倍速模拟对局")]
    public bool EnableTimeAccel = false;

    [Tooltip("加速倍率")]
    [Min(1f)]
    public float TimeScale = 5f;

    [Header("========== 输出设置 ==========")]
    [Tooltip("输出文件名前缀，会自动加上时间戳")]
    public string OutputFilePrefix = "MatchResults";

    [Header("========== 运行时状态(只读) ==========")]
    [SerializeField] private int m_TotalMatches;
    [SerializeField] private int m_CompletedMatches;
    [SerializeField] private string m_CurrentMatchInfo = "等待初始化...";

    // 每场对局的追踪变量
    private string m_SuperStarTaker = "None";
    private float m_MatchStartTime;
    private bool m_MatchEnded;

    //--------------------------------------------------------------------------------
    // Unity Lifecycle
    //--------------------------------------------------------------------------------

    void Awake()
    {
        // ---- 初始化静态状态（仅首次） ----
        if (!BatchMatchState.IsInitialized)
        {
            BatchMatchState.Matchups = new List<MatchupConfig>(Matchups);
            BatchMatchState.RepeatCount = RepeatCount;
            BatchMatchState.MatchTimeOverride = MatchTimeOverride;
            BatchMatchState.EnableTimeAccel = EnableTimeAccel;
            BatchMatchState.MatchupIndex = 0;
            BatchMatchState.RepeatIndex = 0;
            BatchMatchState.IsComplete = false;
            BatchMatchState.Results = new List<MatchResult>();
            BatchMatchState.IsInitialized = true;

            // 生成带时间戳的输出路径 — 保存在 Assets/MatchResults 文件夹
            BatchMatchState.OutputDir = Path.Combine(Application.dataPath, "MatchResults");
            Directory.CreateDirectory(BatchMatchState.OutputDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            BatchMatchState.OutputPathCSV = Path.Combine(BatchMatchState.OutputDir, $"{OutputFilePrefix}_{timestamp}.csv");
            BatchMatchState.OutputPathJSON = Path.Combine(BatchMatchState.OutputDir, $"{OutputFilePrefix}_{timestamp}.json");

            Debug.Log($"<color=cyan>[BatchRunner] 输出目录: {BatchMatchState.OutputDir}</color>");
            Debug.Log($"<color=cyan>[BatchRunner] 总对局数: {BatchMatchState.TotalMatchCount}</color>");
        }
        else
        {
            // 场景重载后，从静态状态恢复配置
            Matchups = BatchMatchState.Matchups;
            RepeatCount = BatchMatchState.RepeatCount;
            MatchTimeOverride = BatchMatchState.MatchTimeOverride;
            EnableTimeAccel = BatchMatchState.EnableTimeAccel;
        }

        if (BatchMatchState.IsComplete)
        {
            Debug.Log("<color=green>[BatchRunner] 所有对局已完成！</color>");
            return;
        }

        // 确保场景重载时能正确检测到 Match 实例
        if (Match.instance == null)
        {
            StartCoroutine(DelayedConfigure());
            return;
        }

        ConfigureCurrentMatch();
    }

    IEnumerator DelayedConfigure()
    {
        yield return null;
        if (Match.instance != null)
        {
            ConfigureCurrentMatch();
        }
        else
        {
            Debug.LogError("<color=red>[BatchRunner] 找不到 Match 实例！请确保场景中有 Match 组件。</color>");
        }
    }

    void Start()
    {
        if (BatchMatchState.IsComplete) return;

        // 应用加速设置
        if (EnableTimeAccel)
        {
            Time.timeScale = TimeScale;
            Time.fixedDeltaTime = 0.02f * TimeScale;
            Debug.Log($"<color=yellow>[BatchRunner] 已开启 {TimeScale}x 加速模拟</color>");
        }

        // 订阅 SuperStar 追踪事件
        Tank.OnStarTaken += OnStarTaken;
    }

    void OnDestroy()
    {
        Tank.OnStarTaken -= OnStarTaken;

        // 恢复时间缩放
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    void OnApplicationQuit()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (!BatchMatchState.IsComplete && BatchMatchState.Results.Count > 0)
        {
            SaveResults();
        }
    }

    //--------------------------------------------------------------------------------
    // Match Configuration
    //--------------------------------------------------------------------------------

    void ConfigureCurrentMatch()
    {
        if (BatchMatchState.MatchupIndex >= BatchMatchState.Matchups.Count)
        {
            BatchMatchState.IsComplete = true;
            SaveResults();
            Debug.Log("<color=green>[BatchRunner] 全部对局完成！</color>");
            return;
        }

        var config = BatchMatchState.Matchups[BatchMatchState.MatchupIndex];

        Match.instance.TeamSettings.Clear();
        Match.instance.TeamSettings.Add(new Match.TeamSetting
        {
            Team = ETeam.A,
            TankScript = config.TeamAScript
        });
        Match.instance.TeamSettings.Add(new Match.TeamSetting
        {
            Team = ETeam.B,
            TankScript = config.TeamBScript
        });

        if (BatchMatchState.MatchTimeOverride > 0)
        {
            Match.instance.GlobalSetting.MatchTime = (int)BatchMatchState.MatchTimeOverride;
        }

        // ---- 重置追踪变量 ----
        m_SuperStarTaker = "None";
        m_MatchEnded = false;
        m_MatchStartTime = Time.time;

        int matchNum = BatchMatchState.Results.Count + 1;
        m_CurrentMatchInfo = $"#{matchNum}: {config.TeamAScript} vs {config.TeamBScript} " +
                             $"(Round {BatchMatchState.RepeatIndex + 1}/{BatchMatchState.RepeatCount})";

        Debug.Log($"<color=yellow>[BatchRunner] 开始 {m_CurrentMatchInfo}</color>");

        UpdateInspectorDisplay();
    }

    //--------------------------------------------------------------------------------
    // Event Handler
    //--------------------------------------------------------------------------------

    void OnStarTaken(Tank taker, bool isSuperStar)
    {
        if (taker == null) return;

        if (isSuperStar)
        {
            m_SuperStarTaker = taker.GetName();
            Debug.Log($"<color=magenta>[BatchRunner] SuperStar 被 {m_SuperStarTaker} 获取！</color>");
        }
    }

    //--------------------------------------------------------------------------------
    // Update Loop
    //--------------------------------------------------------------------------------

    void Update()
    {
        if (BatchMatchState.IsComplete) return;
        if (Match.instance == null) return;
        if (m_MatchEnded) return;

        UpdateInspectorDisplay();

        if (Match.instance.IsMathEnd())
        {
            m_MatchEnded = true;
            OnMatchEnd();
        }
    }

    void OnMatchEnd()
    {
        RecordResult();
        AdvanceToNextMatch();
    }

    //--------------------------------------------------------------------------------
    // Result Recording
    //--------------------------------------------------------------------------------

    void RecordResult()
    {
        var config = BatchMatchState.Matchups[BatchMatchState.MatchupIndex];

        // 取消事件订阅
        Tank.OnStarTaken -= OnStarTaken;

        var result = new MatchResult
        {
            MatchNumber = BatchMatchState.Results.Count + 1,
            TeamAScript = config.TeamAScript,
            TeamBScript = config.TeamBScript,
            TeamAName = GetTankName(ETeam.A),
            TeamBName = GetTankName(ETeam.B),
            TeamAScore = GetTeamScore(ETeam.A),
            TeamBScore = GetTeamScore(ETeam.B),
            Winner = GetWinnerName(),
            SuperStarTaker = m_SuperStarTaker,
            Duration = Time.time - m_MatchStartTime,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        BatchMatchState.Results.Add(result);

        Debug.Log($"<color=cyan>========================================</color>");
        Debug.Log($"<color=cyan>[BatchRunner] 第 {result.MatchNumber} 场结束</color>");
        Debug.Log($"<color=cyan>  {result.TeamAName}({result.TeamAScript})  vs  {result.TeamBName}({result.TeamBScript})</color>");
        Debug.Log($"<color=cyan>  比分: {result.TeamAScore} : {result.TeamBScore}</color>");
        Debug.Log($"<color=cyan>  SuperStar: {result.SuperStarTaker}</color>");
        Debug.Log($"<color=cyan>  胜者: {result.Winner}</color>");
        Debug.Log($"<color=cyan>  时长: {result.Duration:F1}s</color>");
        Debug.Log($"<color=cyan>========================================</color>");
    }

    void AdvanceToNextMatch()
    {
        BatchMatchState.RepeatIndex++;
        if (BatchMatchState.RepeatIndex >= BatchMatchState.RepeatCount)
        {
            BatchMatchState.RepeatIndex = 0;
            BatchMatchState.MatchupIndex++;
        }

        if (BatchMatchState.MatchupIndex >= BatchMatchState.Matchups.Count)
        {
            BatchMatchState.IsComplete = true;
            SaveResults();
            Debug.Log($"<color=green>[BatchRunner] ====== 全部 {BatchMatchState.Results.Count} 场对局完成！=====</color>");
            Debug.Log($"<color=green>[BatchRunner] CSV 结果: {BatchMatchState.OutputPathCSV}</color>");
            Debug.Log($"<color=green>[BatchRunner] JSON 结果: {BatchMatchState.OutputPathJSON}</color>");
            return;
        }

        StartCoroutine(ReloadSceneAfterDelay());
    }

    IEnumerator ReloadSceneAfterDelay()
    {
        Debug.Log($"<color=yellow>[BatchRunner] {MatchEndDelay}s 后开始下一场...</color>");
        yield return new WaitForSeconds(MatchEndDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    //--------------------------------------------------------------------------------
    // Helper Methods
    //--------------------------------------------------------------------------------

    int GetTeamScore(ETeam team)
    {
        int score = 0;
        var tanks = Match.instance.GetTanks(team);
        if (tanks != null)
        {
            foreach (var t in tanks)
                score += t.Score;
        }
        return score;
    }

    string GetTankName(ETeam team)
    {
        var tank = Match.instance.GetTank(team);
        return tank != null ? tank.GetName() : team.ToString();
    }

    string GetWinnerName()
    {
        var winnerTeam = Match.instance.WinnerTeam;
        var tank = Match.instance.GetTank(winnerTeam);
        return tank != null ? tank.GetName() : winnerTeam.ToString();
    }

    void UpdateInspectorDisplay()
    {
        m_TotalMatches = BatchMatchState.TotalMatchCount;
        m_CompletedMatches = BatchMatchState.Results.Count;
    }

    //--------------------------------------------------------------------------------
    // Save Results
    //--------------------------------------------------------------------------------

    void SaveResults()
    {
        if (BatchMatchState.Results.Count == 0) return;

        try
        {
            SaveCSV();
            SaveJSON();
            Debug.Log($"<color=green>[BatchRunner] 结果已保存到:</color>");
            Debug.Log($"<color=green>  CSV:  {BatchMatchState.OutputPathCSV}</color>");
            Debug.Log($"<color=green>  JSON: {BatchMatchState.OutputPathJSON}</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BatchRunner] 保存结果失败: {e.Message}");
        }
    }

    void SaveCSV()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# 批量对局结果 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 总对局数: {BatchMatchState.Results.Count}");
        sb.AppendLine();

        sb.AppendLine("场次,A队脚本,A队名称,B队脚本,B队名称," +
                       "A队得分,B队得分," +
                       "胜者,SuperStar获得者,时长(秒),记录时间");

        foreach (var r in BatchMatchState.Results)
        {
            sb.AppendLine(
                $"{r.MatchNumber}," +
                $"{EscapeCsv(r.TeamAScript)}," +
                $"{EscapeCsv(r.TeamAName)}," +
                $"{EscapeCsv(r.TeamBScript)}," +
                $"{EscapeCsv(r.TeamBName)}," +
                $"{r.TeamAScore},{r.TeamBScore}," +
                $"{EscapeCsv(r.Winner)}," +
                $"{EscapeCsv(r.SuperStarTaker)}," +
                $"{r.Duration:F1}," +
                $"{r.Timestamp}"
            );
        }

        File.WriteAllText(BatchMatchState.OutputPathCSV, sb.ToString(), Encoding.UTF8);
    }

    void SaveJSON()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"timestamp\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.AppendLine($"  \"totalMatches\": {BatchMatchState.Results.Count},");
        sb.AppendLine("  \"results\": [");

        for (int i = 0; i < BatchMatchState.Results.Count; i++)
        {
            var r = BatchMatchState.Results[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"matchNumber\": {r.MatchNumber},");
            sb.AppendLine($"      \"teamA\": {{ \"script\": \"{EscapeJson(r.TeamAScript)}\", \"name\": \"{EscapeJson(r.TeamAName)}\", \"score\": {r.TeamAScore} }},");
            sb.AppendLine($"      \"teamB\": {{ \"script\": \"{EscapeJson(r.TeamBScript)}\", \"name\": \"{EscapeJson(r.TeamBName)}\", \"score\": {r.TeamBScore} }},");
            sb.AppendLine($"      \"winner\": \"{EscapeJson(r.Winner)}\",");
            sb.AppendLine($"      \"superStarTaker\": \"{EscapeJson(r.SuperStarTaker)}\",");
            sb.AppendLine($"      \"duration\": {r.Duration:F1},");
            sb.AppendLine($"      \"timestamp\": \"{r.Timestamp}\"");
            sb.Append("    }");
            if (i < BatchMatchState.Results.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(BatchMatchState.OutputPathJSON, sb.ToString(), Encoding.UTF8);
    }

    string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    //--------------------------------------------------------------------------------
    // Editor Helper
    //--------------------------------------------------------------------------------

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/BatchMatchRunner/打开结果文件夹")]
    static void OpenResultFolder()
    {
        string path = Path.Combine(Application.dataPath, "MatchResults");
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(path);
    }

    [UnityEditor.MenuItem("Tools/BatchMatchRunner/重置批量对局状态")]
    static void ResetBatchState()
    {
        BatchMatchState.Reset();
        Debug.Log("[BatchRunner] 批量对局状态已重置");
    }
#endif
}
