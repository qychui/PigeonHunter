# PigeonHunter

## Game Preview 

https://x.com/qychui/status/2057009900312842413

![DuckHunt](DuckHunt.gif)

![DuckHunt](DuckHunt2.gif)

![DuckHunt](DuckHunt3.gif)

![ScenePreview](ScenePreview.png)

---



## English

### Project Overview

Pigeon Hunt is a retro FC / NES-style shooting mini-game made with Unity for VRChat. The original plan was to first recreate the gameplay using publicly available Duck Hunt-style assets, then gradually replace the assets and expand the features until it became a true Pigeon Hunt project. However, since the project has been delayed for quite a while, the current version is being organized and wrapped up first.

The project recreates roughly 95% of the core gameplay from the original FC / NES Duck Hunt, and implements basic multiplayer synchronization, a retro TV display effect, and a simple UI / score system.

This project was developed with Unity 2022.3.22f1, depends on UdonSharp, and requires VRChat SDK Worlds 3.10.3 or later.

### Game Modes

**Mode A: Single Pigeon Mode**

**Mode B: Pair Pigeon Mode**

**Mode C: Shooting Range Mode**

### Gun Features

The gun implements basic shooting, gunshot audio, muzzle particles, shot flash objects, hit detection, and laser aiming assistance.

The player who picks up the gun will attempt to become the current gameplay owner. When the gun fires, local feedback is played immediately, and key effects are synchronized to other clients through `SyncController`, including gunshot audio, muzzle particles, shot flash, hit events, and miss-shot events.

The laser aiming assist supports three display states:

- Off
- Line + hit dot
- Hit dot only

Double-clicking the use input switches the laser display mode. When the gun is picked up, remote clients can also see the laser. When the gun is dropped, the laser is hidden in sync.

### Multiplayer Synchronization

The project uses lightweight synchronization. It does not try to keep every frame perfectly identical, and instead synchronizes important gameplay checkpoints.

Mode A and Mode B pigeon movement is not synchronized frame by frame. Instead, each round synchronizes a seed, allowing different clients to use the same random data for pigeon generation, launch direction, boundary deflection, and random direction changes after shooting.

Mode C uses a wave seed to synchronize clay target generation data. Since each clay target has a relatively consistent lifecycle, synchronization focuses on wave start, hit events, miss-shot events, and round snapshots.

### Owner Rules

Whoever picks up the gun will attempt to become the gameplay owner. The owner is responsible for sending key synchronization events such as hits, miss shots, round plans, and round results.

If the owner changes during gameplay, the new owner will continue sending synchronization data through later actions. If multiple Pigeon Hunt instances exist in the same scene, each gun only affects the `GameManager` it belongs to, preventing a gun from one instance from shooting the screen of another instance.

### Late Join Handling

When a new player joins during gameplay, the project does not forcefully rebuild all currently playing animation states immediately. Instead, it tends to realign before the next round or wave. This reduces sudden teleporting, animation jumps, and timeline mismatch.

Mode C supports round snapshots, allowing late joiners to align with the current stage, score, and difficulty before the next round.

### Main Configuration

Common `GameManager` settings include:

- `pigeonsPerRound`: number of pigeons per round
- `spawnDelay`: delay between waves
- `pairModeWaveCount`: number of waves in pair pigeon mode
- `roundDifficultyStep`: difficulty increase per round
- `maxDifficultyMultiplier`: maximum difficulty multiplier
- `pigeonBoundaryRandomDeflectionChanceBase`: initial chance for random boundary deflection
- `pigeonBoundaryRandomDeflectionChanceStepPerRound`: boundary deflection chance increase per round
- `pigeonBoundaryRandomDeflectionChanceMax`: maximum chance for random boundary deflection
- `shotDirectionChangeChance`: chance for pigeons to randomly change direction after a shot
- `shootingRangeWaveCount`: number of waves per Mode C round
- `shootingRangeClayLifetimeMax / Min`: clay target lifetime range
- `enableGunShotFlashObjects`: whether to enable shot flash objects
- `gunShotFlashDuration`: duration of the shot flash
- `screenRetroTvMask`: Retro TV mask object

Common `GunController` settings include:

