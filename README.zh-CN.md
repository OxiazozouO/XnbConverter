# XnbConverter

一个高效率的xnb转化工具，你可以在10秒内完成解包任务(Xact相关文件需要久一点)
本项目为js项目xnbcli的c#移植扩展版本，并且对其功能不足之处进行补充，其中主要参考了monogame的源代码的解析的大部分，但是现在对于自定义数据结构的xnb解析支持主要还是用硬编码，希望以后能更灵活的处理这种xnb。

## 性能对比

| 工具   | XnbConverter | [StardewXnbHack](https://github.com/Pathoschild/StardewXnbHack) | [xnbcli](https://github.com/LeonBlade/xnbcli/) | [XNBExtract](https://community.playstarbound.com/threads/110976) |
|------|--------------|-----------------------------------------------------------------|------------------------------------------------|------------------------------------------------------------------|
| 拆包时间 | 0m7s         | ≈0m 43s                                                         | ≈6m 5s                                         | ≈2m 20s                                                          |

## 关于

创建者：oxiaozouo by [1714050472@qq.com](1714050472@qq.com)
版本：0.1.0-alpha
语言：C# .NET 6

## 支持的转码内容

你可以非常方便的将xnb文件转化为源资源格式， 目前已知适用[Stardew Valley]、[Terraria(部分)]。

#### 可相互转换：

| 导出的文件        | 说明                                                                     |
|--------------|------------------------------------------------------------------------|
| ".json"      | 某个类的json形式，一般是游戏中某个行为的配置文件、目前可以支持星露谷1.5的结构化数据，星露谷1.5的结构化数据可以自行查看游戏wiki |
| ".png"       | 游戏贴图素材,一般有人物肖像、物品贴图、地图的图块集等                                            |
| ".cso"       | 着色器，但是此文件为xnb的一部分，尚未进一步的解析                                             |
| ".tbin"      | 地图，可以用[tiled](https://www.mapeditor.org/)进行编辑                          |
| ".xml"       | 某个类的xml形式，一般为字体文件                                                      |
| ".json .png" | 一般为字体，json为字体的裁剪信息和字形信息，png为字体的图片                                      |
| ".json .wav" | 一般为SoundEffect，就是游戏音乐、游戏音效等音乐文件                                        |
|              |                                                                        |

#### 仅拆包：

| 导入的文件  | 说明                                                 |
|--------|----------------------------------------------------|
| ".xwb" | 解包成若干个wav文件   为SoundEffect的合集，Xact的一部分，扩展了更多音频处理功能 |

## 命令使用说明：

#### 拆包：

```
unpack -c -i "xnb文件/xnb文件夹" -o "导出目录"
```

​ 匹配 "xnb文件/xnb文件夹" 里的所有xnb文件，并且导出源文件和xnb配置文件到"导出目录"

#### 打包：

```
pack -c -i "xnb文件/xnb文件夹" -o "导出目录"
```

​ 匹配 "xnb文件/xnb文件夹" 里的所有.config文件，并且编译xnb文件到"导出目录"

#### 自动转换：

```
auto -c -i "文件/文件夹" -o "导出目录"
```

​ 在 "文件/文件夹" 匹配.xnb和.config等文件，自动拆包和打包并且生成文件到 "导出目录"

你可以打开程序界面输入这些指令，也可以直接点击pack.bat、unpack.bat等批处理文件快速执行

#### 参数说明：

-c ：启用并行处理(启用这个功能是本项目区别于xnbcli的主要特性)
通过对文件的并行处理、你可以将打包/解包任务消耗的时间在一定范围内缩减n倍，
运行内存也会对应的增加，你可以设置：.config/config.json的"Concurrency"的值，以控制并行处理的数量。
-i ：输入文件/文件夹
-o ：输出目录

#### 配置文件说明：

{
"LogTime" : true, //true 或 false 这个选项决定是否打印日志的时候打印时间
"TimeFormat" : "MM-dd HH:mm:ss", //"yyyy-MM-dd HH:mm:ss"、"MM-dd HH:mm:ss"等 这个参数决定打印时间的格式、更多格式请参考字符串日期格式
"LogPrintingOptions" : "Info, Warn, Error", //Info, Warn, Error, Debug （只有四个可选项）
日志的打印选项，决定在控制台打印的信息的类型，选项之间请用英文“,”隔开
"LogSaveOptions" : "Error", //Info, Warn, Error, Debug （只有四个可选项） 保存日志的选项，决定程序保存什么类型的日志到日志文件，选项之间请用英文“,”隔开
"Concurrency" : 15 // Concurrency is   >0 and < 16 推荐设置 15 并行处理文件的数量，推荐15个
}

## xnb模组的简易教程：

1、使用本软件解包。
2、你可以修改原文件，比如美化图片、更换字体、更换你修改后的json、地图等
3、你也可以新增xnb，比如在星露谷修改一个地图：
首先你准备好已经改好的"地图.tbin"，新增的"图块集.png"，然后复制一份"其他图块集.config"，
把文件名字改成"图块集.config"。
4、打包回去，本项目对于错误的数据格式会进行报错并中断该任务，请你在修改xnb里的数据时保证数据格式正确。

## 注意：

本项目处于开发阶段（暂且弃坑）、仅供学习交流使用，请勿商用。对于制作出来的xnb文件涉及的版权问题（字体版权、美术资源版权、原作者版权等），请你自行斟酌、本项目不承担任何责任。

对于程序运行时出现的任何问题，请你联系邮箱1714050472@qq.com，我会尽快解决。

## 版权信息：

### 1、[xnbcli](https://github.com/LeonBlade/xnbcli)

本项目在xnbcli项目的基础上进行c#移植，并补充功能

### 2、[unxwb](https://github.com/mariodon/unxwb)

本项目对WaveBank的导出处理来自unxwb。并遵循其 GPL协议

### 3、[monogame](https://github.com/MonoGame/MonoGame)

本项目的AudioEngine、SoundBank、WaveBank等读取处理部分的代码主要来自monogame项目。

### 4、[TConvert](https://github.com/trigger-segfault/TConvert)

本项目对SoundEffect的读取参考TConvert的WavConverter.cs的代码部分以及ffmpeg对音频的处理，并且遵循其 GPL 协议
并且完成了对SoundEffect的写入处理

### 5、[FFmpeg](http://ffmpeg.org)

本项目对音频的转码处理主要来自ffmpeg，并遵循其 LGPL 公共许可协议。

### 6、[TbinCSharp](https://github.com/spacechase0/TbinCSharp)

本项目对于Tbin的读取来自TbinCSharp，并遵循其 MIT 许可协议。
完成了tbin写入xnb的功能

### 7、[LibSquishNet](https://github.com/MaxxWyndham/LibSquishNet)

本项目对dxt压缩、dxt压缩等处理的代码来自LibSquishNet，并遵循其 MIT 许可协议。
主要对代码进行性能优化，性能显著提升，并集成到本项目之中。

### 8、[LzxDecoder.cs](https://github.com/MonoGame/MonoGame/blob/master/MonoGame.Framework/Content/LzxDecoder.cs)

本项目对lzx压缩处理的代码来自LzxDecoder.cs,并遵循其 LGPL MS-PL 等协议
原项目地址为：[lzx](https://www.cabextract.org.uk/libmspack/)

本项目遵循 GPL 协议

## 使用到的库：

### 1、[CommandLineParser](https://www.google.com/search?q=CommandLineParser)

### 2、[LZ4PCL](https://github.com/zenith-nz/LZ4PCL)

### 3、[Newtonsoft.Json](https://www.newtonsoft.com/json)

### 4、[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)
