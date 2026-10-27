# 编辑场景和绑定组件

先运行 `Build.ps1`，再运行 `Preview.ps1 -Editor`，可打开项目自带的 Godot .NET 4.5.1 编辑器。
项目根文件为 `game/project.godot`，解决方案为 `game/Spire408.sln`。

场景已经写入 `.tscn`，运行时只实例化场景并填充内容。
编辑器中可以移动节点、调整 Theme、修改文字和控件尺寸。

## 开局设置

打开 `game/Spire408/Scenes/StudySetup.tscn`，选中根节点。
Inspector 中的导出属性对应四科 SpireTickbox、章节与来源 SpireDropdown、题数 Label、开始与返回 SpireButton。
它们分别继承游戏的 NTickbox、NSettingsDropdown、NPopupYesNoButton。
LibraryButton 绑定 LibraryRow 中的 SpireSettingsButton，继承 NSettingsButton，复用原版设置页的贴图、HSV 材质、选中标记及交互行为。其 CaptionLabel 在 Inspector 绑定静态标签。
底部 Actions 按 Cancel、Spacer、Start 排列；Spacer 水平扩展，使返回与开始游戏分别贴近两端。
`SpireButton.tscn` / `SpireCancelButton.tscn` 继承原版弹窗按钮场景；勾选框复用原版 TickboxVisuals，字体、边框和滚动条引用原版资源。
可以拖入其他控件替换引用；不需要在代码中重建界面。

## 单选答题

打开 `game/Spire408/Scenes/QuizPanel.tscn`，选中根节点。
Stem、OptionA～D、Explanation 绑定 RichTextLabel，PickA～D 绑定 SpireTickbox。
Scroll 绑定题干与解析所在的 ScrollContainer，Submit 和 Skip 绑定按钮。
选项的相互排斥由脚本处理，选择后仍需要显式提交。

控件改名后，检查根节点的导出引用是否仍然有效。
按钮事件由脚本连接一次，不要再在 Signals 面板重复连接相同行为。

## 大题

打开 `game/Spire408/Scenes/EssayPanel.tscn`，选中根节点。
Answer1～Answer4 绑定四个 TextEdit，Prompt1～Prompt4 绑定题目标签，Part1～Part4 绑定对应容器。
Heading、Rules、Stem、Feedback、Scroll 以及 Submit、Back、Abandon 均通过导出属性绑定。
Previous、Next、Page 绑定评分点翻页按钮和页码，WrongToggle 绑定原版勾选框。
DraftTimer 控制草稿保存节流；表单保存在场景里，不通过运行时代码创建整套 UI。
输入使用 Godot 原生 TextEdit，保留中文输入法；按钮、字体、纸张背景和滚动条沿用游戏资源。
四组输入框按页复用，数据最多 32 个评分点；翻页不会重新创建场景节点。
题库浏览场景为 `EssayLibrary.tscn`，Summary、Description、Search、QuestionSelect、Open、Back 均可在 Inspector 中重新绑定。
开局设置页的 LibraryButton 对应“大题题库”按钮；保留题库、分页与输入框的静态场景结构。

## 战斗学识计数器

打开 `game/Spire408/Scenes/StudyCombatHud.tscn`。根节点导出 Emblem、RemainingLabel、GoldLabel、ActivationButton 四个绑定。
徽记、次数及金币均为静态场景节点，上方状态文字已删除；战斗 UI 激活时只实例化该场景，不在代码中创建整套控件。
根节点锚定左下方，可在编辑器中调整位置、尺寸与文字。其位置位于原版能量球上方，状态从当前跑局存档读取。
ActivationButton 使用原生 NButton 交互，StudyKnowledgeButton 导出 HoverMotion、AnimatedMotion、Glow、Animations 四个绑定。图标与计数在 Motion 中，悬停和按压缩放放在外层 HoverMotion，避免与状态动画争用同一个缩放属性。
AnimationPlayer 的默认动画库包含 idle（2.4 秒轻微呼吸）和 active（1.2 秒脉动与光晕）；所有轨道和光晕节点已保存在场景里，可直接在编辑器中调整，没有运行时搭建动画树。
StudyActivation 在原生 PlayCardAction 创建时预留当前激活，按动作对象、卡牌、战斗和跑局校验，再在对应出牌前消费；未激活的牌直接走原生执行路径，不创建答题面板。
正确答题的倍率在 StudyBook 原生 ModifyDamageMultiplicative / ModifyBlockMultiplicative 接口中生效；StudyCardBoost 仅在当前主动出牌任务期间开放，结束或异常时清理。

## 预览与打包

`Preview.ps1` 可独立预览设置页；`Preview.ps1 -Quiz` 可预览公式和选项；`Preview.ps1 -Essay` 可预览大题和真实离线批改。
预览脚本会从本机 `SlayTheSpire2.pck` 提取必要的 UI 资源到忽略版本控制的开发缓存，并注册本机 sts2.dll 中的原版组件。
不重新编译原版脚本，也不把这些原版资源复制进分发包。请保持项目位于当前游戏目录中。
`game/addons/spire408_native_ui` 插件仅供编辑器读取原版脚本；界面树和组件引用保存在 `.tscn` 中。
预览只使用临时学习记录，不读取或写入游戏存档。
修改场景后，运行 `Build.ps1 -SkipAssets -Install` 可更新本地 Mod。
修改题库或公式排版后，用完整 `Build.ps1 -Install` 重新生成资源。

题干和解析的公式在打包阶段转成图片，游戏运行时不依赖 Node.js、Python 或联网服务。

## 遗物美术

教材母版位于 `art/textbook-408-transparent.png`，提示词和生成来源见 `art/README.md`。
`tools/build_art.mjs` 生成 85×85 与 256×256 PNG，打包脚本再保存为不依赖 `.import` 的 `.res` 纹理。
模型、资源路径和稀有度位于 `game/Spire408/Models/StudyBook.cs`。
名称、规则描述与备注位于 `game/Spire408/localization/zhs/relics.json`；`.description` 为加成及奖励规则，`.flavor` 按用户原文保留教材文案，使用原版遗物详情页的下方备注区域。

学识徽记母版为 `art/knowledge-resource-transparent.png`，使用内置 imagegen 生成，真实透明背景。构建器输出 256×256 纹理；数字由场景标签实时叠加。