- `fireOrigin`: raycast origin
- `fireCooldown`: shooting cooldown
- `distance`: shooting detection distance
- `hitMask`: hittable layers
- `muzzleFlash`: muzzle particles
- `shootSfx / clayHitSfx`: gunshot and clay hit audio
- `laserLine / laserHitDot`: laser line and hit dot
- `laserDisplayMode`: laser display mode

Common `MainAreaAnimationController` settings include:

- References for each dog state object
- Hit / miss / round start / round next animation timing
- Dog jump movement parameters
- `lmfao_VRCDog`
- `lmfaoVRCDogChance`
- `hideDogAfterArcPeak`
- `hideDogAfterArcPeakDelay`

### Folder Structure

- `Animations`: animation assets
- `Materials`: material assets
- `Models`: model assets
- `Prefabs`: main prefabs, including PigeonHunter
- `Scenes`: sample scenes
- `Shaders`: custom shaders such as RetroTV, Score, and Timed Color Switch
- `Sounds`: audio assets
- `Textures`: texture assets
- `UScripts`: UdonSharp scripts

### Notes

This project uses checkpoint-style synchronization rather than full frame-by-frame synchronization. Therefore, small timing differences between clients may still occur. The project uses round seeds, wave seeds, and key event synchronization to reduce differences.

When modifying target movement logic, random logic, or round flow, make sure the owner and remote clients use the same seed. Otherwise, target generation or random events may become desynchronized.

If there are multiple Duck Hunt Objects in the scene, duplicate and assign separate Score Materials for each Duck Hunt Object reference. Otherwise, score display desynchronization may occur.

### Credits / License Template

You can include the following in the world description:

Special thanks to the following asset creators for supporting this project:

