# 开源来源与许可说明

本项目原创源代码采用MIT许可。下列项目、模型、题目内容和游戏资源保留各自许可与权利，不能用本项目的MIT许可覆盖。完整原文见 [third_party/licenses](third_party/licenses)。

| 项目 | 作者/组织 | 用途 | 许可或范围 |
| --- | --- | --- | --- |
| [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) | Ritsukage / BAKAOLC及贡献者 | Mod生命周期、模型注册、存档与原版UI接入；当前固定0.6.9 | MIT，独立运行依赖 |
| [Harmony](https://github.com/pardeike/Harmony) | Andreas Pardeike及贡献者 | 原版行为补丁，使用游戏提供的0Harmony程序集 | MIT |
| [Godot](https://github.com/godotengine/godot) | Godot Engine contributors | 静态场景编辑、资源打包；开发工具4.5.1 | MIT |
| [Erlangshen-Roberta-110M-NLI](https://huggingface.co/IDEA-CCNL/Erlangshen-Roberta-110M-NLI) / [Fengshenbang-LM](https://github.com/IDEA-CCNL/Fengshenbang-LM) | IDEA-CCNL | 瓦库中文语义推断底模，本地微调、导出ONNX | 发布方声明Apache-2.0；保留许可与修改记录 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | Microsoft及贡献者 | 完全离线CPU推理，Mod托管依赖1.27.1 | MIT及随附第三方声明 |
| [Transformers](https://github.com/huggingface/transformers) | Hugging Face及贡献者 | 本地训练与WordPiece分词参考 | Apache-2.0；开发依赖 |
| [PyTorch](https://github.com/pytorch/pytorch) | PyTorch contributors | 本地微调、GPU训练与ONNX导出 | BSD风格许可；开发依赖 |
| [MathJax](https://github.com/mathjax/MathJax-src) | MathJax Consortium及贡献者 | 题目公式离线渲染，3.2.2 | Apache-2.0；构建依赖 |
| [sharp](https://github.com/lovell/sharp) | Lovell Fuller及贡献者 | 公式及项目自制美术的栅格处理，0.34.3 | Apache-2.0及其依赖许可；构建依赖 |
| [408-](https://github.com/lij768423-svg/408-) | lij768423-svg | 选择题导入源；固定267d0d815b1eee770dad12e88bbd560d08ba3f66 | 仓库软件采用ISC；软件许可不代表所有考试内容权利已确认 |
| [cs408](https://github.com/43aquarius/cs408) | 43aquarius | 196道综合题导入源；固定5a689c40f7ec7e9298c76966dfe300b8f3c3b682 | MIT原文明确区分源码和备考内容；题库不提交到本公开仓库 |
| [408-quiz](https://github.com/Become-ILLUSORY/408-quiz) | Become-ILLUSORY及其标注的来源 | 仅作答案交叉核验，固定d17aeb568dc48b536051831880fc80a5cfb89904 | 未作为运行依赖、未复制完整数据到公开仓库；不据此推定内容再分发授权 |

## 玩法灵感与部署参考

- [CET46 In Spire](https://github.com/sleepyHolo/SpireMod_CET46InSpire)，sleepyHolo及贡献者：提供“在尖塔中答题并影响卡牌效果”的玩法灵感。上游采用GPL-3.0；本项目为二代C#/Godot独立实现，没有移植其Java代码或词库。
- [你画瓦猜 / DrawAndGuessMod](https://github.com/QingTian-222/Sts2Mod_DrawAndGuessMod)，QingTian-222及贡献者：参考其小模型通过ONNX在本地部署的方式。上游采用MIT；本项目未使用其DINOv2权重、适配器或图像模型。
- [BaseLib-StS2](https://github.com/Alchyr/BaseLib-StS2)：早期框架评估参考；当前选择RitsuLib，BaseLib不是本项目运行依赖。

## 不随源码仓库发布的内容

原版游戏DLL、反编译代码、字体、纹理、原版场景与音效不随本仓库发布，须从合法安装的《杀戮尖塔2》中读取。项目原创Godot场景、C#脚本和自制教材/徽记图像单独保留。

完整题库、来源缓存、来源派生的训练样例、模型权重、训练检查点、玩家存档及本机开发缓存被`.gitignore`排除。公开导入/训练工具并不意味着授予题目内容的再分发权，也不应把模型与数据默认当作本项目MIT源码的一部分。
