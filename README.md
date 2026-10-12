# EPIC 机械臂上下料小游戏

使用 Unity 6.6（6000.6.0f1）打开项目，在 `Assets/Scenes/RobotMachineGrab.unity` 中点击 Play。

- 单击机械臂：输送带送料并停稳 → 抓取原料 → 开门上料 → 机械臂退出、关门加工 → 工作台返回并开门 → 取出阶梯轴并放到成品台。
- 双击机械臂：切换损坏或修复，损坏期间流程暂停。
- 右键拖动旋转视角，滚轮缩放，中键平移，R 恢复视角。
- 指示灯：绿色正常/加工，黄色转移动作，红色故障。

模型通过 Git LFS 保存，下载时请使用已安装 Git LFS 的 Git 客户端：

```sh
git lfs install
git clone https://github.com/scy8848/EPIC.git
cd EPIC
git lfs pull
```

Game 窗口取消勾选 `Low Resolution Aspect Ratios`，避免低分辨率预览放大。详细实现和操作说明见 [RobotGrab_README.md](RobotGrab_README.md)。

这是固定工位布局的交互原型，加工以动画和工件模型切换表示，不模拟真实切削或工业夹持力。
