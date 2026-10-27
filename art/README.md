# 408计算机专业课

遗物名称：408计算机专业课。

备注（原描述）：一本平平无奇的专业课教材，不知道为什么透露着地狱的气息。0.6.1 起主描述改为加成和奖励规则，此文案移至原版下方备注栏。

绘图使用本机 imagen CLI，模型别名 `gpt-image-2.5`，实际映射为 `gpt-image-2.5-sunburst`，渠道自动选择 `packy-cf`。
任务 ID：`20261022T015017-4e7fd9eae2c8`。
原始图为 `generated/textbook-408.png`，PNG、1024×1024。完整提示词保存在 `408-textbook-prompt.txt`。
参考图只用于匹配本机原版遗物的描边、色块和光照风格，不作为本项目分发素材。

该渠道不支持背景参数，原图把棋盘格画进了 RGB 背景。随后使用内置图像编辑工具去除棋盘格，形成真正带 Alpha 的 `textbook-408-transparent.png` 母版。
透明母版为 PNG、1254×1254、RGBA。只做了透明背景清理，保留教材造型和 408 封面。资源构建器校验 Alpha，再输出 85×85 背包图标和 256×256 详情图。

玩法说明、大小图和轮廓均已在游戏中验证；母版可继续用于后续修改。

## 学识资源徽记

使用内置 imagegen 工具新建透明图像，工具模式为生成、`transparent_background=true`。
母版：`knowledge-resource-transparent.png`。完整提示词见 `knowledge-resource-prompt.txt`。
金色手绘边框、青绿色晶面与书页符号配合原版幻想风格；中央不绘制数字，由战斗场景实时覆盖剩余次数。
构建器验证真实 Alpha 并输出 `game/Spire408/Art/knowledge_resource.png`（256×256）；打包时生成同名 `.res`。
