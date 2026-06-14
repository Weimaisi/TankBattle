# BatchMatchRunner 使用说明

## 功能
- 自动连续运行多场坦克 AI 对局
- 自动记录每场结果（比分、SuperStar获得者、胜者）
- 支持 5 倍速加速模拟
- 输出 CSV + JSON 结果文件
- 结果保存在 **项目根目录/MatchResults/** 文件夹

## 使用步骤

### 1. 挂脚本
把 `BatchMatchRunner.cs` 挂到 BattleField 场景的任意 GameObject 上。

### 2. 设置执行顺序
`Edit > Project Settings > Script Execution Order`，添加 `BatchMatchRunner`，值设为 **500**。

### 3. 配置 Inspector

| 参数 | 说明 |
|------|------|
| **Matchups** | AI 对决列表，填写 AI 脚本全名 |
| **RepeatCount** | 每对对决重复次数 |
| **MatchTimeOverride** | 覆盖对局时长，0=默认180秒 |
| **MatchEndDelay** | 每场结束后等待秒数 |
| **EnableTimeAccel** | 开启5倍速模拟 |
| **TimeScale** | 加速倍率（默认5） |
| **OutputFilePrefix** | 输出文件名前缀 |

### 4. AI 脚本全名参考

| AI | 脚本全名 |
|----|----------|
| BT 行为树 | `BT.MyTank` |
| FSM 状态机 | `FSM.MyTank` |
| GOAP | `GOAP.MyTank` |

### 5. 运行
点 Play，脚本自动跑完所有对局。

## 输出示例

### CSV
```csv
场次,A队脚本,A队名称,B队脚本,B队名称,A队得分,B队得分,胜者,SuperStar获得者,时长(秒)
1,BT.MyTank,BT_Tank,FSM.MyTank,FSM_Tank,120,85,BT_Tank,BT_Tank,180.0
2,BT.MyTank,BT_Tank,FSM.MyTank,FSM_Tank,95,110,FSM_Tank,FSM_Tank,180.0
```

### JSON
```json
{
  "results": [
    {
      "matchNumber": 1,
      "teamA": { "script": "BT.MyTank", "name": "BT_Tank", "score": 120 },
      "teamB": { "script": "FSM.MyTank", "name": "FSM_Tank", "score": 85 },
      "winner": "BT_Tank",
      "superStarTaker": "BT_Tank",
      "duration": 180.0
    }
  ]
}
```

## Editor 菜单
- `Tools > BatchMatchRunner > 打开结果文件夹` — 打开 MatchResults 目录
- `Tools > BatchMatchRunner > 重置批量对局状态` — 清除所有状态
