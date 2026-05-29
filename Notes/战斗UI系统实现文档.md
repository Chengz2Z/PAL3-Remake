# 战斗UI系统实现文档

> 最后更新：2026-05-29

---

## 一、概述

战斗UI系统是仙剑奇侠传三重制版战斗系统的重要组成部分，为玩家提供交互式战斗体验。本文档记录战斗UI系统的设计和实现细节。

---

## 二、系统架构

### 2.1 核心组件

| 组件 | 文件路径 | 职责 |
|------|----------|------|
| **CombatUIManager** | `Assets/Scripts/Pal3.Game/GameSystems/Combat/UI/CombatUIManager.cs` | 管理战斗UI的显示和交互 |
| **CombatTurnSystem** | `Assets/Scripts/Pal3.Game/GameSystems/Combat/CombatTurnSystem.cs` | 处理回合制逻辑，支持玩家输入 |
| **CombatManager** | `Assets/Scripts/Pal3.Game/GameSystems/Combat/CombatManager.cs` | 战斗管理器，集成UI系统 |
| **CombatActorController** | `Assets/Scripts/Pal3.Game/GameSystems/Combat/Actor/Controllers/CombatActorController.cs` | 战斗角色控制器，显示伤害飘字 |

### 2.2 数据流

```
玩家输入 → CombatUIManager → CombatTurnSystem → CombatActorController
                ↓
         行动选择结果
                ↓
         执行战斗行动
                ↓
         显示伤害飘字
```

---

## 三、功能特性

### 3.1 行动菜单

战斗UI提供以下行动选项：

| 行动 | 按钮 | 功能 |
|------|------|------|
| **攻击** | Attack | 对敌人进行普通攻击 |
| **技能** | Skill | 使用技能攻击（待实现） |
| **物品** | Item | 使用物品（待实现） |
| **防御** | Defend | 防御，减少伤害（待实现） |
| **逃跑** | Flee | 逃离战斗（待实现） |

### 3.2 目标选择

- 选择"攻击"后，显示敌人列表供玩家选择目标
- 选择"防御"和"逃跑"不需要目标选择
- 选择"技能"和"物品"后，显示目标选择界面（待实现）

### 3.3 状态显示

- **HP条**：显示每个角色的当前生命值和最大生命值
- **MP条**：显示每个角色的当前魔法值和最大魔法值
- **实时更新**：每次行动后自动更新状态显示

### 3.4 伤害飘字

- **普通伤害**：红色显示
- **暴击伤害**：黄色显示，字体更大
- **治疗效果**：绿色显示（待实现）
- **动画效果**：飘字向上浮动并逐渐消失

---

## 四、实现细节

### 4.1 CombatUIManager 类

```csharp
public sealed class CombatUIManager : IDisposable
{
    // 显示行动菜单
    public void ShowActionMenu(Action<CombatActionSelection> onActionSelected);
    
    // 隐藏行动菜单
    public void HideActionMenu();
    
    // 更新角色状态显示
    public void UpdateActorStatus();
    
    // 显示伤害飘字
    public void ShowDamagePopup(Vector3 worldPosition, int damage, bool isCritical = false);
    
    // 显示治疗飘字
    public void ShowHealPopup(Vector3 worldPosition, int healAmount);
}
```

### 4.2 CombatActionSelection 类

```csharp
public sealed class CombatActionSelection
{
    public CombatActionType ActionType { get; set; }
    public CombatActorController Target { get; set; }
    public int SkillId { get; set; } = -1;
    public int ItemId { get; set; } = -1;
}
```

### 4.3 CombatPhase 枚举

```csharp
public enum CombatPhase
{
    Idle = 0,
    TurnStart,
    WaitingForPlayerInput,  // 新增：等待玩家输入
    ActorActing,
    TurnEnd,
    CheckResult,
    Finished,
}
```

---

## 五、集成说明

### 5.1 CombatManager 集成

```csharp
// 在 EnterCombat 方法中创建UI
CreateCombatUI();

// 传递UI管理器给 CombatScene
_combatScene.LoadActors(combatActors, combatContext.MeetType, _combatUIManager);

// 在 Update 方法中更新UI
if (_combatUIManager != null)
{
    _combatUIManager.UpdateActorStatus();
}
```

### 5.2 CombatTurnSystem 集成

```csharp
// 构造函数接收 CombatUIManager
public CombatTurnSystem(CombatScene combatScene,
    Func<IEnumerator, object> coroutineRunner,
    CombatUIManager combatUIManager = null)

// 当轮到玩家角色时显示行动菜单
if (IsPlayerActor(next))
{
    Phase = CombatPhase.WaitingForPlayerInput;
    _combatUIManager.ShowActionMenu(OnPlayerActionSelected);
}
```

### 5.3 CombatActorController 集成

```csharp
// Init 方法接收 CombatUIManager
public void Init(CombatActor actor,
    ActorActionController actionController,
    ElementPosition elementPosition,
    CombatUIManager combatUIManager = null)

// 在 ApplyDamageAsync 中显示伤害飘字
if (_combatUIManager != null)
{
    Vector3 worldPos = _actionController.Transform.Position + Vector3.up * 2f;
    _combatUIManager.ShowDamagePopup(worldPos, damage);
}
```

---

## 六、待实现功能

### 6.1 技能系统集成

- [ ] 技能选择UI
- [ ] 技能目标选择
- [ ] 技能效果执行
- [ ] MP消耗显示

### 6.2 物品系统集成

- [ ] 物品选择UI
- [ ] 物品使用逻辑
- [ ] 物品效果显示

### 6.3 防御和逃跑

- [ ] 防御状态效果
- [ ] 逃跑成功率计算
- [ ] 逃跑失败处理

### 6.4 UI美化

- [ ] 更精美的UI样式
- [ ] 动画效果优化
- [ ] 音效集成

---

## 七、测试指南

### 7.1 基本测试流程

1. 启动游戏，进入战斗场景
2. 验证行动菜单是否正常显示
3. 选择"攻击"，验证目标选择界面
4. 选择目标，验证攻击动画和伤害飘字
5. 验证HP/MP条是否实时更新
6. 验证战斗结束条件

### 7.2 测试用例

| 测试项 | 预期结果 |
|--------|----------|
| 行动菜单显示 | 菜单正常显示，按钮可点击 |
| 目标选择 | 显示敌人列表，可选择目标 |
| 攻击执行 | 角色移动到目标，执行攻击动画 |
| 伤害飘字 | 显示伤害数字，有动画效果 |
| 状态更新 | HP/MP条实时更新 |
| 战斗结束 | 一方全灭时战斗结束 |

---

## 八、已知问题

1. **技能和物品功能未实现**：当前只能使用普通攻击
2. **防御和逃跑功能未实现**：选择后只是等待0.5秒
3. **UI样式简单**：需要进一步美化
4. **目标选择界面简陋**：需要改进交互体验

---

## 九、后续开发计划

### 阶段4.1：完善基础功能
- 实现技能系统集成
- 实现物品系统集成
- 实现防御和逃跑逻辑

### 阶段4.2：UI美化
- 设计更精美的UI样式
- 添加动画效果
- 集成音效

### 阶段4.3：高级功能
- 实现状态效果显示
- 实现合击技
- 实现特殊战斗场景

---

## 十、参考资料

- 原版游戏数据文件：`extracted/PAL3/` 和 `extracted/PAL3A/`
- 战斗系统设计文档：`Notes/开发计划.md`
- 数据字典：`Notes/数据字典.md`
- 原仓库：https://github.com/0x7c13/Pal3.Unity