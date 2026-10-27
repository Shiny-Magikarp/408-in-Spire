# 408 in Spire

在《杀戮尖塔2》中复习408计算机专业基础：选择四科和章节，用答题积累金币，为指定卡牌获得伤害与格挡加成。

当前开发版 **0.8.0**，适配游戏v0.111.0，依赖RitsuLib 0.6.9。仅支持单人标准模式。

## 玩法

- 开局选择数据结构、计算机组成原理、操作系统、计算机网络，可按章节、错题和真题来源筛选。
- 平时正常出牌。点击学识徽记后，下一张主动选择的牌触发选择题；已排队或自动打出的牌不会抢走激活。
- 答对一题获得10答题金币，该次牌的伤害与格挡翻倍。普通/精英/Boss战分别最多30/60/90答题金币，对应3/6/9次成功增益；答错不消耗次数，答题金币胜利时结算。
- 卡牌和遗物自身正常产金，不占答题上限。初始金币为0，普通战利品金币关闭，部分金币事件改为大题挑战。
- 徽记显示剩余增益次数和本场答题金币，带静息、激活和悬停反馈。界面沿用原版组件，场景和组件绑定可在Godot编辑器中修改。
- 错题记录跨局保存；大题题库可以在跑局前独立练习，独立练习不发游戏奖励。

## 瓦库与大题

本地导入后共204道大题：196道来自cs408，另有8道项目自编练习。全部题目已有评分入口，其中72道导入题使用确定性计算结果规则，8道使用数值/概念混合规则，124道使用新增的实验性语义核分。

瓦库dev2已在本地完成微调：8,777条训练对，1,360条开发验证，1,200条合成测试。新增124道覆盖1,747个参考评分点，支持长答案分段核对和逐点反馈。模型FP16权重约205MB，完整本地Mod约255MB，完全离线，不需要API Key。

**覆盖全部题目不等于可靠批改所有代码与证明。** 合成测试分类正确1,189/1,200，仍有9条错误给分；另写68条短断言的纯NLI分类正确50条。运行时另有有限的条件冲突检查，但缺少独立人工标注的真实答卷评估。代码执行、证明与绘图过程仍需复核，此类结果不发额外遗物或卡牌。评分为练习分，不是原卷整题过程分。

训练方法、精度比较和限制见 [瓦库全题评分说明](docs/WAKUU-FULL.md)。

## 选择题质量

当前本地题库可玩1,676道，668道因未核验OCR、缺图或条件歧义隔离。已做299组跨来源候选配对与28道独立复算，修正已确认错配；尚未完成逐题学术审校，不能保证全库答案正确。具体依据见 [题库核验记录](docs/ACADEMIC-AUDIT.md)。

选择题固定来源commit为`267d0d815b1eee770dad12e88bbd560d08ba3f66`，大题固定来源commit为`5a689c40f7ec7e9298c76966dfe300b8f3c3b682`。一键导入验证SHA-256，应用带前值校验的显式勘误。

## 构建和运行

源码仓库不包含原版游戏DLL、纹理、字体、原版场景、完整题库、模型权重或玩家存档。请将项目放在合法安装的游戏目录内，使用本机游戏程序集和单独导入的数据构建。

需要.NET9 SDK、Python3.12、Node.js；训练另需PyTorch和Transformers等依赖。游戏运行不需要这些开发工具。

```powershell
# 首次模型准备（固定底模下载、自编概念训练和ONNX开发依赖）
./PrepareWakuu.ps1
# 全大题实验性微调、评估与候选选择
./TrainWakuu.ps1
# 导入选择题；后续可使用 --offline
./import_questions.ps1
# 构建、打包并安装；重启游戏启用
./Build.ps1 -Install
```

完整题库与派生训练数据的授权须单独核实；上游软件许可不能直接替代题目内容授权。本仓库公开导入与训练工具，**不附带题库或权重的公开下载发行包**。本地生成的数据、模型、缓存和安装包均被版本控制排除。

场景编辑：`./Preview.ps1 -Editor`；具体绑定见 [编辑器说明](docs/EDITOR.md)。

## 验证

```powershell
python -X utf8 -m unittest discover -s tools -p 'test_*.py' -v
dotnet run --project tests/CoreTests.csproj
python -X utf8 tools/verify_assets.py
./SmokeTest.ps1
```

完整验证需要本地题库与模型。游戏集成测试使用隔离APPDATA、禁用Steam，不改玩家实际存档，结束后恢复不含自动测试入口的正式DLL。公开CI只运行不依赖游戏/模型的数据结构测试、源码检查和核心项目编译。

## 致谢

感谢以下项目的作者与贡献者：

| 项目 | 在本项目中的用途 |
| --- | --- |
| [CET46 In Spire](https://github.com/sleepyHolo/SpireMod_CET46InSpire) | 学习与尖塔战斗结合的玩法灵感；本项目为二代独立实现，没有移植其Java代码或词库 |
| [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) | Mod框架、模型注册、生命周期与存档支持 |
| [你画瓦猜](https://github.com/QingTian-222/Sts2Mod_DrawAndGuessMod) | 小模型通过ONNX在游戏本地部署的参考；未使用其DINOv2权重或适配器 |
| [lij768423-svg/408-](https://github.com/lij768423-svg/408-) | 选择题导入来源 |
| [43aquarius/cs408](https://github.com/43aquarius/cs408) | 综合应用题、参考答案与解析导入来源 |
| [Become-ILLUSORY/408-quiz](https://github.com/Become-ILLUSORY/408-quiz) | 选择题答案交叉核验参考，不作为运行依赖 |
| [IDEA-CCNL / Erlangshen-Roberta-110M-NLI](https://huggingface.co/IDEA-CCNL/Erlangshen-Roberta-110M-NLI) | 瓦库的中文自然语言推断底模 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | 完全离线推理 |
| [Harmony](https://github.com/pardeike/Harmony)、[Godot](https://github.com/godotengine/godot) | 游戏行为补丁、场景编辑与打包 |
| [PyTorch](https://github.com/pytorch/pytorch)、[Transformers](https://github.com/huggingface/transformers) | 本地训练、分词与导出 |
| [MathJax](https://github.com/mathjax/MathJax-src)、[sharp](https://github.com/lovell/sharp) | 公式和项目自制图片的构建处理 |
| [BaseLib-StS2](https://github.com/Alchyr/BaseLib-StS2) | 早期框架评估参考；当前不是运行依赖 |

感谢Mega Crit与《杀戮尖塔2》的Mod社区。原版游戏资源归其权利人所有，不随本源码仓库发布。

## 许可

本项目原创源代码采用 [MIT](LICENSE)。第三方代码、模型与题目内容保留各自许可及限制，详见 [第三方声明](THIRD_PARTY_NOTICES.md) 与 [许可原文](third_party/licenses)。