- "Duck Hunt Cartridge" (https://skfb.ly/6BAYI) by BIGDOGLOBAL is licensed under Creative Commons Attribution-NoDerivs (http://creativecommons.org/licenses/by-nd/4.0/).
- "NES Zapper" (https://skfb.ly/onJZv) by Thomas Fugier is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "NES Console and Controller" (https://skfb.ly/onGrq) by Daz is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "Retro TV set new, old, broken" (https://skfb.ly/o7uKC) by Bansheeva is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).



## 中文

### 项目简介

Pigeon Hunt 是一个使用 Unity 制作的 VRChat 复古 FC / NES 风格射击小游戏。作者最初的计划是先使用公开的 Duck Hunt 风格素材完成玩法复刻，再逐步替换素材并扩展功能，最终做成真正的 Pigeon Hunt 项目。不过这个项目已经拖了比较久，所以目前会先把现有版本整理并收尾。

项目大约复刻了原版 FC / NES Duck Hunt 95% 的核心玩法，并实现了基础多人同步、复古电视显示效果和简单的 UI / 分数系统。

本项目使用 Unity 2022.3.22f1 开发，依赖 UdonSharp，并需要 VRChat SDK Worlds 3.10.3 或更高版本。

### 游戏模式

**Mode A：单鸽模式**

**Mode B：双鸽模式**

**Mode C：射击场模式**

### 枪械功能

枪械实现了基础射击、枪声音效、枪口粒子、开枪闪光物体、命中检测和激光辅助瞄准。

拾取枪械的玩家会尝试成为当前 gameplay owner。开枪时，本地会立即播放射击反馈，并通过 `SyncController` 将关键效果同步给其他客户端，包括枪声、枪口粒子、开枪闪光、命中事件和空枪事件。

激光辅助瞄准支持三种显示状态：

- 关闭
- 线段 + 命中点
- 仅命中点

双击使用键可以切换激光显示模式。枪械被拾取后，远端玩家也能看到激光；枪械被放下后，激光会同步隐藏。

### 多人同步设计

项目采用轻量同步方式，不追求每一帧都完全一致，而是同步关键 gameplay 节点。

Mode A 和 Mode B 的鸽子移动轨迹不会逐帧同步，而是每回合同步一个 seed，让不同客户端使用同一套随机数据来生成鸽子、发射方向、边界反弹偏转，以及射击后的随机转向。

Mode C 使用 wave seed 同步飞盘生成数据。由于每个飞盘的生命周期相对固定，同步重点放在波次开始、命中事件、空枪事件和回合快照上。

### Owner 规则

谁拾取枪械，谁就会尝试成为 gameplay owner。owner 负责发送关键同步事件，例如命中、空枪、回合计划和回合结果。

如果游戏过程中 owner 发生切换，新的 owner 会在后续操作中继续发送同步数据。如果同一个场景中存在多个 Pigeon Hunt 实例，每把枪只会影响它所属的 `GameManager`，避免一个实例的枪射到另一个实例的屏幕上。

### 新玩家加入

当新玩家在游戏中途加入时，项目不会强制立刻重建所有正在播放的动画状态，而是倾向于在下一回合或下一波开始前重新校准。这样可以减少突然传送、动画跳变和时间线错位。

Mode C 支持回合快照，新加入的玩家可以在下一回合前对齐当前阶段、分数和难度。

### 主要配置项

常用 `GameManager` 配置包括：

- `pigeonsPerRound`：每回合鸽子数量
- `spawnDelay`：每波之间的延迟
- `pairModeWaveCount`：双鸽模式的波数
- `roundDifficultyStep`：每回合难度增长
- `maxDifficultyMultiplier`：最大难度倍率
- `pigeonBoundaryRandomDeflectionChanceBase`：边界随机偏转初始概率
- `pigeonBoundaryRandomDeflectionChanceStepPerRound`：每回合边界偏转概率增长
- `pigeonBoundaryRandomDeflectionChanceMax`：边界随机偏转最大概率
- `shotDirectionChangeChance`：开枪后鸽子随机转向概率
- `shootingRangeWaveCount`：Mode C 每回合波数
- `shootingRangeClayLifetimeMax / Min`：飞盘生命周期范围
- `enableGunShotFlashObjects`：是否启用开枪闪光物体
- `gunShotFlashDuration`：开枪闪光持续时间
- `screenRetroTvMask`：Retro TV 遮罩物体

常用 `GunController` 配置包括：

- `fireOrigin`：射线发射点
- `fireCooldown`：射击冷却
- `distance`：射击检测距离
- `hitMask`：可命中 Layer
- `muzzleFlash`：枪口粒子
- `shootSfx / clayHitSfx`：枪声和飞盘命中音效
- `laserLine / laserHitDot`：激光线和命中点
- `laserDisplayMode`：激光显示模式

常用 `MainAreaAnimationController` 配置包括：

- 狗的各状态对象引用
- 命中 / miss / round start / round next 动画时间
- 狗跳跃位移参数
- `lmfao_VRCDog`
- `lmfaoVRCDogChance`
- `hideDogAfterArcPeak`
- `hideDogAfterArcPeakDelay`

### 目录结构

- `Animations`：动画资源
- `Materials`：材质资源
- `Models`：模型资源
- `Prefabs`：主要 prefab，包括 PigeonHunter
- `Scenes`：示例场景
- `Shaders`：自定义 shader，例如 RetroTV、Score、Timed Color Switch
- `Sounds`：音频资源
- `Textures`：贴图资源
- `UScripts`：UdonSharp 脚本

### 注意事项

本项目使用检查点式同步，而不是完整逐帧同步。因此，不同客户端之间仍然可能出现轻微时间差。项目通过 round seed、wave seed 和关键事件同步来尽量减少差异。

修改目标移动逻辑、随机逻辑或回合流程时，要确保 owner 和 remote 客户端使用同一套 seed。否则目标生成或随机事件可能会不同步。

如果场景中存在多个Duck Hunt Object，需要复制多份新的Score Materials配置到Duck Hunt Object引用上，不然会出现分数不同步的问题。

### 致谢 / 授权模板

可以在世界描述中加入以下内容：

感谢以下素材作者对本项目的支持：

- "Duck Hunt Cartridge" (https://skfb.ly/6BAYI) by BIGDOGLOBAL is licensed under Creative Commons Attribution-NoDerivs (http://creativecommons.org/licenses/by-nd/4.0/).
- "NES Zapper" (https://skfb.ly/onJZv) by Thomas Fugier is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "NES Console and Controller" (https://skfb.ly/onGrq) by Daz is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
- "Retro TV set new, old, broken" (https://skfb.ly/o7uKC) by Bansheeva is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).

